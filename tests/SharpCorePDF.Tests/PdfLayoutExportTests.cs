using System.Text;
using System.Xml.Linq;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Reading;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the export surface: the four documents a layout read can be written as, the page space their
/// coordinates are stated in and the refusals that keep a caller from paying for an export it cannot use.
/// </summary>
/// <remarks>
/// Every document is parsed back with <see cref="XDocument"/> rather than compared with a string, so a test
/// fails when the bytes are not well-formed and passes only when the structure a consumer reads is there.
/// The fixtures are the same ones the layout tests draw, so an assertion can name the page, the word and the
/// position it expects instead of describing a binary asset.
/// </remarks>
public sealed class PdfLayoutExportTests
{
    /// <summary>The height of the page every text fixture declares.</summary>
    private const double PageHeight = 792d;

    /// <summary>Creates a reader with the default engine options.</summary>
    /// <returns>The use case, wired the way the container wires it.</returns>
    private static PdfLayoutReaderService Reader() => new(new PdfLayoutReader());

    /// <summary>Wraps document bytes in a content source.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <param name="name">The name used in diagnostics.</param>
    /// <returns>The source.</returns>
    private static PdfByteArraySource Source(byte[] bytes, string name = "fixture.pdf") => new(bytes, name);

    /// <summary>Gets the cancellation token of the running test.</summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Exports a document and returns the bytes the reader wrote.</summary>
    /// <param name="bytes">The bytes of the document to read.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="range">The pages to export; <see langword="null"/> takes the whole document.</param>
    /// <param name="options">The layout options; <see langword="null"/> takes the defaults.</param>
    /// <returns>The document the reader wrote.</returns>
    private static async Task<byte[]> ExportAsync(
        byte[] bytes,
        PdfLayoutFormat format,
        PageRange? range = null,
        PdfLayoutOptions? options = null)
    {
        using var destination = new MemoryStream();

        await Reader()
            .ExportAsync(Source(bytes), range ?? PageRange.All, format, destination, options, null, Token);

        return destination.ToArray();
    }

    /// <summary>Exports a document and parses what it wrote.</summary>
    /// <param name="bytes">The bytes of the document to read.</param>
    /// <param name="format">The format to write.</param>
    /// <param name="range">The pages to export; <see langword="null"/> takes the whole document.</param>
    /// <returns>The parsed document.</returns>
    private static async Task<XDocument> ParseAsync(
        byte[] bytes,
        PdfLayoutFormat format,
        PageRange? range = null) =>
        XDocument.Parse(Encoding.UTF8.GetString(await ExportAsync(bytes, format, range)));

    /// <summary>States the namespace of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The namespace every element of the document is in.</returns>
    private static string NamespaceOf(PdfLayoutFormat format) => format switch
    {
        PdfLayoutFormat.Alto => "http://www.loc.gov/standards/alto/ns-v4#",
        PdfLayoutFormat.PageXml => "http://schema.primaresearch.org/PAGE/gts/pagecontent/2019-07-15",
        PdfLayoutFormat.Hocr => "http://www.w3.org/1999/xhtml",
        PdfLayoutFormat.SvgText => "http://www.w3.org/2000/svg",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
    };

    /// <summary>States the local name of the root element of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The local name of the root element.</returns>
    private static string RootOf(PdfLayoutFormat format) => format switch
    {
        PdfLayoutFormat.Alto => "alto",
        PdfLayoutFormat.PageXml => "PcGts",
        PdfLayoutFormat.Hocr => "html",
        PdfLayoutFormat.SvgText => "svg",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
    };

    /// <summary>Builds a name in the namespace of a format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="localName">The local name of the element.</param>
    /// <returns>The name to search the document with.</returns>
    private static XName Name(PdfLayoutFormat format, string localName) =>
        XName.Get(localName, NamespaceOf(format));

    /// <summary>Collects the page elements an export wrote, in document order.</summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="format">The format it was written in.</param>
    /// <returns>One element per page.</returns>
    private static List<XElement> Pages(XDocument document, PdfLayoutFormat format) => format switch
    {
        PdfLayoutFormat.Hocr =>
        [
            .. document.Descendants(Name(format, "div"))
                .Where(element => (string?)element.Attribute("class") == "ocr_page"),
        ],
        PdfLayoutFormat.SvgText =>
        [
            .. document.Descendants(Name(format, "g"))
                .Where(element => ((string?)element.Attribute("id"))?.StartsWith("page-", StringComparison.Ordinal) == true),
        ],
        _ =>
        [
            .. document.Descendants(Name(format, "Page")),
        ],
    };

    /// <summary>Collects the words an export wrote, in document order.</summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="format">The format it was written in.</param>
    /// <returns>The words, in the order the document states them.</returns>
    private static List<string> ExportedWords(XDocument document, PdfLayoutFormat format) => format switch
    {
        PdfLayoutFormat.Alto =>
        [
            .. document.Descendants(Name(format, "String"))
                .Select(element => (string?)element.Attribute("CONTENT") ?? string.Empty),
        ],
        PdfLayoutFormat.PageXml =>
        [
            .. document.Descendants(Name(format, "TextEquiv"))
                .Select(element => (string?)element.Element(Name(format, "Unicode")) ?? string.Empty)
                .SelectMany(line => line.Split(' ')),
        ],
        PdfLayoutFormat.Hocr =>
        [
            .. document.Descendants(Name(format, "span"))
                .Where(element => (string?)element.Attribute("class") == "ocrx_word")
                .Select(element => element.Value),
        ],
        PdfLayoutFormat.SvgText =>
        [
            .. document.Descendants(Name(format, "text")).Select(element => element.Value),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
    };

    /// <summary>Rounds a coordinate the way every exporter rounds one.</summary>
    /// <param name="value">The coordinate, in points.</param>
    /// <returns>The whole unit the document states.</returns>
    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Each format writes a well-formed document that carries the words of the page.</summary>
    /// <param name="format">The format under test.</param>
    /// <returns>A task that completes when the document has been checked.</returns>
    [Theory]
    [InlineData(PdfLayoutFormat.Alto)]
    [InlineData(PdfLayoutFormat.PageXml)]
    [InlineData(PdfLayoutFormat.Hocr)]
    [InlineData(PdfLayoutFormat.SvgText)]
    public async Task Exports_alto_pagexml_hocr_and_svg(PdfLayoutFormat format)
    {
        var document = await ParseAsync(PdfContentFixtures.Text(), format);

        Assert.Equal(RootOf(format), document.Root!.Name.LocalName);
        Assert.Equal(NamespaceOf(format), document.Root.Name.NamespaceName);
        Assert.Equal(["Hello", "World", "Second", "line"], ExportedWords(document, format));
    }

    /// <summary>A page that draws no text still produces a page element, and no text with it.</summary>
    /// <param name="format">The format under test.</param>
    /// <returns>A task that completes when the document has been checked.</returns>
    [Theory]
    [InlineData(PdfLayoutFormat.Alto)]
    [InlineData(PdfLayoutFormat.PageXml)]
    [InlineData(PdfLayoutFormat.Hocr)]
    [InlineData(PdfLayoutFormat.SvgText)]
    public async Task Writes_a_page_element_for_a_page_with_no_text(PdfLayoutFormat format)
    {
        var document = await ParseAsync(PdfContentFixtures.Images(), format);

        Assert.Equal(2, Pages(document, format).Count);
        Assert.Empty(ExportedWords(document, format));
    }

    /// <summary>A range writes the pages it names and no others.</summary>
    /// <returns>A task that completes when the document has been checked.</returns>
    [Fact]
    public async Task Writes_only_the_pages_of_the_range()
    {
        var document = await ParseAsync(
            PdfContentFixtures.TextPages(),
            PdfLayoutFormat.Alto,
            PageRange.Single(2));

        Assert.Single(Pages(document, PdfLayoutFormat.Alto));
        Assert.Equal(["Page", "two"], ExportedWords(document, PdfLayoutFormat.Alto));
    }

    /// <summary>A document of more than one page is written in ascending page order.</summary>
    /// <returns>A task that completes when the document has been checked.</returns>
    [Fact]
    public async Task Writes_a_range_in_ascending_page_order()
    {
        var document = await ParseAsync(PdfContentFixtures.TextPages(), PdfLayoutFormat.PageXml);
        var lines = Pages(document, PdfLayoutFormat.PageXml)
            .Select(page => page.Descendants(Name(PdfLayoutFormat.PageXml, "Unicode")).Single().Value);

        Assert.Equal(["Page one", "Page two"], lines);
    }

    /// <summary>The document is UTF-8 without a byte order mark, whatever the platform writes with.</summary>
    /// <returns>A task that completes when the bytes have been checked.</returns>
    [Fact]
    public async Task Writes_utf8_without_a_byte_order_mark()
    {
        var bytes = await ExportAsync(PdfContentFixtures.Text(), PdfLayoutFormat.Alto);

        Assert.Equal((byte)'<', bytes[0]);
        Assert.Contains("encoding=\"utf-8\"", Encoding.UTF8.GetString(bytes[..64]), StringComparison.Ordinal);
    }

    /// <summary>The same read produces the same bytes, so an export can be compared and stored.</summary>
    /// <param name="format">The format under test.</param>
    /// <returns>A task that completes when both documents have been compared.</returns>
    [Theory]
    [InlineData(PdfLayoutFormat.Alto)]
    [InlineData(PdfLayoutFormat.PageXml)]
    [InlineData(PdfLayoutFormat.Hocr)]
    [InlineData(PdfLayoutFormat.SvgText)]
    public async Task Writes_the_same_bytes_for_the_same_input(PdfLayoutFormat format)
    {
        var first = await ExportAsync(PdfContentFixtures.Text(), format);
        var second = await ExportAsync(PdfContentFixtures.Text(), format);

        Assert.Equal(first, second);
    }

    /// <summary>Text a markup document would read as structure is escaped, not written as structure.</summary>
    /// <param name="format">The format under test.</param>
    /// <returns>A task that completes when the document has been checked.</returns>
    [Theory]
    [InlineData(PdfLayoutFormat.Alto)]
    [InlineData(PdfLayoutFormat.PageXml)]
    [InlineData(PdfLayoutFormat.Hocr)]
    [InlineData(PdfLayoutFormat.SvgText)]
    public async Task Escapes_text_that_looks_like_markup(PdfLayoutFormat format)
    {
        var document = await ParseAsync(PdfContentFixtures.TextMarkup(), format);

        Assert.Equal(["a", "<", "b", "&", "c", ">", "d"], ExportedWords(document, format));
    }

    /// <summary>An ALTO box is stated with the origin at the top left, flipped against the page height.</summary>
    /// <returns>A task that completes when the box has been checked.</returns>
    [Fact]
    public async Task Places_an_alto_box_in_page_space_with_the_origin_at_the_top()
    {
        var bytes = PdfContentFixtures.Text();
        var read = await Reader()
            .ReadBlocksAsync(Source(bytes), PageRange.All, null, null, Token);

        var hello = read.Items.SelectMany(block => block.Words).First(word => word.Text == "Hello");

        var document = await ParseAsync(bytes, PdfLayoutFormat.Alto);
        var element = document
            .Descendants(Name(PdfLayoutFormat.Alto, "String"))
            .Single(candidate => (string?)candidate.Attribute("CONTENT") == "Hello");

        Assert.Equal(Round(hello.BoundingBox.Left), (int)element.Attribute("HPOS")!);
        Assert.Equal(Round(PageHeight - hello.BoundingBox.Top), (int)element.Attribute("VPOS")!);
        Assert.Equal(
            Round(hello.BoundingBox.Right) - Round(hello.BoundingBox.Left),
            (int)element.Attribute("WIDTH")!);
        Assert.InRange((int)element.Attribute("VPOS")!, 0, (int)PageHeight);
    }

    /// <summary>An hOCR page states the box of the page and the ordinal hOCR counts from.</summary>
    /// <returns>A task that completes when the box has been checked.</returns>
    [Fact]
    public async Task Writes_an_hocr_page_box_the_size_of_the_page()
    {
        var document = await ParseAsync(PdfContentFixtures.Text(), PdfLayoutFormat.Hocr);
        var page = Pages(document, PdfLayoutFormat.Hocr).Single();

        Assert.Equal("bbox 0 0 612 792; ppageno 0", (string?)page.Attribute("title"));
    }

    /// <summary>SVG text sits on the baseline of the word, measured from the top of the page.</summary>
    /// <returns>A task that completes when the position has been checked.</returns>
    [Fact]
    public async Task Places_svg_text_on_the_baseline_it_is_drawn_on()
    {
        var bytes = PdfContentFixtures.Text();
        var read = await Reader()
            .ReadBlocksAsync(Source(bytes), PageRange.All, null, null, Token);

        var hello = read.Items.SelectMany(block => block.Words).First(word => word.Text == "Hello");

        var document = await ParseAsync(bytes, PdfLayoutFormat.SvgText);
        var element = document
            .Descendants(Name(PdfLayoutFormat.SvgText, "text"))
            .Single(candidate => candidate.Value == "Hello");

        Assert.Equal(hello.BoundingBox.Left, (double)element.Attribute("x")!, 3);
        Assert.Equal(PageHeight - hello.BaselineY, (double)element.Attribute("y")!, 3);
    }

    /// <summary>The blocks travel back with the export, so a lossy read is never a silent one.</summary>
    /// <returns>A task that completes when the result has been checked.</returns>
    [Fact]
    public async Task Returns_the_blocks_it_wrote_with_the_strategy_that_produced_them()
    {
        var options = new PdfLayoutOptions
        {
            Segmentation = PdfPageSegmentationStrategy.Raw,
            ReadingOrder = PdfReadingOrderStrategy.Raw,
        };

        using var destination = new MemoryStream();

        var written = await Reader()
            .ExportAsync(
                Source(PdfContentFixtures.Text()),
                PageRange.All,
                PdfLayoutFormat.Hocr,
                destination,
                options,
                null,
                Token);

        var read = await Reader()
            .ReadBlocksAsync(Source(PdfContentFixtures.Text()), PageRange.All, options, null, Token);

        Assert.Equal(options.Segmentation, written.Segmentation);
        Assert.Equal(options.ReadingOrder, written.ReadingOrderStrategy);
        Assert.Equal(read.Items.Select(block => block.Text), written.Items.Select(block => block.Text));
        Assert.Empty(written.Warnings);
        Assert.NotEmpty(destination.ToArray());
    }

    /// <summary>A destination that cannot be written is refused before the source is opened.</summary>
    /// <returns>A task that completes when the refusal has been checked.</returns>
    [Fact]
    public async Task Refuses_a_destination_that_cannot_be_written()
    {
        using var destination = new MemoryStream([], writable: false);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Reader().ExportAsync(
            new UnopenableSource(),
            PageRange.All,
            PdfLayoutFormat.Alto,
            destination,
            null,
            null,
            Token));

        Assert.Equal("destination", error.ParamName);
    }

    /// <summary>A format this release does not write is refused before the source is opened.</summary>
    /// <returns>A task that completes when the refusal has been checked.</returns>
    [Fact]
    public async Task Refuses_a_format_this_release_does_not_write()
    {
        using var destination = new MemoryStream();

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Reader().ExportAsync(
            new UnopenableSource(),
            PageRange.All,
            (PdfLayoutFormat)99,
            destination,
            null,
            null,
            Token));

        Assert.Equal("format", error.ParamName);
    }

    /// <summary>A source that fails when it is opened, so a test can prove an export never opened it.</summary>
    private sealed class UnopenableSource : IPdfContentSource
    {
        /// <inheritdoc />
        public string DisplayName => "unopened.pdf";

        /// <inheritdoc />
        public long? Length => null;

        /// <inheritdoc />
        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The source must not be opened before the export is validated.");
    }
}
