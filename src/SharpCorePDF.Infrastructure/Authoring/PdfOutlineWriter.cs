using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Writes the bookmark tree of an authored document: one object per bookmark, with its title, its
/// destination and its children, hung off the catalog's <c>/Outlines</c> entry.
/// </summary>
/// <remarks>
/// A bookmark names a page by the one-based number a caller sees, and this writer turns it into the
/// object that page was given. Because pages can still be added after the bookmark is stated, the page
/// numbers are checked here, when the document is written, rather than when the tree is set.
/// </remarks>
internal static class PdfOutlineWriter
{
    /// <summary>Writes the outline tree and returns the object number of its root.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="bookmarks">The top level bookmarks, in document order.</param>
    /// <param name="pageNumbers">The object number of each page of the document, in order.</param>
    /// <param name="hash">The digest that identifies the document, which receives the tree, or <see langword="null"/> when the caller keeps a digest of its own.</param>
    /// <returns>The object number of the outline root.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A bookmark names a page the document does not have.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">A bookmark names a destination an authored document cannot write.</exception>
    public static int Write(
        PdfWriter writer,
        IReadOnlyList<PdfOutlineItem> bookmarks,
        IReadOnlyList<int> pageNumbers,
        IncrementalHash? hash = null)
    {
        var rootNumber = writer.AllocateObject();
        var roots = new List<OutlineEntry>(bookmarks.Count);

        foreach (var bookmark in bookmarks)
        {
            roots.Add(Build(writer, bookmark, rootNumber));
        }

        for (var index = 0; index < roots.Count; index++)
        {
            WriteEntry(writer, roots[index], roots, index, pageNumbers, hash);
        }

        writer.WriteObject(
            rootNumber,
            new PdfDictionary()
                .SetName("Type", "Outlines")
                .Set("First", new PdfReference(roots[0].Number, 0))
                .Set("Last", new PdfReference(roots[^1].Number, 0))
                .SetInteger("Count", Count(roots)));

        return rootNumber;
    }

    /// <summary>Reserves an object for every bookmark of a subtree, so siblings and parents can name each other.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="item">The bookmark.</param>
    /// <param name="parentNumber">The object number of the bookmark above it.</param>
    /// <returns>The entry that holds the reserved numbers.</returns>
    private static OutlineEntry Build(PdfWriter writer, PdfOutlineItem item, int parentNumber)
    {
        var entry = new OutlineEntry(writer.AllocateObject(), item, parentNumber);

        foreach (var child in item.Children)
        {
            entry.Children.Add(Build(writer, child, entry.Number));
        }

        return entry;
    }

    /// <summary>Writes one bookmark and everything below it.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="entry">The bookmark to write.</param>
    /// <param name="siblings">The bookmarks that share its parent, in order.</param>
    /// <param name="index">Its position among those siblings.</param>
    /// <param name="pageNumbers">The object number of each page of the document, in order.</param>
    /// <param name="hash">The digest of the document, or <see langword="null"/> when no digest is kept.</param>
    private static void WriteEntry(
        PdfWriter writer,
        OutlineEntry entry,
        List<OutlineEntry> siblings,
        int index,
        IReadOnlyList<int> pageNumbers,
        IncrementalHash? hash)
    {
        var dictionary = new PdfDictionary()
            .Set("Parent", new PdfReference(entry.Parent, 0))
            .Set("Title", PdfString.FromText(entry.Item.Title));

        if (index > 0)
        {
            dictionary.Set("Prev", new PdfReference(siblings[index - 1].Number, 0));
        }

        if (index + 1 < siblings.Count)
        {
            dictionary.Set("Next", new PdfReference(siblings[index + 1].Number, 0));
        }

        if (entry.Children.Count > 0)
        {
            // A closed bookmark states the count of its descendants with a negative sign, which is how
            // a viewer knows not to show them; the sign is the only difference between the two states.
            var descendants = Count(entry.Children);

            dictionary
                .Set("First", new PdfReference(entry.Children[0].Number, 0))
                .Set("Last", new PdfReference(entry.Children[^1].Number, 0))
                .SetInteger("Count", entry.Item.IsOpen ? descendants : -descendants);
        }

        var (destination, action) = ReadDestination(entry.Item, pageNumbers);

        if (destination is not null)
        {
            dictionary.Set("Dest", destination);
        }

        if (action is not null)
        {
            dictionary.Set("A", action);
        }

        writer.WriteObject(entry.Number, dictionary);

        hash?.AppendData(Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Item.Title}|{entry.Item.Destination}|{entry.Item.IsOpen}")));

        for (var child = 0; child < entry.Children.Count; child++)
        {
            WriteEntry(writer, entry.Children[child], entry.Children, child, pageNumbers, hash);
        }
    }

    /// <summary>Turns what a bookmark points at into the entries PDF states it with.</summary>
    /// <param name="item">The bookmark.</param>
    /// <param name="pageNumbers">The object number of each page of the document, in order.</param>
    /// <returns>A destination array, an action dictionary, or neither when the bookmark points nowhere.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bookmark names a page the document does not have.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">The destination is one an authored document cannot write.</exception>
    private static (PdfObject? Destination, PdfObject? Action) ReadDestination(
        PdfOutlineItem item,
        IReadOnlyList<int> pageNumbers)
    {
        if (item.Destination is not { } destination)
        {
            return (null, null);
        }

        if (destination.Uri is { } uri)
        {
            return (null, new PdfDictionary().SetName("S", "URI").Set("URI", PdfString.FromText(uri)));
        }

        if (destination.FileName is { } file)
        {
            throw new PdfUnsupportedFeatureException(
                "an external file destination",
                $"the bookmark '{item.Title}' points at the file '{file}'. A bookmark of an authored document points at one of its pages or at a URL.");
        }

        if (destination.PageNumber is { } page)
        {
            if (page < 1 || page > pageNumbers.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(item),
                    page,
                    $"The bookmark '{item.Title}' points at page {page}, but the document has {pageNumbers.Count} page(s).");
            }

            var target = new PdfArray([new PdfReference(pageNumbers[page - 1], 0)]);

            if (destination.Area is { } area)
            {
                // A stated area becomes the top left corner it names, with the zoom left to the viewer:
                // a destination cannot state a scale and mean anything but the viewer's own one.
                target
                    .Add(new PdfName("XYZ"))
                    .Add(new PdfReal(area.Left))
                    .Add(new PdfReal(area.Top))
                    .Add(PdfNull.Instance);
            }
            else
            {
                target.Add(new PdfName("Fit"));
            }

            return (target, null);
        }

        if (destination.NamedDestination is { } name)
        {
            throw new PdfUnsupportedFeatureException(
                "a named destination",
                $"the bookmark '{item.Title}' points at the name '{name}', and an authored document writes no name tree to resolve it against.");
        }

        return (null, null);
    }

    /// <summary>Counts the bookmarks of a subtree, including the ones at its own level.</summary>
    /// <param name="entries">The bookmarks of one level.</param>
    /// <returns>The number of bookmarks at that level and below.</returns>
    private static int Count(List<OutlineEntry> entries)
    {
        var count = 0;

        foreach (var entry in entries)
        {
            count += 1 + Count(entry.Children);
        }

        return count;
    }
}

/// <summary>One bookmark while the tree is being written: its number, its value and its children.</summary>
/// <param name="number">The object number reserved for it.</param>
/// <param name="item">The bookmark the caller supplied.</param>
/// <param name="parent">The object number of the bookmark above it, or of the outline root.</param>
internal sealed class OutlineEntry(int number, PdfOutlineItem item, int parent)
{
    /// <summary>Gets the object number reserved for the bookmark.</summary>
    public int Number { get; } = number;

    /// <summary>Gets the bookmark the caller supplied.</summary>
    public PdfOutlineItem Item { get; } = item;

    /// <summary>Gets the object number of the bookmark above it.</summary>
    public int Parent { get; } = parent;

    /// <summary>Gets the bookmarks below it, in order.</summary>
    public List<OutlineEntry> Children { get; } = [];
}
