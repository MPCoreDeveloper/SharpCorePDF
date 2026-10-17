using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The bookmark half of <see cref="PdfMergeEngine"/>.
/// </summary>
/// <remarks>
/// The outline trees of all sources are merged into one tree whose top level items follow the source
/// order. A destination that points at a page is carried over automatically, because the page object
/// it refers to is remapped while it is copied; a named destination survives only for the primary
/// source, whose name trees are carried over as well.
/// </remarks>
public sealed partial class PdfMergeEngine
{
    /// <summary>The largest number of outline items that is copied, as a guard against damaged trees.</summary>
    private const int MaxOutlineItems = 100_000;

    /// <summary>The name of the entry that holds the first child of a bookmark or of the outline root.</summary>
    private const string FirstEntry = "First";

    /// <summary>The name of the entry that states how many items a bookmark has open below it.</summary>
    private const string CountEntry = "Count";

    /// <summary>Copies the outline trees of every source into one merged outline.</summary>
    /// <param name="context">The merge context.</param>
    /// <returns>The reference to the root of the merged outline, or <see langword="null"/>.</returns>
    private static PdfReference? MergeOutlines(PdfMergeContext context)
    {
        if (context.Output.OutlinePolicy != OutlinePolicy.Preserve)
        {
            foreach (var source in context.Sources.Where(source => source.Resolved.Info.HasOutlines))
            {
                context.Warnings.Add(new(
                    PdfWarningKind.OutlinesDroppedByPolicy,
                    $"'{source.DisplayName}': its {source.Resolved.Info.OutlineItemCount} bookmark(s) were dropped by the configured outline policy."));
            }

            return null;
        }

        var parent = new OutlineParent();
        var sections = new List<OutlineChain>();
        var linkages = new List<OutlineLinkage>();

        foreach (var source in context.Sources)
        {
            var outline = source.Reader.ResolveDictionary(source.Reader.Catalog?.Get("Outlines"));

            if (outline is null)
            {
                continue;
            }

            var linkage = new OutlineLinkage();
            var nodes = ReadChain(source.Reader, outline.Get(FirstEntry), context, source);
            var chain = CopyChain(context, source, nodes, parent, linkage);

            if (!chain.IsEmpty)
            {
                sections.Add(chain);
                linkages.Add(linkage);
            }
        }

        if (sections.Count == 0)
        {
            return null;
        }

        // The top level bookmarks of every source form one chain, in source order.
        for (var index = 0; index < sections.Count; index++)
        {
            if (index > 0)
            {
                linkages[index].Previous = sections[index - 1].LastTarget;
            }

            if (index + 1 < sections.Count)
            {
                linkages[index].Next = sections[index + 1].FirstTarget;
            }
        }

        var rootTarget = context.Writer.AllocateObject();
        parent.Target = rootTarget;

        var root = new PdfDictionary();
        root.Set("Type", new PdfName("Outlines"));
        root.Set(FirstEntry, new PdfReference(sections[0].FirstTarget, 0));
        root.Set("Last", new PdfReference(sections[^1].LastTarget, 0));
        root.SetInteger(CountEntry, sections.Sum(section => section.TotalItems));

        context.Writer.WriteObject(rootTarget, root);

        return new PdfReference(rootTarget, 0);
    }

    /// <summary>Reads a chain of sibling bookmarks, starting at <paramref name="first"/>.</summary>
    /// <param name="reader">The document being read.</param>
    /// <param name="first">The <c>/First</c> entry of the parent, or the <c>/Next</c> chain start.</param>
    /// <param name="context">The merge context.</param>
    /// <param name="source">The source being read.</param>
    /// <returns>The items of the chain, in document order.</returns>
    private static List<OutlineNode> ReadChain(
        PdfFileReader reader,
        PdfObject? first,
        PdfMergeContext context,
        MergeSource source)
    {
        var nodes = new List<OutlineNode>();

        if (first is null)
        {
            return nodes;
        }

        PdfReference? reference = first as PdfReference;

        if (reference is null)
        {
            context.Warnings.Add(new(
                PdfWarningKind.BookmarkSkipped,
                $"'{source.DisplayName}': a bookmark that is not stored as an indirect object was skipped."));
            return nodes;
        }

        while (nodes.Count < MaxOutlineItems && reference is not null)
        {
            var dictionary = reader.ResolveDictionary(reference);

            if (dictionary is null)
            {
                break;
            }

            nodes.Add(new OutlineNode(reference.ObjectNumber, dictionary));
            reference = dictionary.GetReference("Next");
        }

        if (nodes.Count >= MaxOutlineItems)
        {
            context.Warnings.Add(new(
                PdfWarningKind.BookmarksTruncated,
                $"'{source.DisplayName}': more than {MaxOutlineItems} bookmarks were found; the rest was skipped."));
        }

        return nodes;
    }

    /// <summary>Copies a chain of sibling bookmarks and everything below it.</summary>
    /// <param name="context">The merge context.</param>
    /// <param name="source">The source the bookmarks come from.</param>
    /// <param name="nodes">The bookmarks of this level, in document order.</param>
    /// <param name="parent">The object number the bookmarks must point at as their parent.</param>
    /// <param name="linkage">The chain this level belongs to, used for links to the sibling level.</param>
    /// <returns>What was copied.</returns>
    private static OutlineChain CopyChain(
        PdfMergeContext context,
        MergeSource source,
        List<OutlineNode> nodes,
        OutlineParent parent,
        OutlineLinkage linkage)
    {
        if (nodes.Count == 0)
        {
            return default;
        }

        var targets = new int[nodes.Count];

        for (var index = 0; index < nodes.Count; index++)
        {
            targets[index] = source.Copier.Reserve(nodes[index].ObjectNumber);
        }

        var descendants = 0;

        for (var index = 0; index < nodes.Count; index++)
        {
            var position = index;
            var node = nodes[position];
            var itemParent = new OutlineParent { Target = targets[position] };
            var children = CopyChain(
                context,
                source,
                ReadChain(source.Reader, node.Dictionary.Get(FirstEntry), context, source),
                itemParent,
                new OutlineLinkage());

            descendants += children.TotalItems;

            var isOpen = (node.Dictionary.GetInteger(CountEntry) ?? 0) >= 0;
            var count = children.FirstTarget > 0 ? OutlineCount(children.TotalItems, isOpen) : 0;
            var isPrimary = source.Resolved.Source.Id == context.Request.PrimarySourceId;
            var placement = new BookmarkPlacement(parent, children, targets, position, linkage, count, isPrimary);

            source.Copier.Override(node.ObjectNumber, body => RewriteBookmark(context, source, body, placement));
        }

        return new OutlineChain(targets[0], targets[^1], nodes.Count, descendants);
    }

    /// <summary>A parent object number that is filled in after the outline has been walked.</summary>
    private sealed class OutlineParent
    {
        /// <summary>Gets or sets the object number of the parent.</summary>
        public int Target { get; set; }
    }

    /// <summary>The links of a chain of sibling bookmarks to the levels around it.</summary>
    private sealed class OutlineLinkage
    {
        /// <summary>Gets or sets the object number of the last bookmark of the previous chain.</summary>
        public int Previous { get; set; }

        /// <summary>Gets or sets the object number of the first bookmark of the next chain.</summary>
        public int Next { get; set; }
    }

    /// <summary>
    /// Gives the value of the <c>/Count</c> entry of a bookmark: a negative count is how a reader is
    /// told that the bookmark starts out collapsed.
    /// </summary>
    /// <param name="totalItems">The number of bookmarks including everything below them.</param>
    /// <param name="isOpen">Whether the bookmark is meant to be shown expanded.</param>
    /// <returns>The count to write, or its negation for a collapsed bookmark.</returns>
    private static int OutlineCount(int totalItems, bool isOpen) => isOpen ? totalItems : -totalItems;

    /// <summary>Where a copied bookmark belongs in the merged outline.</summary>
    /// <param name="Parent">The parent the bookmark must point at.</param>
    /// <param name="Children">What was copied below the bookmark.</param>
    /// <param name="Targets">The target object numbers of the chain the bookmark belongs to.</param>
    /// <param name="Position">The position of the bookmark inside its chain.</param>
    /// <param name="Linkage">The links to the sibling chains.</param>
    /// <param name="Count">The value for the <c>/Count</c> entry, or zero.</param>
    /// <param name="IsPrimary">Whether the bookmark comes from the primary source.</param>
    private sealed record BookmarkPlacement(
        OutlineParent Parent,
        OutlineChain Children,
        int[] Targets,
        int Position,
        OutlineLinkage Linkage,
        int Count,
        bool IsPrimary);

    /// <summary>Rewrites a copied bookmark into its place in the merged outline.</summary>
    /// <param name="context">The merge context.</param>
    /// <param name="source">The source the bookmark comes from.</param>
    /// <param name="body">The copied bookmark dictionary.</param>
    /// <param name="placement">Where the bookmark belongs in the merged outline.</param>
    /// <returns>The rewritten bookmark.</returns>
    private static PdfDictionary RewriteBookmark(
        PdfMergeContext context,
        MergeSource source,
        PdfObject body,
        BookmarkPlacement placement)
    {
        var bookmark = body as PdfDictionary ?? new PdfDictionary();

        SetReference(bookmark, "Parent", placement.Parent.Target);
        SetReference(bookmark, FirstEntry, placement.Children.FirstTarget);
        SetReference(bookmark, "Last", placement.Children.LastTarget);
        SetReference(
            bookmark,
            "Prev",
            placement.Position > 0 ? placement.Targets[placement.Position - 1] : placement.Linkage.Previous);
        SetReference(
            bookmark,
            "Next",
            placement.Position + 1 < placement.Targets.Length ? placement.Targets[placement.Position + 1] : placement.Linkage.Next);

        if (placement.Count != 0)
        {
            bookmark.SetInteger(CountEntry, placement.Count);
        }
        else
        {
            bookmark.Remove(CountEntry);
        }

        // A named destination points into the name tree of a document; only the tree of the primary
        // source is carried over, so a bookmark of any other source must not keep pointing into a
        // tree that no longer exists.
        if (!placement.IsPrimary && bookmark.Get("Dest") is PdfName or PdfString)
        {
            bookmark.Remove("Dest");
            context.Warnings.Add(new(
                PdfWarningKind.BookmarkDestinationDropped,
                $"'{source.DisplayName}': a bookmark that used a named destination was kept without its destination, because named destinations are merged from the primary source only."));
        }

        return bookmark;
    }

    /// <summary>Sets an entry to a reference, or removes it when there is nothing to point at.</summary>
    /// <param name="dictionary">The dictionary to change.</param>
    /// <param name="key">The entry to set.</param>
    /// <param name="target">The object number to point at, or zero.</param>
    private static void SetReference(PdfDictionary dictionary, string key, int target)
    {
        if (target > 0)
        {
            dictionary.Set(key, new PdfReference(target, 0));
            return;
        }

        dictionary.Remove(key);
    }

    /// <summary>One bookmark of a source document.</summary>
    /// <param name="ObjectNumber">The object number of the bookmark.</param>
    /// <param name="Dictionary">The bookmark dictionary.</param>
    private sealed record OutlineNode(long ObjectNumber, PdfDictionary Dictionary);

    /// <summary>A copied chain of sibling bookmarks.</summary>
    /// <param name="FirstTarget">The target object number of the first sibling, or zero.</param>
    /// <param name="LastTarget">The target object number of the last sibling, or zero.</param>
    /// <param name="ItemCount">The number of siblings in the chain.</param>
    /// <param name="DescendantCount">The number of bookmarks below the chain.</param>
    private readonly record struct OutlineChain(int FirstTarget, int LastTarget, int ItemCount, int DescendantCount)
    {
        /// <summary>Gets the number of bookmarks in the chain including everything below it.</summary>
        public int TotalItems => ItemCount + DescendantCount;

        /// <summary>Gets a value indicating whether the chain contains no bookmark.</summary>
        public bool IsEmpty => ItemCount == 0;
    }
}
