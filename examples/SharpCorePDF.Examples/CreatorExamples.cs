using System.Text;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Writes a document that uses the whole creator surface at once: two page sizes, a JPEG that is
/// copied through rather than re-encoded, an outline, an XMP packet, and a page imported from another
/// file so that the result mixes pages it wrote with a page it copied.
/// </summary>
internal static class CreatorExamples
{
    /// <summary>The packet the document carries, with the title the document information states.</summary>
    private const string Packet = """
        <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
         <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
          <rdf:Description rdf:about=""
            xmlns:dc="http://purl.org/dc/elements/1.1/"
            xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
            xmlns:xmp="http://ns.adobe.com/xap/1.0/">
           <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Décompte 12345</rdf:li></rdf:Alt></dc:title>
           <pdf:Producer>SharpCorePDF</pdf:Producer>
           <xmp:CreatorTool>SharpCorePDF examples</xmp:CreatorTool>
          </rdf:Description>
         </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    /// <summary>Builds, writes and reads back a document that shows the creator surface end to end.</summary>
    /// <param name="context">The documents to import from and the ports to read the result with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    public static async Task WriteCreatorParityAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans-subset.ttf");
        var body = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync(fontPath, cancellationToken));
        var heading = PdfTypeface.Standard(StandardFont.Helvetica);

        // A JPEG keeps its own bytes: the file's entropy-coded data becomes the image stream and the
        // DCTDecode filter reads it, so the picture is never inflated and encoded a second time.
        var logo = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.jpg"),
            cancellationToken);

        // A PNG that carries an iCCP chunk keeps its colour: the profile travels with the image and the
        // colour space becomes /ICCBased, instead of the samples being read as if they were sRGB.
        var profiledLogo = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Logo-icc.png"),
            cancellationToken);

        var document = PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with
        {
            CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DocumentId = "00112233445566778899AABBCCDDEEFF",
        })
            .WithMetadata(new PdfMetadata
            {
                Title = "Décompte 12345",
                Author = "Décomptes SPRL",
                Creator = "SharpCorePDF examples",
            })
            .WithXmp(Encoding.UTF8.GetBytes(Packet))
            .WithOutline(
            [
                new PdfOutlineItem { Title = "Couverture", Destination = new PdfDestination { PageNumber = 1 } },
                new PdfOutlineItem
                {
                    Title = "Sommaire",
                    Destination = new PdfDestination { PageNumber = 2 },
                    Children =
                    [
                        new PdfOutlineItem { Title = "Totaux", Destination = new PdfDestination { PageNumber = 2 } },
                    ],
                },
                new PdfOutlineItem
                {
                    Title = "Décompte du fournisseur",
                    Destination = new PdfDestination { PageNumber = 3 },
                },
                new PdfOutlineItem
                {
                    Title = "SharpCorePDF",
                    Destination = new PdfDestination { Uri = "https://github.com/MPCoreDeveloper/SharpCorePDF" },
                },
            ])
            .AddPage(PdfPageSize.A4, page => page
                .DrawImage(logo, x: 40, y: 30, width: 120, height: 120)
                .DrawText("Décomptes SPRL", 330, 60, heading, 12)
                .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345.", 150, 400, body, 12)
                .DrawText("Référence : é è à ç ô ï ' / |", 150, 420, body, 12))
            .AddPage(PdfPageSize.A5, page => page
                .DrawText("Sommaire", 40, 60, heading, 16)
                .DrawText("Total : 1 234,56 EUR", 40, 84, body, 12)
                .DrawImage(profiledLogo, x: 40, y: 120, width: 152, height: 87))

            // The third page is not written here at all: it is copied out of a document that already
            // exists, with its content, its resources and its annotations as they are.
            .ImportPage(PdfFileSource.From(context.Summary, "summary.pdf"), 1)
            .Build("creator-parity.pdf");

        var path = context.OutputPath("creator-parity.pdf");
        await File.WriteAllBytesAsync(path, document.Bytes.ToArray(), cancellationToken);

        var info = await context.Inspector.InspectAsync(document, cancellationToken: cancellationToken);
        var outline = await context.ContentReader.ReadOutlineAsync(document, cancellationToken: cancellationToken);
        var metadata = await context.ContentReader.ReadMetadataAsync(document, cancellationToken: cancellationToken);
        var images = await context.ContentReader.ReadImagesAsync(
            document,
            PageRange.Single(1),
            cancellationToken: cancellationToken);

        var image = images.Items[0];

        var profiled = await context.ContentReader.ReadImagesAsync(
            document,
            PageRange.Single(2),
            cancellationToken: cancellationToken);

        Report.Item("wrote", path);
        Report.Item("pages", info.PageCount);
        Report.Item("page sizes", string.Join(", ", info.Pages.Select(page => page.Size.ToString())));
        Report.Item("bookmarks", string.Join(" > ", outline.Items.Select(item => item.Title)));
        Report.Item("metadata title", metadata.Metadata?.Title);
        Report.Item("xmp title", metadata.Metadata?.Title);
        Report.Item("image", $"{image.Width} x {image.Height} px, {image.Filter}, {image.ColorSpace}");
        Report.Item("profiled image", $"{profiled.Items[0].Width} x {profiled.Items[0].Height} px, {profiled.Items[0].ColorSpace}");
        Report.Item("bytes", document.Length);
    }
}
