using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
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
    /// <summary>
    /// The bound on a chain of lengths leaves an ordinary indirect length alone, for every stream of a
    /// document and not only the first ones: each stream's data holds an <c>endstream</c> line of its own,
    /// so a length that was not resolved would cut the stream there instead of at its real end.
    /// </summary>
    [Fact]
    public void Resolves_the_indirect_length_of_every_stream_of_a_document()
    {
        const int Streams = 100;
        var data = "q\nendstream\nQ"u8.ToArray();
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>");

        // Objects 4, 6, 8 ... are the streams; 5, 7, 9 ... are their lengths.
        for (var stream = 0; stream < Streams; stream++)
        {
            var body = Encoding.Latin1.GetBytes($"<< /Length {5 + (2 * stream)} 0 R >>\nstream\n")
                .Concat(data)
                .Concat("\nendstream"u8.ToArray())
                .ToArray();

            builder.Add(body);
            builder.Add(data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        var reader = new PdfFileReader(builder.Build(catalog), "lengths.pdf", PdfEngineOptions.Default);

        for (var stream = 0; stream < Streams; stream++)
        {
            var resolved = Assert.IsType<PdfStream>(reader.Resolve(4 + (2 * stream)));

            Assert.Equal(data, resolved.Raw.ToArray());
        }
    }

    /// <summary>
    /// An object stream that declares 2^31 - 1 objects in a header of one pair is read for the one pair it
    /// holds. /N sized two arrays before anything compared it with the stream, so a few hundred bytes asked
    /// for gigabytes; the stream now bounds the count, and the page inside it is found.
    /// </summary>
    [Fact]
    public void Reads_an_object_stream_whose_count_its_header_cannot_hold()
    {
        var page = "3 0 << /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>"u8.ToArray();
        var packed = PdfBuilder.Compress(page);
        var output = new MemoryStream();
        var offsets = new Dictionary<int, long>();

        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));

        void Object(int number, string dictionary, byte[]? stream = null)
        {
            offsets[number] = output.Position;
            Write($"{number} 0 obj\n{dictionary}\n");

            if (stream is not null)
            {
                Write("stream\n");
                output.Write(stream);
                Write("\nendstream\n");
            }

            Write("endobj\n");
        }

        Write("%PDF-1.7\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(4, "<< /Length 3 >>", "q Q"u8.ToArray());
        Object(5, $"<< /Type /ObjStm /N 2147483647 /First 4 /Filter /FlateDecode /Length {packed.Length} >>", packed);

        offsets[6] = output.Position;
        (int Kind, long Field, int Index)[] entries =
        [
            (0, 0, 65535), (1, offsets[1], 0), (1, offsets[2], 0), (2, 5, 0),
            (1, offsets[4], 0), (1, offsets[5], 0), (1, offsets[6], 0),
        ];
        var table = entries
            .SelectMany(entry => new[]
            {
                (byte)entry.Kind,
                (byte)(entry.Field >> 24), (byte)(entry.Field >> 16), (byte)(entry.Field >> 8), (byte)entry.Field,
                (byte)(entry.Index >> 8), (byte)entry.Index,
            })
            .ToArray();

        Write($"6 0 obj\n<< /Type /XRef /Size 7 /W [1 4 2] /Root 1 0 R /Length {table.Length} >>\nstream\n");
        output.Write(table);
        Write($"\nendstream\nendobj\nstartxref\n{offsets[6]}\n%%EOF\n");

        var reader = new PdfFileReader(output.ToArray(), "objstm.pdf", PdfEngineOptions.Default);

        Assert.Single(reader.Pages);
        Assert.Equal("Page", Assert.IsType<PdfDictionary>(reader.Resolve(3)).GetName("Type"));
    }

    /// <summary>
    /// A stream whose <c>/Length</c> is another stream, whose length is another, sixty thousand deep, is
    /// read by finding its <c>endstream</c>. Resolved link by link, the chain would overflow the stack, and a
    /// stack overflow ends the process rather than throwing anything a caller could catch.
    /// </summary>
    [Fact]
    public void Reads_a_stream_whose_length_is_a_chain_of_sixty_thousand_streams()
    {
        const int Chain = 60_000;
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>");

        for (var link = 0; link < Chain; link++)
        {
            builder.Add($"<< /Length {5 + link} 0 R >>\nstream\nq Q\nendstream");
        }

        builder.Add("3");
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        var reader = new PdfFileReader(builder.Build(catalog), "chain.pdf", PdfEngineOptions.Default);
        var content = Assert.IsType<PdfStream>(reader.Resolve(4));

        Assert.Equal("q Q", Encoding.Latin1.GetString(content.Raw.Span));
        Assert.Single(reader.Pages);
    }

    /// <summary>A table that no longer parses is rebuilt, and the document is read completely.</summary>
    [Fact]
    public async Task Rebuilds_a_cross_reference_table_that_does_not_parse()
    {
        var document = await ReadAsync(DamageTheTableKeyword(TestAssets.Outlined));

        Assert.Equal(3, document.PageCount);
        Assert.Contains(
            document.Warnings,
            warning => warning.Kind == PdfWarningKind.CrossReferenceRebuilt);
    }

    /// <summary>A table that is not where <c>startxref</c> announces it is recovered from as well.</summary>
    [Fact]
    public async Task Rebuilds_a_table_that_is_not_where_startxref_announces_it()
    {
        var document = await ReadAsync(PointStartxrefAtTheHeader(TestAssets.SafeWebCore));

        Assert.Equal(6, document.PageCount);
        Assert.Contains(
            document.Warnings,
            warning => warning.Kind == PdfWarningKind.CrossReferenceRebuilt);
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
        var warnings = new List<PdfWarning>();

        var table = new PdfCrossReferenceReader(
            Encoding.Latin1.GetBytes(Text),
            "damaged.pdf",
            PdfEngineOptions.Default,
            warnings).Read();

        var entry = table.Get(1);

        Assert.True(entry.HasValue);
        Assert.Equal(Text.IndexOf("1 0 obj", StringComparison.Ordinal), entry.Value.Offset);
        Assert.Contains(warnings, warning => warning.Kind == PdfWarningKind.CrossReferenceRebuilt);
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
