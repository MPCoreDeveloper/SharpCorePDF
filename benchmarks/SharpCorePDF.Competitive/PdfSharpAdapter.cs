using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace SharpCorePDF.Competitive;

internal sealed class PdfSharpAdapter : EngineAdapter
{
    private const string FaceName = "CompetitiveDejaVu";

    static PdfSharpAdapter()
    {
        // PDFsharp 6.5 requires an explicit licence selection. This selects its upstream AGPL option
        // for this isolated harness only; it says nothing about SharpCorePDF's MIT-licensed packages.
        PdfSharpLicense.SetAgplLicense();
    }

    public PdfSharpAdapter(byte[] fontBytes)
        : base("PDFsharp", typeof(PdfDocument).Assembly) =>
        GlobalFontSettings.FontResolver = new SingleFontResolver(fontBytes);

    public override bool SupportsCreation => true;
    public override bool SupportsMerge => true;

    public override ValueTask<WriteObservation> CreateAsync(
        int pages,
        int linesPerPage,
        CancellationToken cancellationToken,
        string? capturePath = null)
    {
        using var document = new PdfDocument();
        var font = new XFont(
            FaceName,
            12,
            XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEmbedding.TryComputeSubset));

        for (var pageNumber = 1; pageNumber <= pages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(595);
            page.Height = XUnit.FromPoint(842);
            using var graphics = XGraphics.FromPdfPage(page);
            for (var line = 1; line <= linesPerPage; line++)
            {
                graphics.DrawString(
                    SharpCorePdfAdapter.CreationText(pageNumber, line),
                    font,
                    XBrushes.Black,
                    new XPoint(40, 60 + ((line - 1) * 14)));
            }
        }

        using Stream destination = capturePath is null ? new CountingStream() : File.Create(capturePath);
        document.Save(destination, false);
        destination.Flush();
        return ValueTask.FromResult(new WriteObservation(destination.Length, pages));
    }

    public override ValueTask<WriteObservation> MergeAsync(
        IReadOnlyList<byte[]> sources,
        CancellationToken cancellationToken,
        string? capturePath = null)
    {
        using var result = new PdfDocument();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var content = new MemoryStream(source, writable: false);
            using var document = PdfReader.Open(content, PdfDocumentOpenMode.Import);
            foreach (PdfPage page in document.Pages)
            {
                result.AddPage(page);
            }
        }

        using Stream destination = capturePath is null ? new CountingStream() : File.Create(capturePath);
        var pageCount = result.PageCount;
        result.Save(destination, false);
        destination.Flush();
        return ValueTask.FromResult(new WriteObservation(destination.Length, pageCount));
    }

    private sealed class SingleFontResolver(byte[] bytes) : IFontResolver
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1822:Mark members as static",
            Justification = "IFontResolver requires an instance property.")]
        public string DefaultFontName => FaceName;

        public byte[] GetFont(string faceName)
        {
            if (!string.Equals(faceName, FaceName, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown benchmark font face '{faceName}'.", nameof(faceName));
            }

            return bytes;
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (isBold || isItalic || !string.Equals(familyName, FaceName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The competitive harness supplies one regular font face.", nameof(familyName));
            }

            return new FontResolverInfo(FaceName);
        }
    }
}
