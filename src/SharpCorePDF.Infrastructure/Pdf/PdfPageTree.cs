namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>A page of a source document, together with the attributes it inherits from its ancestors.</summary>
/// <param name="ObjectNumber">The object number of the page.</param>
/// <param name="Index">The zero-based index of the page in the document.</param>
/// <param name="Dictionary">The page dictionary as stored in the file.</param>
/// <param name="Inherited">The inheritable attributes contributed by the ancestors of the page.</param>
internal sealed record SourcePage(long ObjectNumber, int Index, PdfDictionary Dictionary, PdfDictionary Inherited);

/// <summary>The result of walking a page tree.</summary>
internal sealed class PageTreeWalk
{
    /// <summary>Gets the pages in document order.</summary>
    public List<SourcePage> Pages { get; } = [];

    /// <summary>
    /// Gets the object numbers of the intermediate page tree nodes. A merge replaces the whole tree,
    /// so references to these nodes are redirected to the merged node instead of being copied.
    /// </summary>
    public List<long> NodeObjectNumbers { get; } = [];
}

/// <summary>
/// Walks the page tree of a document and materialises inherited attributes onto every page.
/// </summary>
/// <remarks>
/// Inheritance is resolved at read time on purpose. The merged document puts every page directly
/// under one flat page tree node, which is what makes merging order and partial selection possible;
/// the attributes a page used to inherit therefore have to travel with the page itself.
/// </remarks>
internal static class PdfPageTree
{
    /// <summary>The attributes a page may inherit from its ancestors, per the PDF specification.</summary>
    private static readonly string[] InheritableAttributes = ["Resources", "MediaBox", "CropBox", "Rotate"];

    /// <summary>Walks the page tree of a document.</summary>
    /// <param name="reader">The document to walk.</param>
    /// <param name="warnings">The list that receives non-fatal remarks.</param>
    /// <param name="maxDepth">The maximum depth of the tree.</param>
    /// <returns>The pages and the intermediate node object numbers.</returns>
    public static PageTreeWalk Walk(PdfFileReader reader, List<string> warnings, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var result = new PageTreeWalk();
        var catalog = reader.Catalog;

        if (catalog?.Get("Pages") is not { } root)
        {
            warnings.Add($"'{reader.SourceName}': the document catalog has no page tree, so the document contributes no pages.");
            return result;
        }

        new PageTreeWalker(reader, result, warnings, maxDepth).Visit(root, inherited: null, depth: 0);
        return result;
    }

    /// <summary>The state one walk of a page tree carries through the recursion.</summary>
    private sealed class PageTreeWalker
    {
        private readonly PdfFileReader reader;
        private readonly PageTreeWalk result;
        private readonly List<string> warnings;
        private readonly int maxDepth;
        private readonly HashSet<long> visited = [];

        /// <summary>Initializes a new instance of the <see cref="PageTreeWalker"/> class.</summary>
        /// <param name="reader">The document to walk.</param>
        /// <param name="result">The pages and the intermediate node object numbers found so far.</param>
        /// <param name="warnings">The list that receives non-fatal remarks.</param>
        /// <param name="maxDepth">The maximum depth of the tree.</param>
        public PageTreeWalker(PdfFileReader reader, PageTreeWalk result, List<string> warnings, int maxDepth)
        {
            this.reader = reader;
            this.result = result;
            this.warnings = warnings;
            this.maxDepth = maxDepth;
        }

        /// <summary>Walks a node of the tree and everything below it.</summary>
        /// <param name="nodeValue">The value of the node.</param>
        /// <param name="inherited">The attributes the ancestors of the node contribute.</param>
        /// <param name="depth">The depth of the node.</param>
        public void Visit(PdfObject nodeValue, PdfDictionary? inherited, int depth)
        {
            if (depth > maxDepth)
            {
                warnings.Add($"'{reader.SourceName}': the page tree is deeper than {maxDepth} levels and was cut off.");
                return;
            }

            var objectNumber = nodeValue is PdfReference reference ? reference.ObjectNumber : (long?)null;

            if (objectNumber is { } number && !visited.Add(number))
            {
                warnings.Add($"'{reader.SourceName}': the page tree contains a cycle at object {number}; the branch was skipped.");
                return;
            }

            var node = reader.ResolveDictionary(nodeValue);

            if (node is null)
            {
                warnings.Add($"'{reader.SourceName}': a page tree node could not be resolved and was skipped.");
                return;
            }

            var effective = MergeInheritedAttributes(node, inherited);

            if (node.Get("Kids") is PdfArray kids)
            {
                if (node.GetName("Type") is not null and not "Pages")
                {
                    warnings.Add(
                        $"'{reader.SourceName}': object {Format(objectNumber)} has children but is not typed as a page tree node.");
                }

                if (objectNumber is { } nodeNumber)
                {
                    result.NodeObjectNumbers.Add(nodeNumber);
                }

                foreach (var kid in kids.Items)
                {
                    Visit(kid, effective, depth + 1);
                }

                return;
            }

            if (objectNumber is { } pageNumber)
            {
                result.Pages.Add(new SourcePage(pageNumber, result.Pages.Count, node, effective ?? new PdfDictionary()));
                return;
            }

            warnings.Add(
                $"'{reader.SourceName}': a page stored inline in the page tree cannot be referenced and was skipped.");
        }
    }

    private static string Format(long? objectNumber) => objectNumber is { } number
        ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "?";

    private static PdfDictionary? MergeInheritedAttributes(PdfDictionary node, PdfDictionary? inherited)
    {
        PdfDictionary? effective = inherited;

        foreach (var key in InheritableAttributes)
        {
            if (!node.ContainsKey(key))
            {
                continue;
            }

            effective = effective is null ? new PdfDictionary() : new PdfDictionary(effective.Entries);
            effective.Set(key, node.Get(key));
        }

        return effective;
    }
}
