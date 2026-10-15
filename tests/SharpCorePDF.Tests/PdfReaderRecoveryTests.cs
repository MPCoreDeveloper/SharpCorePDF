using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the safety net for damaged documents: a file whose cross-reference table cannot be read is
/// rebuilt by scanning for object headers, or refused when that is switched off.
/// </summary>
/// <remarks>
/// The fixtures are real documents, so these tests damage them the way real damage happens — a table that no
/// longer parses, a <c>startxref</c> that points at the wrong place, a file that was cut in half — and then
/// ask for the same document back.
/// </remarks>
public sealed class PdfReaderRecoveryTests
{
    /// <summary>A table that no longer parses is rebuilt, and the document is read completely.</summary>
    [Fact]
    public async Task Rebuilds_a_cross_reference_table_that_does_not_parse()
    {
        var document = await ReadAsync(DamageTheTableKeyword(TestAssets.Outlined));

        Assert.Equal(3, document.PageCount);
        Assert.Contains(
            document.Warnings,
            warning => warning.Contains("rebuilt by scanning", StringComparison.Ordinal));
    }

    /// <summary>A table that is not where <c>startxref</c> announces it is recovered from as well.</summary>
    [Fact]
    public async Task Rebuilds_a_table_that_is_not_where_startxref_announces_it()
    {
        var document = await ReadAsync(PointStartxrefAtTheHeader(TestAssets.SafeWebCore));

        Assert.Equal(6, document.PageCount);
        Assert.Contains(
            document.Warnings,
            warning => warning.Contains("rebuilt by scanning", StringComparison.Ordinal));
    }

    /// <summary>A document whose catalog lives in an object stream cannot be rebuilt by scanning.</summary>
    /// <remarks>
    /// The scanner finds object headers in the bytes of the file, and the catalog of a document that keeps its
    /// objects in object streams is inside a compressed stream, where no scan can see it. Refusing the
    /// document is the honest answer; the alternative is a merge that silently contributes nothing.
    /// </remarks>
    [Fact]
    public async Task Refuses_a_document_whose_catalog_cannot_be_found_by_scanning()
    {
        var reader = new PdfDocumentReader(PdfEngineOptions.Default);

        using var content = new MemoryStream(PointStartxrefAtTheHeader(TestAssets.Modern));

        var exception = await Assert.ThrowsAsync<PdfSourceFormatException>(
            async () => await reader.ReadAsync(
                "damaged.pdf",
                content,
                password: null,
                TestContext.Current.CancellationToken));

        Assert.Contains("no document catalog", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A file that was cut behind its last object keeps working.</summary>
    [Fact]
    public async Task Rebuilds_a_file_that_lost_its_trailer()
    {
        var bytes = File.ReadAllBytes(TestAssets.Outlined);
        var truncated = bytes[..Encoding.Latin1.GetString(bytes).LastIndexOf("startxref", StringComparison.Ordinal)];

        var document = await ReadAsync(truncated);

        Assert.Equal(3, document.PageCount);
        Assert.NotEmpty(document.Warnings);
    }

    /// <summary>Rebuilding may be switched off, and then the damage is reported instead of repaired.</summary>
    [Fact]
    public async Task Refuses_a_damaged_table_when_rebuilding_is_off()
    {
        var reader = new PdfDocumentReader(PdfEngineOptions.Default with { ReconstructCrossReferenceOnFailure = false });

        using var content = new MemoryStream(DamageTheTableKeyword(TestAssets.Outlined));

        await Assert.ThrowsAsync<PdfSourceFormatException>(
            async () => await reader.ReadAsync(
                "damaged.pdf",
                content,
                password: null,
                TestContext.Current.CancellationToken));
    }

    /// <summary>Bytes that hold no document at all are refused with the name of the source.</summary>
    /// <param name="text">The bytes that are not a PDF document.</param>
    [Theory]
    [InlineData("this is not a PDF file at all")]
    [InlineData("%PDF-1.7\ntrailer\n<< >>\n%%EOF\n")]
    [InlineData("%PDF-1.7\n1 0 obj\n<< /Type /Pages >>\nendobj\n%%EOF\n")]
    public async Task Refuses_bytes_that_are_not_a_document(string text)
    {
        var reader = new PdfDocumentReader(PdfEngineOptions.Default);

        using var content = new MemoryStream(Encoding.Latin1.GetBytes(text));

        var exception = await Assert.ThrowsAsync<PdfSourceFormatException>(
            async () => await reader.ReadAsync(
                "damaged.pdf",
                content,
                password: null,
                TestContext.Current.CancellationToken));

        Assert.Equal("damaged.pdf", exception.SourceName);
    }

    /// <summary>A rebuilt table points at the byte where the object header begins.</summary>
    /// <remarks>
    /// Every offset of a rebuilt table is used to parse the object behind it, so an offset that is one byte
    /// off makes a recovered document read as an empty one.
    /// </remarks>
    [Fact]
    public void Points_a_rebuilt_table_at_the_object_header()
    {
        const string Text = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";
        var warnings = new List<string>();

        var table = new PdfCrossReferenceReader(
            Encoding.Latin1.GetBytes(Text),
            "damaged.pdf",
            PdfEngineOptions.Default,
            warnings).Read();

        var entry = table.Get(1);

        Assert.True(entry.HasValue);
        Assert.Equal(Text.IndexOf("1 0 obj", StringComparison.Ordinal), entry.Value.Offset);
        Assert.Contains(warnings, warning => warning.Contains("rebuilt by scanning", StringComparison.Ordinal));
    }

    /// <summary>A file that lost everything behind its catalog is read as a document without pages.</summary>
    /// <remarks>
    /// This is the shape of a download that was cut short: the catalog survived, the objects it points at did
    /// not. Reading it is the right answer, because the caller sees a document with no pages together with the
    /// warning that says the table had to be rebuilt.
    /// </remarks>
    [Fact]
    public async Task Reads_a_file_that_lost_everything_behind_its_catalog()
    {
        var document = await ReadAsync(Encoding.Latin1.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF\n"));

        Assert.Equal(0, document.PageCount);
        Assert.NotEmpty(document.Warnings);
    }

    /// <summary>Reads a document from bytes and reports what the reader made of it.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The description of the document.</returns>
    private static async Task<PdfDocumentInfo> ReadAsync(byte[] bytes)
    {
        var reader = new PdfDocumentReader(PdfEngineOptions.Default);

        using var content = new MemoryStream(bytes);

        return await reader.ReadAsync(
            "damaged.pdf",
            content,
            password: null,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Rewrites the <c>xref</c> keyword of a document that carries a classic table.</summary>
    /// <param name="path">The fixture to damage.</param>
    /// <returns>The damaged bytes.</returns>
    private static byte[] DamageTheTableKeyword(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var offset = ReadStartxrefOffset(bytes);

        Assert.Equal("xref", Encoding.Latin1.GetString(bytes, offset, 4));

        "Xref"u8.CopyTo(bytes.AsSpan(offset));
        return bytes;
    }

    /// <summary>Points <c>startxref</c> at the file header instead of at the cross-reference section.</summary>
    /// <param name="path">The fixture to damage.</param>
    /// <returns>The damaged bytes.</returns>
    private static byte[] PointStartxrefAtTheHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var offset = ReadStartxrefOffset(bytes);
        var text = Encoding.Latin1.GetString(bytes);

        Assert.InRange(offset, PdfHeaderLength, bytes.Length);

        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;

        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        // Every digit becomes a zero, which keeps the width of the file and makes the offset point at the
        // header, where neither a table nor a stream is to be found.
        for (var index = start; index < text.Length && char.IsAsciiDigit(text[index]); index++)
        {
            bytes[index] = (byte)'0';
        }

        return bytes;
    }

    /// <summary>The length of a PDF file header, which nothing can point at.</summary>
    private const int PdfHeaderLength = 8;

    /// <summary>Reads the offset a document announces behind its <c>startxref</c> keyword.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The offset.</returns>
    private static int ReadStartxrefOffset(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;

        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var end = start;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return int.Parse(text[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}
