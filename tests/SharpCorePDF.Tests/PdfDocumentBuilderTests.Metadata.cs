using System.Text;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the document a builder describes beyond its pages: the XMP packet, the outline, and the
/// pages it copies out of another document.
/// </summary>
/// <remarks>
/// Everything is read back through the content reader, so what is checked is what a consumer of the
/// result sees: the packet as the file stores it, the tree as a viewer shows it, and the text,
/// annotations and geometry of the pages that were copied.
/// </remarks>
public sealed partial class PdfDocumentBuilderTests
{
    private const string Packet = """
        <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
         <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
          <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
           <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Décompte annuel</rdf:li></rdf:Alt></dc:title>
          </rdf:Description>
         </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    private static PdfByteArraySource Authored(byte[] bytes) => new(bytes, "authored.pdf");

    private static Task<PdfXmpResult> MetadataAsync(byte[] bytes) =>
        Reader().ReadMetadataAsync(Authored(bytes), cancellationToken: Token);

    private static Task<PdfReadResult<PdfOutlineItem>> OutlineAsync(byte[] bytes) =>
        Reader().ReadOutlineAsync(Authored(bytes), cancellationToken: Token);

    private static Task<PdfReadResult<PdfAnnotationInfo>> AnnotationsAsync(byte[] bytes, PageRange range) =>
        Reader().ReadAnnotationsAsync(Authored(bytes), range, cancellationToken: Token);

    /// <summary>A three page document whose pages each carry one line of text.</summary>
    /// <returns>The bytes of the document.</returns>
    private static byte[] Source() =>
        PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("première page", 40, 60, Helvetica, 12))
            .AddPage(PdfPageSize.A4, page => page.DrawText("deuxième page", 40, 60, Helvetica, 12))
            .AddPage(PdfPageSize.A4, page => page.DrawText("troisième page", 40, 60, Helvetica, 12))
            .ToArray();

    /// <summary>A one page document that states its size and its rotation on the page tree.</summary>
    /// <returns>The bytes of the document.</returns>
    private static byte[] RotatedSource()
    {
        var builder = new PdfBuilder();
        var catalog = builder.Reserve();
        var pages = builder.Reserve();
        var page = builder.Reserve();

        builder.Set(page, $"<< /Type /Page /Parent {pages} 0 R >>");
        builder.Set(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 /MediaBox [0 0 200 300] /Rotate 90 >>");
        builder.Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");

        return builder.Build(catalog);
    }

    /// <summary>An XMP packet the caller states is carried byte for byte in the catalog.</summary>
    [Fact]
    public async Task Carries_the_xmp_packet_it_was_given()
    {
        var packet = Encoding.UTF8.GetBytes(Packet);

        var bytes = PdfDocumentBuilder.Create()
            .WithXmp(packet)
            .AddPage(PdfPageSize.A4, page => page.DrawText("Décompte", 40, 60, Helvetica, 12))
            .ToArray();

        var result = await MetadataAsync(bytes);
        var metadata = Assert.IsType<PdfXmpMetadata>(result.Metadata);

        Assert.Equal(packet, metadata.RawPacket.ToArray());
        Assert.Equal("Décompte annuel", metadata.Title);
        Assert.Contains("/Metadata", Encoding.Latin1.GetString(bytes));
    }

    /// <summary>A packet read out of one document can be carried by another, unchanged.</summary>
    [Fact]
    public async Task Carries_a_packet_read_from_another_document()
    {
        var first = PdfDocumentBuilder.Create()
            .WithXmp(Encoding.UTF8.GetBytes(Packet))
            .AddPage(PdfPageSize.A4)
            .ToArray();

        var metadata = Assert.IsType<PdfXmpMetadata>((await MetadataAsync(first)).Metadata);

        var second = PdfDocumentBuilder.Create()
            .WithXmp(metadata)
            .AddPage(PdfPageSize.A4)
            .ToArray();

        var carried = Assert.IsType<PdfXmpMetadata>((await MetadataAsync(second)).Metadata);

        Assert.Equal(metadata.RawPacket.ToArray(), carried.RawPacket.ToArray());
    }

    /// <summary>Bytes that are not XMP are refused where they are set, not written as metadata.</summary>
    [Fact]
    public void Refuses_bytes_that_are_not_an_xmp_packet()
    {
        Assert.Throws<ArgumentException>(() => PdfDocumentBuilder
            .Create()
            .WithXmp(Encoding.UTF8.GetBytes("<html><body>not metadata</body></html>")));
    }

    /// <summary>The bookmarks are written with their nesting, their state and their destinations.</summary>
    [Fact]
    public async Task Writes_the_outline_it_was_given()
    {
        PdfOutlineItem[] outline =
        [
            new()
            {
                Title = "Chapitre 1",
                Destination = new PdfDestination { PageNumber = 1 },
                Children =
                [
                    new() { Title = "Section 1.1", Destination = new PdfDestination { PageNumber = 2 } },
                ],
            },
            new()
            {
                Title = "Chapitre 2",
                Destination = new PdfDestination { PageNumber = 3 },
                IsOpen = false,
                Children =
                [
                    new() { Title = "Section 2.1", Destination = new PdfDestination { PageNumber = 3 } },
                ],
            },
            new() { Title = "Site", Destination = new PdfDestination { Uri = "https://example.com/decompte" } },
        ];

        var bytes = PdfDocumentBuilder.Create()
            .WithOutline(outline)
            .AddPage(PdfPageSize.A4)
            .AddPage(PdfPageSize.A4)
            .AddPage(PdfPageSize.A4)
            .ToArray();

        var result = await OutlineAsync(bytes);

        Assert.Equal(3, result.Count);
        Assert.Equal(["Chapitre 1", "Chapitre 2", "Site"], result.Items.Select(item => item.Title));
        Assert.Equal(1, result.Items[0].Destination!.PageNumber);
        Assert.Equal("Section 1.1", Assert.Single(result.Items[0].Children).Title);
        Assert.False(result.Items[1].IsOpen);
        Assert.Equal(3, result.Items[1].Destination!.PageNumber);
        Assert.Equal("https://example.com/decompte", result.Items[2].Destination!.Uri);
    }

    /// <summary>A bookmark that points past the last page is refused when the document is written.</summary>
    /// <remarks>
    /// The check cannot happen where the tree is set, because pages are added afterwards; a bookmark
    /// that dangles is refused rather than written pointing at a page that is not there.
    /// </remarks>
    [Fact]
    public void Refuses_a_bookmark_that_points_past_the_last_page()
    {
        var builder = PdfDocumentBuilder.Create()
            .WithOutline([new() { Title = "Trop loin", Destination = new PdfDestination { PageNumber = 2 } }])
            .AddPage(PdfPageSize.A4);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.ToArray());

        Assert.Contains("Trop loin", exception.Message);
    }

    /// <summary>A bookmark without a title is refused where it is set.</summary>
    [Fact]
    public void Refuses_a_bookmark_without_a_title()
    {
        Assert.Throws<ArgumentException>(
            () => PdfDocumentBuilder.Create().WithOutline([new PdfOutlineItem { Title = " " }]));
    }

    /// <summary>A nested bookmark is checked like a top level one.</summary>
    [Fact]
    public void Refuses_a_nested_bookmark_without_a_title()
    {
        var outline = new PdfOutlineItem[]
        {
            new()
            {
                Title = "Chapitre",
                Children = [new PdfOutlineItem { Title = "" }],
            },
        };

        Assert.Throws<ArgumentException>(() => PdfDocumentBuilder.Create().WithOutline(outline));
    }

    /// <summary>A page of another document keeps its text when it is imported between drawn pages.</summary>
    [Fact]
    public async Task Imports_a_page_between_the_pages_it_draws()
    {
        var source = Source();

        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, page => page.DrawText("couverture", 40, 60, Helvetica, 12))
            .ImportPage(new PdfByteArraySource(source, "source.pdf"), 2)
            .AddPage(PdfPageSize.A4, page => page.DrawText("annexe", 40, 60, Helvetica, 12))
            .ToArray();

        Assert.Equal(3, (await InspectAsync(bytes)).PageCount);
        Assert.Equal("couverture", PlainText(await TextAsync(bytes, PageRange.Single(1))));
        Assert.Equal("deuxième page", PlainText(await TextAsync(bytes, PageRange.Single(2))));
        Assert.Equal("annexe", PlainText(await TextAsync(bytes, PageRange.Single(3))));
    }

    /// <summary>A range is imported one page after another, in the order the source states them.</summary>
    [Fact]
    public async Task Imports_a_range_in_source_order()
    {
        var source = Source();

        var bytes = PdfDocumentBuilder.Create()
            .ImportPages(new PdfByteArraySource(source, "source.pdf"), PageRange.FromTo(2, 3))
            .ToArray();

        Assert.Equal(2, (await InspectAsync(bytes)).PageCount);
        Assert.Equal("deuxième page", PlainText(await TextAsync(bytes, PageRange.Single(1))));
        Assert.Equal("troisième page", PlainText(await TextAsync(bytes, PageRange.Single(2))));
    }

    /// <summary>An imported page keeps the annotations its own document states.</summary>
    [Fact]
    public async Task Keeps_the_annotations_of_an_imported_page()
    {
        var bytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4)
            .ImportPage(new PdfByteArraySource(PdfContentFixtures.Annotations(), "annotated.pdf"), 1)
            .ToArray();

        var annotations = await AnnotationsAsync(bytes, PageRange.Single(2));

        Assert.Equal(8, annotations.Count);
    }

    /// <summary>Geometry and rotation a page inherited in its own tree travel with it onto a flat one.</summary>
    [Fact]
    public async Task Materialises_the_attributes_an_imported_page_inherited()
    {
        var bytes = PdfDocumentBuilder.Create()
            .ImportPage(new PdfByteArraySource(RotatedSource(), "rotated.pdf"), 1)
            .ToArray();

        var page = Assert.Single((await InspectAsync(bytes)).Pages);

        Assert.Equal(200, page.Size.WidthPoints, 0.001d);
        Assert.Equal(300, page.Size.HeightPoints, 0.001d);
        Assert.Equal(90, page.Rotation.Degrees);
    }

    /// <summary>A page the source does not have is refused when it is asked for.</summary>
    [Fact]
    public void Refuses_a_page_the_source_does_not_have()
    {
        var source = new PdfByteArraySource(Source(), "source.pdf");

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfDocumentBuilder.Create().ImportPage(source, 4));

        Assert.Contains("source.pdf", exception.Message);
    }

    /// <summary>A range that runs past the last page is refused rather than trimmed.</summary>
    [Fact]
    public void Refuses_a_range_that_runs_past_the_last_page()
    {
        var source = new PdfByteArraySource(Source(), "source.pdf");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PdfDocumentBuilder.Create().ImportPages(source, PageRange.FromTo(2, 9)));
    }
}
