using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of a source whose content stream is open and whose page selection has been resolved against the
/// document it came from.
/// </summary>
/// <remarks>
/// The type is a record with no behaviour beyond its diagnostic form: it exists so the engine is handed the
/// pages that were actually resolved rather than a selection that still has to be interpreted.
/// </remarks>
public sealed class ResolvedPdfSourceTests
{
    /// <summary>The diagnostic form names the source, the pages taken, and how many the document holds.</summary>
    [Fact]
    public void Formats_the_source_for_diagnostics()
    {
        var resolved = new ResolvedPdfSource
        {
            Source = PdfSource.Create(new PdfByteArraySource([1], "report.pdf")),
            Content = new MemoryStream([], writable: false),
            Info = new PdfDocumentInfo
            {
                SourceName = "report.pdf",
                Version = new PdfVersion(1, 7),
                PageCount = 3,
            },
            Pages = [1, 3],
        };

        Assert.Equal("report.pdf [1,3] of 3 page(s)", resolved.ToString());
    }
}
