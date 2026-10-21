using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The engine's implementation of <see cref="IPdfPageContentReader"/>: it opens a document, walks the
/// structures that hold its content, and returns immutable descriptions of what it found.
/// </summary>
/// <remarks>
/// <para>
/// The reader is a set of partial files, one per capability, because the capabilities share the walk
/// of the page tree, the resolution of resources and the decoding of streams but not much else.
/// Nothing here writes: no destination is opened that the caller did not hand in, and no temporary
/// file is created (NFR-7.7).
/// </para>
/// <para>
/// A document that cannot be opened because it is encrypted yields empty results and the warning that
/// says so, exactly as inspection reports it; a document whose streams cannot be decoded yields the
/// entries it could read plus a warning per entry it could not (NFR-7.6, NFR-7.8).
/// </para>
/// </remarks>
public sealed partial class PdfPageContentReader : IPdfPageContentReader
{
    private readonly PdfEngineOptions options;

    /// <summary>Initializes a new instance of the <see cref="PdfPageContentReader"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfPageContentReader(PdfEngineOptions? options = null) => this.options = options ?? PdfEngineOptions.Default;

    /// <summary>Gets the options this reader was created with.</summary>
    public PdfEngineOptions Options => options;

    /// <summary>Reads a document into memory and opens it.</summary>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="content">The stream to read; it is not disposed.</param>
    /// <param name="password">The password, when the document is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The opened document.</returns>
    private async Task<PdfFileReader> OpenAsync(
        string sourceName,
        Stream content,
        string? password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);

        var bytes = await PdfByteReader
            .ReadAllAsync(content, options.MaxSourceBytes, sourceName, cancellationToken)
            .ConfigureAwait(false);

        return new PdfFileReader(bytes, sourceName, options, password);
    }

    /// <summary>Selects the pages a range names, clamped to the pages the document has.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="range">The requested range.</param>
    /// <returns>The pages that exist inside the range, in document order.</returns>
    /// <remarks>
    /// A range that starts past the last page selects nothing rather than throwing: reading is a
    /// question about a document, and "there is no page 500 in a three page file" is an answer, not an
    /// error.
    /// </remarks>
    private static List<SourcePage> SelectPages(PdfFileReader reader, PageRange range)
    {
        var pages = reader.Pages;
        var from = Math.Max(1, range.From);
        var to = range.IsOpenEnded ? pages.Count : Math.Min(range.To, pages.Count);
        var selected = new List<SourcePage>();

        for (var page = from; page <= to; page++)
        {
            selected.Add(pages[page - 1]);
        }

        return selected;
    }

    /// <summary>Adds the remark that explains why an encrypted document produced nothing.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="warnings">The list that receives the remark.</param>
    private static void NoteClosedEncryption(PdfFileReader reader, List<PdfWarning> warnings)
    {
        if (reader.IsEncrypted && reader.Security is null)
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionNotOpened,
                $"'{reader.SourceName}': the document is encrypted and could not be opened, so its content cannot be read."));
        }
    }

    /// <summary>Builds a result from the items found and the remarks made.</summary>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="reader">The document, whose own remarks are folded in.</param>
    /// <param name="items">The items that were read.</param>
    /// <param name="warnings">The remarks made while reading.</param>
    /// <returns>The result.</returns>
    private static PdfReadResult<T> Result<T>(
        PdfFileReader reader,
        IEnumerable<T> items,
        List<PdfWarning> warnings) => new()
        {
            Items = [.. items],
            Warnings = [.. reader.Warnings, .. warnings],
        };

    /// <summary>Reads the resources of a page, which it may inherit from its ancestors.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="page">The page.</param>
    /// <returns>The resources of the page, or <see langword="null"/> when it has none.</returns>
    private static PdfDictionary? PageResources(PdfFileReader reader, SourcePage page) =>
        reader.ResolveDictionary(page.Dictionary.Get("Resources"))
        ?? reader.ResolveDictionary(page.Inherited.Get("Resources"));

    /// <summary>Reads a number that may be stored directly or behind a reference.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    private static double? ReadNumber(PdfFileReader reader, PdfObject? value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        PdfReference reference => ReadNumber(reader, reader.Resolve(reference)),
        _ => null,
    };

    /// <summary>Reads a rectangle that may be stored directly or behind a reference.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value.</param>
    /// <returns>The normalised rectangle, or <see langword="null"/> when it cannot be read.</returns>
    private static PdfRectangle? ReadRectangle(PdfFileReader reader, PdfObject? value)
    {
        var array = reader.ResolveArray(value);

        if (array is null || array.Count < 4)
        {
            return null;
        }

        var edges = new double[4];

        for (var index = 0; index < edges.Length; index++)
        {
            if (ReadNumber(reader, array.Items[index]) is not { } number)
            {
                return null;
            }

            edges[index] = number;
        }

        return PdfRectangle.FromCorners(edges[0], edges[1], edges[2], edges[3]);
    }

    /// <summary>Reads text or a name that may be stored directly or behind a reference.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string? ReadText(PdfFileReader reader, PdfObject? value) => value switch
    {
        PdfString text => text.ToText(),
        PdfName name => name.Value,
        PdfReference reference => ReadText(reader, reader.Resolve(reference)),
        _ => null,
    };

    /// <summary>Reads a name that may be stored directly or behind a reference.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="value">The value.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    private static string? ReadName(PdfFileReader reader, PdfObject? value) => value switch
    {
        PdfName name => name.Value,
        PdfReference reference => ReadName(reader, reader.Resolve(reference)),
        _ => null,
    };

    /// <summary>Maps the object number of every page to its one-based number.</summary>
    /// <param name="reader">The document.</param>
    /// <returns>The map.</returns>
    private static Dictionary<long, int> BuildPageMap(PdfFileReader reader)
    {
        var map = new Dictionary<long, int>(reader.Pages.Count);

        foreach (var page in reader.Pages)
        {
            map[page.ObjectNumber] = page.Index + 1;
        }

        return map;
    }
}
