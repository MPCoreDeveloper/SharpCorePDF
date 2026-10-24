using System.Globalization;
using System.Text;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the incremental update: an edit that appends a revision to the bytes of its source instead
/// of rewriting them.
/// </summary>
/// <remarks>
/// The promise under test is a byte-level one — every byte of every earlier revision is still there, in
/// the same place — so the assertions compare the result with the source byte for byte before they ask
/// the read side what the document now says.
/// </remarks>
public sealed class PdfIncrementalWriterTests
{
    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default with { VerifyOutput = true };
    private static readonly PdfDocumentEditorService Editor = new(new PdfDocumentReader(Options), new PdfEditEngine(Options));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PdfTypeface Helvetica => PdfTypeface.Standard(StandardFont.Helvetica);

    private static byte[] ThreePages() => PdfDocumentBuilder.Create()
        .AddPage(PdfPageSize.A5, page => page.DrawText("one", 72, 100, Helvetica, 12))
        .AddPage(PdfPageSize.A4, page => page.DrawText("two", 72, 100, Helvetica, 12))
        .AddPage(PdfPageSize.Letter, page => page.DrawText("three", 72, 100, Helvetica, 12))
        .ToArray();

    /// <summary>A one page document whose header declares PDF 1.4.</summary>
    private static byte[] LegacyDocument()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();

        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Resources << >> /Contents 4 0 R >>");
        builder.AddStream(string.Empty, []);
        builder.Set(catalog, "<< /Type /Catalog /Pages 2 0 R >>");
        builder.Set(pages, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        var document = builder.Build(catalog);

        // The header and the cross-reference table agree on every offset, so rewriting the version in
        // place — same length, no byte moved — leaves a document a reader accepts as PDF 1.4.
        Encoding.Latin1.GetBytes("%PDF-1.4").CopyTo(document, 0);
        return document;
    }

    /// <summary>Makes the options of a plan ask for an appended revision, with a fixed moment.</summary>
    private static PdfEditPlan Incremental(PdfEditPlan plan) => plan.WithOutput(options => options with
    {
        UseIncrementalUpdate = true,
        ModificationDate = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
        DocumentId = "0123456789abcdef0123456789abcdef",
    });

    private static async Task<(PdfEditResult Result, byte[] Bytes)> ApplyAsync(byte[] source, PdfEditPlan plan)
    {
        using var destination = new MemoryStream();

        var result = await Editor
            .ApplyAsync(new PdfByteArraySource(source, "source.pdf"), plan, destination, cancellationToken: Token)
            .ConfigureAwait(false);

        return (result, destination.ToArray());
    }

    private static Task<PdfEditResult> ApplyWithEngineAsync(
        Stream content,
        PdfEditPlan plan,
        string? password = null) =>
        new PdfEditEngine(Options).ApplyAsync("source.pdf", content, plan, password, new MemoryStream(), Token);

    /// <summary>Reads the offset the last <c>startxref</c> of a document points at.</summary>
    private static long StartXref(byte[] document)
    {
        var text = Encoding.Latin1.GetString(document);
        var index = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;
        var digits = text[index..].TrimStart();
        var length = 0;

        while (length < digits.Length && char.IsAsciiDigit(digits[length]))
        {
            length++;
        }

        return long.Parse(digits[..length], CultureInfo.InvariantCulture);
    }

    /// <summary>Every byte of the source is the beginning of the result, unchanged and in place.</summary>
    [Fact]
    public async Task Appends_a_revision_after_the_bytes_of_the_source()
    {
        var source = ThreePages();
        var (_, edited) = await ApplyAsync(
            source,
            Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)));

        Assert.True(edited.Length > source.Length);
        Assert.Equal(source, edited[..source.Length]);

        var reader = new PdfFileReader(edited, "edited.pdf", Options);

        Assert.Equal(3, reader.Pages.Count);
        Assert.Equal(90, (int)(reader.Pages[0].Dictionary.GetInteger("Rotate") ?? 0L));
    }

    /// <summary>The new cross-reference section chains back to the one of the source.</summary>
    [Fact]
    public async Task Chains_the_revision_to_the_cross_reference_section_of_the_source()
    {
        var source = ThreePages();
        var (_, edited) = await ApplyAsync(source, Incremental(PdfEditPlan.Create().DeletePage(2)));

        var offset = StartXref(edited);

        Assert.True(offset > source.Length, "the newest cross-reference section has to be inside the appended revision");

        var revision = Encoding.Latin1.GetString(edited, (int)offset, edited.Length - (int)offset);

        Assert.Contains("trailer", revision, StringComparison.Ordinal);
        Assert.Contains($"/Prev {StartXref(source)}", revision, StringComparison.Ordinal);
        Assert.Contains("%%EOF", revision, StringComparison.Ordinal);

        // The earlier revision is still a whole document inside the result, ending where it ended.
        var ending = Encoding.Latin1.GetString(source, source.Length - 6, 6);
        Assert.Contains(ending, Encoding.Latin1.GetString(edited), StringComparison.Ordinal);
    }

    /// <summary>The offsets of the revision are absolute in a destination that can seek.</summary>
    [Fact]
    public async Task Counts_offsets_from_the_start_of_a_seekable_destination()
    {
        var source = ThreePages();

        using var content = new MemoryStream(source);
        using var destination = new MemoryStream();

        var result = await new PdfEditEngine(PdfEngineOptions.Default).ApplyAsync(
            "source.pdf",
            content,
            Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)),
            password: null,
            destination,
            Token);

        var edited = destination.ToArray();

        Assert.Equal(source, edited[..source.Length]);
        Assert.Equal(edited.Length, result.BytesWritten);
        Assert.Equal(3, new PdfFileReader(edited, "edited.pdf", Options).Pages.Count);
    }

    /// <summary>A source that does not end on a newline still gets its revision on a line of its own.</summary>
    [Fact]
    public async Task Starts_the_revision_on_a_line_of_its_own()
    {
        // The authoring surface ends a document with a newline; the one dropped here is what a producer
        // that does not is like, and an object header that does not start a line is not an object header.
        var source = ThreePages()[..^1];

        Assert.NotEqual((byte)'\n', source[^1]);

        var (_, edited) = await ApplyAsync(
            source,
            Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)));

        Assert.Equal(source, edited[..source.Length]);
        Assert.Equal((byte)'\n', edited[source.Length]);
        Assert.Equal(3, new PdfFileReader(edited, "edited.pdf", Options).Pages.Count);
    }

    /// <summary>A second edit extends the first revision rather than starting over.</summary>
    [Fact]
    public async Task Chains_a_second_revision_to_the_first()
    {
        var (_, once) = await ApplyAsync(
            ThreePages(),
            Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)));

        var (_, twice) = await ApplyAsync(
            once,
            Incremental(PdfEditPlan.Create().RotatePage(2, PageRotation.Clockwise180)));

        Assert.True(
            ((ReadOnlySpan<byte>)twice).StartsWith(once),
            "the second revision has to keep every byte of the first");

        Assert.Equal(2, Encoding.Latin1.GetString(twice).Split("/Prev").Length - 1);

        var reader = new PdfFileReader(twice, "edited.pdf", Options);

        Assert.Equal(3, reader.Pages.Count);
        Assert.Equal(90, (int)(reader.Pages[0].Dictionary.GetInteger("Rotate") ?? 0L));
        Assert.Equal(180, (int)(reader.Pages[1].Dictionary.GetInteger("Rotate") ?? 0L));
    }

    /// <summary>A signature over the source still covers exactly the bytes it was made over.</summary>
    [Fact]
    public async Task Keeps_a_signature_that_covers_the_earlier_revision()
    {
        var signed = await SigningFixtures.SignAsync(ThreePages(), SigningFixtures.Options(SigningFixtures.Certificate));

        var (result, edited) = await ApplyAsync(
            signed,
            Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)));

        Assert.True(
            ((ReadOnlySpan<byte>)edited).StartsWith(signed),
            "the result has to begin with the bytes of the source");

        var validation = Assert.Single(await SigningFixtures.ValidateAsync(edited));

        Assert.True(validation.IsValid);

        // Signing appends a revision of its own, so the signed state of the source is revision 2 and the
        // edit is revision 3: the signature still covers exactly what it was made over, and no more.
        Assert.Equal(2, validation.Signer.CoveredRevision);
        Assert.False(validation.Signer.CoversWholeDocument);

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PriorSignaturePreserved);
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.IncrementalUpdateAppended);

        // The edit is in the newest revision, which is the one a reader resolves the pages through.
        var reader = new PdfFileReader(edited, "edited.pdf", Options);

        Assert.Equal(3, reader.Pages.Count);
        Assert.Equal(90, (int)(reader.Pages[0].Dictionary.GetInteger("Rotate") ?? 0L));
    }

    /// <summary>A rewrite cannot keep a signature, and the outcome says so.</summary>
    [Fact]
    public async Task Warns_that_a_complete_rewrite_invalidates_a_signature()
    {
        var signed = await SigningFixtures.SignAsync(ThreePages(), SigningFixtures.Options(SigningFixtures.Certificate));

        var (result, edited) = await ApplyAsync(
            signed,
            PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90));

        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.PriorSignatureInvalidated);
        Assert.DoesNotContain(result.Warnings, warning => warning.Kind == PdfWarningKind.IncrementalUpdateAppended);
        Assert.False(((ReadOnlySpan<byte>)edited).StartsWith(signed));
    }

    /// <summary>An appended revision cannot change the header, so the version of the source is kept.</summary>
    [Fact]
    public async Task Keeps_the_version_of_the_source_and_says_so()
    {
        var (result, edited) = await ApplyAsync(
            LegacyDocument(),
            Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)));

        Assert.Equal(PdfVersion.V1_4, result.OutputVersion);
        Assert.Contains(result.Warnings, warning => warning.Kind == PdfWarningKind.IncrementalUpdateVersionKept);
        Assert.Equal(PdfVersion.V1_4, new PdfFileReader(edited, "edited.pdf", Options).Version);
    }

    /// <summary>A source that cannot be replayed byte for byte cannot be extended.</summary>
    [Fact]
    public async Task Refuses_a_source_that_cannot_seek()
    {
        using var content = new ForwardOnlyStream(ThreePages());

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(
            () => ApplyWithEngineAsync(
                content,
                Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90))));
    }

    /// <summary>The result begins with the source, so a destination that already holds bytes is refused.</summary>
    [Fact]
    public async Task Refuses_a_destination_that_already_holds_bytes()
    {
        using var content = new MemoryStream(ThreePages());
        using var destination = new MemoryStream();

        destination.Write([1, 2, 3]);

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(
            () => new PdfEditEngine(Options).ApplyAsync(
                "source.pdf",
                content,
                Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)),
                password: null,
                destination,
                Token));
    }

    /// <summary>An encrypted source has a file key an appended revision cannot be written with.</summary>
    [Fact]
    public async Task Refuses_an_incremental_update_of_an_encrypted_document()
    {
        var encrypted = await Editor.ApplyToArrayAsync(
            new PdfByteArraySource(ThreePages(), "three-pages.pdf"),
            PdfEditPlan.Create(PdfEditOutputOptions.Default with
            {
                Encryption = PdfEncryptionOptions.Default with { UserPassword = "open", OwnerPassword = "own" },
            }),
            cancellationToken: Token);

        using var content = new MemoryStream(encrypted);

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(
            () => ApplyWithEngineAsync(
                content,
                Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)),
                "open"));
    }

    /// <summary>The combination is refused when the plan is validated, before the source is read.</summary>
    [Fact]
    public async Task Refuses_a_plan_that_combines_an_incremental_update_with_encryption()
    {
        var plan = Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90)).WithOutput(
            options => options with { Encryption = PdfEncryptionOptions.Default });

        Assert.NotEmpty(plan.Validate());

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(() => ApplyAsync(ThreePages(), plan));
    }

    /// <summary>A table that was rebuilt by scanning has no offset a revision can chain to.</summary>
    [Fact]
    public async Task Refuses_a_document_whose_cross_reference_table_was_rebuilt()
    {
        var text = Encoding.Latin1.GetString(ThreePages());
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;
        var begin = start;

        while (char.IsWhiteSpace(text[begin]))
        {
            begin++;
        }

        var end = begin;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        // The pointer at the end of the file is overwritten in place — the digits keep their length, so
        // no byte of the document moves — and the reader has to recover the table by scanning.
        var damaged = Encoding.Latin1.GetBytes(text[..begin] + new string('9', end - begin) + text[end..]);

        using var content = new MemoryStream(damaged);

        await Assert.ThrowsAsync<PdfEditPlanValidationException>(
            () => ApplyWithEngineAsync(
                content,
                Incremental(PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90))));
    }

    /// <summary>A stream that can only be read forwards, like the body of a download.</summary>
    private sealed class ForwardOnlyStream(byte[] content) : Stream
    {
        private readonly MemoryStream inner = new(content, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

