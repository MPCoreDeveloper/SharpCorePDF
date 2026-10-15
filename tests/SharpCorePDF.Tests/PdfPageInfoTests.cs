using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the page description a caller reads out of an inspection: the size a viewer displays, and the
/// text a diagnostic shows for a page.
/// </summary>
/// <remarks>
/// The size stored in a document is the size before any rotation, so the two sizes differ for the quarter
/// turns and that difference is the whole point of <see cref="PdfPageInfo.DisplaySize"/>.
/// </remarks>
public sealed class PdfPageInfoTests
{
    /// <summary>A rotated page is displayed with its width and its height swapped.</summary>
    [Fact]
    public void Reports_the_size_a_viewer_displays()
    {
        var page = new PdfPageInfo(2, PdfPageSize.Letter, PageRotation.None, HasContent: true, AnnotationCount: 1);

        Assert.Equal(PdfPageSize.Letter, page.DisplaySize);
        Assert.Equal(PdfPageSize.Letter.Transpose(), (page with { Rotation = PageRotation.Clockwise90 }).DisplaySize);
        Assert.Equal(PdfPageSize.Letter.Transpose(), (page with { Rotation = PageRotation.Clockwise270 }).DisplaySize);
        Assert.Equal(PdfPageSize.Letter, (page with { Rotation = PageRotation.Clockwise180 }).DisplaySize);
    }

    /// <summary>The text of a page names its number, its size and its rotation when it has one.</summary>
    [Fact]
    public void Formats_a_page()
    {
        var page = new PdfPageInfo(2, PdfPageSize.Letter, PageRotation.None, HasContent: true, AnnotationCount: 1);
        var turned = page with { Rotation = PageRotation.Clockwise90 };

        Assert.Equal(2, page.Number);
        Assert.True(page.HasContent);
        Assert.Equal(1, page.AnnotationCount);
        Assert.Equal("page 2: 612 x 792 pt", page.ToString());
        Assert.Equal("page 2: 612 x 792 pt rotated 90 degrees", turned.ToString());
    }
}
