using System.Globalization;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Determines how the page of an overlay or underlay document is placed on the page it is applied to.
/// </summary>
public enum PdfOverlayPlacement
{
    /// <summary>
    /// The overlay page is scaled until it fits the visible area, keeping its aspect ratio, and centred
    /// in it. This is the default: a stamp drawn for one page size lands on another without distortion.
    /// </summary>
    Fit = 0,

    /// <summary>
    /// The overlay page is scaled in both directions until it fills the visible area, so its aspect
    /// ratio may change. Useful for a background that is meant to cover the page exactly.
    /// </summary>
    Stretch = 1,

    /// <summary>
    /// The overlay page is placed at its own size, anchored at the lower-left corner of the visible
    /// area. No part of it is scaled, so a watermark drawn at page size lands where it was drawn.
    /// </summary>
    OriginalSize = 2,
}

/// <summary>Determines how an overlay is composited with the content it is placed over or under.</summary>
/// <remarks>
/// These are the separable blend modes of the PDF imaging model; every one of them is written as a
/// single <c>/BM</c> name in the graphics state. A mode the engine cannot express is refused by
/// <see cref="PdfOverlayOptions.Validate"/> rather than approximated.
/// </remarks>
public enum PdfOverlayBlendMode
{
    /// <summary>The overlay replaces the content underneath it, weighted by its opacity. The default.</summary>
    Normal = 0,

    /// <summary>The overlay darkens the content underneath it, as ink does.</summary>
    Multiply = 1,

    /// <summary>The overlay lightens the content underneath it.</summary>
    Screen = 2,

    /// <summary>The overlay multiplies dark areas and screens light ones.</summary>
    Overlay = 3,

    /// <summary>Only the darker of the overlay and the content underneath it is kept.</summary>
    Darken = 4,

    /// <summary>Only the lighter of the overlay and the content underneath it is kept.</summary>
    Lighten = 5,
}

/// <summary>
/// Controls how one page of an overlay or underlay document is recorded on the page it is applied to.
/// </summary>
/// <remarks>
/// <para>
/// An overlay is content, not an annotation: its operators are recorded in a form XObject behind or in
/// front of the page's own content stream, which is never rewritten. A reader that ignores the overlay
/// still shows the page, and a reader that removes it still shows every byte the page had.
/// </para>
/// <para>
/// Opacity and blend mode are written through an <c>ExtGState</c>, so they apply to the overlay alone
/// and not to the page it lands on.
/// </para>
/// </remarks>
public sealed record PdfOverlayOptions
{
    /// <summary>Gets the options used when a caller states none: opaque, fitted, clipped, normal blend.</summary>
    public static PdfOverlayOptions Default { get; } = new();

    /// <summary>Gets the opacity of the overlay, from <c>0</c> (invisible) to <c>1</c> (opaque).</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>Gets how the overlay page is placed on the page it is applied to.</summary>
    public PdfOverlayPlacement Placement { get; init; } = PdfOverlayPlacement.Fit;

    /// <summary>
    /// Gets a value indicating whether the overlay is clipped to the visible area of the page. Defaults
    /// to <see langword="true"/>: an overlay that reaches past the page would otherwise paint outside
    /// it, which most viewers cut away anyway and none of them agrees about.
    /// </summary>
    public bool Clip { get; init; } = true;

    /// <summary>Gets how the overlay is composited with the content it is placed over or under.</summary>
    public PdfOverlayBlendMode BlendMode { get; init; } = PdfOverlayBlendMode.Normal;

    /// <summary>Validates these options.</summary>
    /// <returns>Every problem found, or an empty list when the options are usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!double.IsFinite(Opacity) || Opacity is < 0.0 or > 1.0)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the overlay opacity {Opacity} is outside 0-1."));
        }

        if (!Enum.IsDefined(Placement))
        {
            errors.Add($"overlay placement '{(int)Placement}' is not defined.");
        }

        if (!Enum.IsDefined(BlendMode))
        {
            errors.Add($"overlay blend mode '{(int)BlendMode}' is not one the engine writes.");
        }

        return errors;
    }
}
