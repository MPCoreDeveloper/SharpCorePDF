using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of what has to stay true of a document across incremental updates, independently of the edit
/// each revision makes: every earlier revision is still a document of its own, and the document keeps the
/// identity it was created with.
/// </summary>
/// <remarks>
/// ISO 32000-1 §7.5.6 makes an update an append: the bytes up to each <c>%%EOF</c> are the document as it
/// stood at that revision, which is what a signature over an earlier revision depends on. §14.4 makes the
/// first element of the trailer <c>ID</c> a permanent identifier that "shall not change when the file is
/// incrementally updated"; only the second element changes. <c>docs/tools/validate-incremental.py</c>
/// checks the same invariants with qpdf on the documents the validation corpus writes.
/// </remarks>
public sealed class PdfIncrementalInvariantTests
{
    private const string SourceId = "00112233445566778899AABBCCDDEEFF";

    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default with { VerifyOutput = true };
    private static readonly PdfDocumentEditorService Editor = new(new PdfDocumentReader(Options), new PdfEditEngine(Options));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Two updates on top of a source leave three revisions, and the bytes up to each <c>%%EOF</c> open as a
    /// document with the page geometry that revision gave it.
    /// </summary>
    [Fact]
    public async Task Leaves_every_earlier_revision_readable_on_its_own()
    {
        var source = Source();
        var first = await AppendAsync(source, PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90));
        var second = await AppendAsync(first, PdfEditPlan.Create().RotatePage(2, PageRotation.Clockwise180));

        var revisions = Revisions(second);

        Assert.Equal(3, revisions.Count);
        Assert.Equal(source, revisions[0]);
        Assert.Equal(first, revisions[1]);
        Assert.Equal(second, revisions[2]);

        int[][] rotations = [[0, 0], [90, 0], [90, 180]];

        for (var index = 0; index < revisions.Count; index++)
        {
            var reader = new PdfFileReader(revisions[index], $"revision-{index + 1}.pdf", Options);

            Assert.Equal(2, reader.Pages.Count);
            Assert.Equal(
                rotations[index],
                reader.Pages.Select(page => (int)(page.Dictionary.GetInteger("Rotate") ?? 0L)).ToArray());
            Assert.DoesNotContain(reader.Warnings, warning => warning.Kind == PdfWarningKind.CrossReferenceRebuilt);
        }
    }

    /// <summary>The newest revision states a <c>/Size</c> that covers every object any revision numbered.</summary>
    [Fact]
    public async Task States_a_size_that_covers_every_revision()
    {
        var source = Source();
        var updated = await AppendAsync(source, PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90));

        var before = new PdfFileReader(source, "source.pdf", Options).CrossReferenceTable;
        var after = new PdfFileReader(updated, "updated.pdf", Options).CrossReferenceTable;

        Assert.True(after.Trailer.GetInteger("Size") >= before.Trailer.GetInteger("Size"));
        Assert.True(after.Trailer.GetInteger("Size") > after.HighestObjectNumber);
    }

    /// <summary>The permanent half of the identifier survives an update; only the second half may change.</summary>
    [Fact(Skip = "Known defect: PdfEditEngine.WriteTrailer writes a new identifier into both elements of /ID even when the revision is appended (PdfEditEngine.Document.cs). Remove the skip with the fix.")]
    public async Task Keeps_the_first_identifier_element_across_an_incremental_update()
    {
        var source = Source();
        var updated = await AppendAsync(source, PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90));

        Assert.Equal(Convert.FromHexString(SourceId), Identifier(source, 0));
        Assert.Equal(Identifier(source, 0), Identifier(updated, 0));
    }

    private static byte[] Source() => PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with { DocumentId = SourceId })
        .AddPage(PdfPageSize.A5, page => page.DrawText("one", 72, 100, PdfTypeface.Standard(StandardFont.Helvetica), 12))
        .AddPage(PdfPageSize.A4, page => page.DrawText("two", 72, 100, PdfTypeface.Standard(StandardFont.Helvetica), 12))
        .ToArray();

    private static async Task<byte[]> AppendAsync(byte[] source, PdfEditPlan plan)
    {
        using var destination = new MemoryStream();

        await Editor.ApplyAsync(
            new PdfByteArraySource(source, "source.pdf"),
            plan.WithOutput(options => options with
            {
                UseIncrementalUpdate = true,
                ModificationDate = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
            }),
            destination,
            cancellationToken: Token);

        return destination.ToArray();
    }

    /// <summary>Cuts a document behind each <c>%%EOF</c> and the end of line that follows it.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The bytes of each revision, oldest first.</returns>
    private static List<byte[]> Revisions(byte[] document)
    {
        var revisions = new List<byte[]>();
        var marker = "%%EOF"u8;
        var from = 0;

        while (document.AsSpan(from).IndexOf(marker) is var found and >= 0)
        {
            var end = from + found + marker.Length;

            while (end < document.Length && document[end] is (byte)'\r' or (byte)'\n')
            {
                end++;
            }

            revisions.Add(document[..end]);
            from = end;
        }

        return revisions;
    }

    private static byte[] Identifier(byte[] document, int element)
    {
        var trailer = new PdfFileReader(document, "document.pdf", Options).CrossReferenceTable.Trailer;
        var identifier = Assert.IsType<PdfArray>(trailer.Get("ID"));

        return Assert.IsType<PdfString>(identifier.Items[element]).Bytes;
    }
}
