namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Reads the cross-reference table of a document.
/// </summary>
/// <remarks>
/// Follows the <c>startxref</c> pointer, the <c>/Prev</c> chain of incremental updates and the hybrid
/// <c>/XRefStm</c> references, and falls back to scanning the file when the table is unusable.
/// </remarks>
internal sealed partial class PdfCrossReferenceReader
{
    private readonly byte[] data;
    private readonly string sourceName;
    private readonly PdfEngineOptions options;
    private readonly List<string> warnings;
    private readonly PdfCrossReferenceTable table = new();

    /// <summary>Initializes a new instance of the <see cref="PdfCrossReferenceReader"/> class.</summary>
    /// <param name="data">The bytes of the document.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="warnings">The list that receives non-fatal remarks.</param>
    public PdfCrossReferenceReader(byte[] data, string sourceName, PdfEngineOptions options, List<string> warnings)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.sourceName = sourceName;
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    /// <summary>Reads the table.</summary>
    /// <returns>The merged cross-reference table and trailer.</returns>
    /// <exception cref="SharpCorePDF.Domain.Exceptions.PdfSourceFormatException">The table is damaged beyond repair.</exception>
    public PdfCrossReferenceTable Read()
    {
        var pending = new Queue<(long Offset, bool IsNewest)>();
        var visited = new HashSet<long>();

        if (FindStartXref() is { } start)
        {
            table.StartXrefOffset = start;
            pending.Enqueue((start, true));
        }

        while (pending.Count > 0)
        {
            var (offset, isNewest) = pending.Dequeue();

            if (offset < 0 || offset >= data.Length || !visited.Add(offset))
            {
                continue;
            }

            var (previous, hybrid) = TryReadSection(offset, isNewest);

            if (previous is { } previousOffset && previousOffset > 0 && previousOffset < data.Length)
            {
                pending.Enqueue((previousOffset, false));
            }

            if (hybrid is { } hybridOffset && hybridOffset > 0 && hybridOffset < data.Length)
            {
                pending.Enqueue((hybridOffset, false));
            }
        }

        if (table.Count == 0 || !table.Trailer.ContainsKey("Root"))
        {
            Reconstruct();
        }

        if (!table.Trailer.ContainsKey("Root"))
        {
            throw new SharpCorePDF.Domain.Exceptions.PdfSourceFormatException(
                sourceName,
                "the document has no document catalog, so it has no pages to merge.");
        }

        return table;
    }

    private long? FindStartXref()
    {
        var searchStart = Math.Max(0, data.Length - 4096);
        var index = data.AsSpan(searchStart).LastIndexOf("startxref"u8);

        if (index < 0)
        {
            searchStart = 0;
            index = data.AsSpan().LastIndexOf("startxref"u8);
        }

        if (index < 0)
        {
            return null;
        }

        var lexer = new PdfLexer(data, searchStart + index + 9);

        return lexer.TryReadInteger(out var offset) ? offset : null;
    }

    private (long? Previous, long? Hybrid) TryReadSection(long offset, bool isNewest)
    {
        var probe = new PdfLexer(data, (int)offset);

        return probe.TryReadKeyword("xref")
            ? ReadClassicTable((int)offset, isNewest)
            : ReadCrossReferenceStream((int)offset, isNewest);
    }
}
