namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>How a viewer arranges pages when the document is opened with more than one page visible.</summary>
public enum PdfPageLayout
{
    /// <summary>One page at a time. The default.</summary>
    SinglePage = 0,

    /// <summary>One page at a time, scrolling continuously.</summary>
    OneColumn = 1,

    /// <summary>Two pages at a time, with odd-numbered pages on the left.</summary>
    TwoColumnLeft = 2,

    /// <summary>Two pages at a time, with odd-numbered pages on the right.</summary>
    TwoColumnRight = 3,

    /// <summary>Two pages at a time with continuous scrolling, odd pages on the left.</summary>
    TwoPageLeft = 4,

    /// <summary>Two pages at a time with continuous scrolling, odd pages on the right.</summary>
    TwoPageRight = 5,
}

/// <summary>What a viewer shows beside the page when the document is opened.</summary>
public enum PdfPageMode
{
    /// <summary>The page alone. The default.</summary>
    UseNone = 0,

    /// <summary>The bookmarks panel.</summary>
    UseOutlines = 1,

    /// <summary>The page thumbnails.</summary>
    UseThumbnails = 2,

    /// <summary>Full screen, hiding the viewer's own interface.</summary>
    FullScreen = 3,

    /// <summary>The optional content (layer) panel.</summary>
    UseOptionalContent = 4,

    /// <summary>The attachments panel.</summary>
    UseAttachments = 5,
}

/// <summary>The side binding of a document, which decides how two-page layouts are paired.</summary>
public enum PdfReadingDirection
{
    /// <summary>Left to right, the default.</summary>
    LeftToRight = 0,

    /// <summary>Right to left, as Arabic and Hebrew documents are bound.</summary>
    RightToLeft = 1,
}

/// <summary>Whether a viewer may scale the pages when the document is printed.</summary>
public enum PdfPrintScaling
{
    /// <summary>The viewer may scale to fit the paper.</summary>
    AppDefault = 0,

    /// <summary>The viewer prints at the page size of the document.</summary>
    None = 1,
}

/// <summary>
/// What a viewer should show when it opens the document: the window chrome, the panel beside the page
/// and the way the pages are paired and printed.
/// </summary>
/// <remarks>
/// An edit that sets these preferences replaces the ones the document had, because a caller who states
/// one of them is describing the window the whole document should open in. Every member has the value
/// a document gets when it states nothing, so the defaults are what an empty record writes.
/// </remarks>
public sealed record PdfViewerPreferences
{
    /// <summary>Gets the preferences a document gets when it states none.</summary>
    public static PdfViewerPreferences Default { get; } = new();

    /// <summary>Gets a value indicating whether the viewer hides its tool bar.</summary>
    public bool HideToolbar { get; init; }

    /// <summary>Gets a value indicating whether the viewer hides its menu bar.</summary>
    public bool HideMenubar { get; init; }

    /// <summary>Gets a value indicating whether the viewer hides the rest of its interface.</summary>
    public bool HideWindowUi { get; init; }

    /// <summary>Gets a value indicating whether the viewer sizes its window to the first page.</summary>
    public bool FitWindow { get; init; }

    /// <summary>Gets a value indicating whether the viewer centres its window on the screen.</summary>
    public bool CenterWindow { get; init; }

    /// <summary>Gets a value indicating whether the viewer shows the document title instead of its file name.</summary>
    public bool DisplayDocTitle { get; init; }

    /// <summary>Gets how the pages are arranged.</summary>
    public PdfPageLayout PageLayout { get; init; } = PdfPageLayout.SinglePage;

    /// <summary>Gets what the viewer shows beside the page.</summary>
    public PdfPageMode PageMode { get; init; } = PdfPageMode.UseNone;

    /// <summary>Gets the side the document is bound on.</summary>
    public PdfReadingDirection ReadingDirection { get; init; } = PdfReadingDirection.LeftToRight;

    /// <summary>Gets whether the viewer may scale the pages when printing.</summary>
    public PdfPrintScaling PrintScaling { get; init; } = PdfPrintScaling.AppDefault;

    /// <summary>Validates these preferences.</summary>
    /// <returns>Every problem found, or an empty list when the preferences are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(PageLayout))
        {
            errors.Add($"page layout '{(int)PageLayout}' is not one PDF defines.");
        }

        if (!Enum.IsDefined(PageMode))
        {
            errors.Add($"page mode '{(int)PageMode}' is not one PDF defines.");
        }

        if (!Enum.IsDefined(ReadingDirection))
        {
            errors.Add($"reading direction '{(int)ReadingDirection}' is not one PDF defines.");
        }

        if (!Enum.IsDefined(PrintScaling))
        {
            errors.Add($"print scaling '{(int)PrintScaling}' is not one PDF defines.");
        }

        return errors;
    }
}
