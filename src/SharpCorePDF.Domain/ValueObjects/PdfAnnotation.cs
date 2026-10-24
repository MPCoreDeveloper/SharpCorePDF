using System.Globalization;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// One annotation to write on a page: a note, a link, a mark over text, a shape or a stamp.
/// </summary>
/// <remarks>
/// <para>
/// The record holds the properties every kind shares and the geometry each kind needs, and
/// <see cref="Validate"/> checks that the second matches the first: a highlight without a quad, an ink
/// annotation without a stroke or a link with both a page and a URL is refused before the source is
/// opened, not written and left for a reader to interpret.
/// </para>
/// <para>
/// Which fields are used by which kind:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="PdfAnnotationKind.Link"/>: <see cref="Destination"/>; nothing is drawn.</description></item>
/// <item><description>Text, FreeText, Stamp: <see cref="Contents"/>, and a <see cref="TextStyle"/> for FreeText.</description></item>
/// <item><description>Highlight, Underline, StrikeOut: <see cref="Quads"/>.</description></item>
/// <item><description>Line: <see cref="LineFrom"/> and <see cref="LineTo"/>.</description></item>
/// <item><description>Ink: <see cref="Strokes"/>.</description></item>
/// <item><description>Square, Circle: the rectangle alone.</description></item>
/// </list>
/// <para>
/// An annotation the engine writes always carries an appearance stream unless <see cref="Appearance"/>
/// overrides the one it would generate, so a viewer that does not synthesise appearances still shows
/// the annotation, and one that prints the page prints it.
/// </para>
/// </remarks>
public sealed record PdfAnnotation
{
    /// <summary>Gets the kind of annotation, which decides what is written and what is drawn.</summary>
    public required PdfAnnotationKind Kind { get; init; }

    /// <summary>Gets the rectangle the annotation occupies, in page space.</summary>
    public required PdfRectangle Rectangle { get; init; }

    /// <summary>
    /// Gets the identifier the annotation is written with in its <c>/NM</c> entry, or
    /// <see langword="null"/> to have the engine name it.
    /// </summary>
    /// <remarks>
    /// The identifier is how a later <c>UpdateAnnotation</c> or <c>RemoveAnnotation</c> operation names
    /// this annotation, and how a read of the page reports it. An annotation created without one is
    /// named <c>scpdf-N</c>, where N is the number of annotations the plan has created so far.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>Gets the text of the annotation, or <see langword="null"/> for one that carries none.</summary>
    public string? Contents { get; init; }

    /// <summary>
    /// Gets the colour of the annotation: the fill of a mark, the stroke of a shape or line, the border
    /// of a note. <see langword="null"/> leaves the kind's own default.
    /// </summary>
    public PdfColor? Color { get; init; }

    /// <summary>Gets the opacity of the annotation, from <c>0</c> (invisible) to <c>1</c> (opaque).</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>
    /// Gets the flags of the annotation. The engine adds <see cref="PdfAnnotationFlags.Print"/> to them
    /// when it writes the annotation, so an annotation is not lost when the page is printed.
    /// </summary>
    public PdfAnnotationFlags Flags { get; init; }

    /// <summary>Gets the border, or <see langword="null"/> for the kind's own default.</summary>
    public PdfBorder? Border { get; init; }

    /// <summary>
    /// Gets where a link points: a page of this document, a named destination, or a URL. It is required
    /// for <see cref="PdfAnnotationKind.Link"/> and ignored for every other kind.
    /// </summary>
    public PdfDestination? Destination { get; init; }

    /// <summary>Gets the appearance to write, or <see langword="null"/> to have one generated.</summary>
    public PdfAnnotationAppearance? Appearance { get; init; }

    /// <summary>
    /// Gets the name of the stamp icon: <c>Approved</c>, <c>Draft</c>, <c>Confidential</c>,
    /// <c>Final</c>, <c>Experimental</c>, <c>NotApproved</c>, <c>AsIs</c>, <c>Expired</c>,
    /// <c>NotForPublicRelease</c>, <c>Sold</c>, <c>ForComment</c>, <c>TopSecret</c> or <c>Departmental</c>.
    /// </summary>
    public string? IconName { get; init; }

    /// <summary>Gets the style the text of a free-text annotation is drawn with.</summary>
    public PdfStandardTextStyle? TextStyle { get; init; }

    /// <summary>Gets the quads a markup annotation covers, in page space.</summary>
    public IReadOnlyList<PdfRectangle> Quads { get; init; } = [];

    /// <summary>Gets the point a line annotation starts at.</summary>
    public PdfPoint? LineFrom { get; init; }

    /// <summary>Gets the point a line annotation ends at.</summary>
    public PdfPoint? LineTo { get; init; }

    /// <summary>
    /// Gets the freehand strokes of an ink annotation. Each stroke is a list of at least two points, in
    /// page space; the strokes are not joined to one another.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<PdfPoint>> Strokes { get; init; } = [];

    /// <summary>Validates the annotation.</summary>
    /// <returns>Every problem found, or an empty list when the annotation can be written.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Kind))
        {
            errors.Add($"annotation kind '{(int)Kind}' is not one the engine writes.");
            return errors;
        }

        if (Rectangle.IsEmpty)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the annotation rectangle {Rectangle} encloses no area."));
        }

        if (!double.IsFinite(Opacity) || Opacity is < 0.0 or > 1.0)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the annotation opacity {Opacity} is outside 0-1."));
        }

        if (((int)Flags & ~KnownFlags) != 0)
        {
            errors.Add($"the annotation flags '{(int)Flags}' contain a bit the PDF specification does not define.");
        }

        if (Border is { } border)
        {
            errors.AddRange(border.Validate());
        }

        if (Appearance is { } appearance)
        {
            errors.AddRange(appearance.Validate());
        }

        if (TextStyle is { } style)
        {
            errors.AddRange(style.Validate());
        }

        if (Name is { Length: 0 })
        {
            errors.Add("the annotation name is empty; leave it unset to have the engine name the annotation.");
        }

        errors.AddRange(ValidateKind());
        return errors;
    }

    /// <summary>Formats the annotation for diagnostics.</summary>
    /// <returns>The kind, the identifier and the rectangle.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Kind} annotation {Name ?? "(unnamed)"} at {Rectangle}");

    /// <summary>Every flag the PDF specification defines for an annotation.</summary>
    private const int KnownFlags = 0x3FF;

    /// <summary>
    /// The distance, in points, below which two coordinates are the same point. A tenth of a thousandth
    /// of a point is far below anything a page can resolve and far above the noise of a computed value.
    /// </summary>
    private const double PointTolerance = 1e-4;

    /// <summary>Checks the properties the annotation's kind requires.</summary>
    /// <returns>Every problem found, or an empty sequence.</returns>
    private IEnumerable<string> ValidateKind()
    {
        switch (Kind)
        {
            case PdfAnnotationKind.Link:
                if (Destination is not { } destination)
                {
                    yield return "a link annotation needs a destination: a page or a URL.";
                }
                else if ((destination.Uri is null) == (destination.PageNumber is null))
                {
                    yield return "a link annotation needs exactly one of a URL or a page of this document.";
                }

                break;

            case PdfAnnotationKind.FreeText when string.IsNullOrEmpty(Contents):
                yield return "a free-text annotation needs the text it draws.";
                break;

            case PdfAnnotationKind.Stamp when string.IsNullOrWhiteSpace(IconName) && Appearance is null:
                yield return "a stamp needs an icon name or an appearance of its own.";
                break;

            case PdfAnnotationKind.Highlight:
            case PdfAnnotationKind.Underline:
            case PdfAnnotationKind.StrikeOut:
                if (Quads.Count == 0)
                {
                    yield return "a markup annotation needs at least one quad to cover.";
                }
                else if (Quads.Any(quad => quad is null || quad.IsEmpty))
                {
                    yield return "a markup annotation has a quad that encloses no area.";
                }

                if (Color is null)
                {
                    yield return "a markup annotation needs the colour it is drawn in.";
                }

                break;

            case PdfAnnotationKind.Line:
                if (LineFrom is not { } from || LineTo is not { } to)
                {
                    yield return "a line annotation needs both of its points.";
                }
                else if (Math.Abs(from.X - to.X) <= PointTolerance && Math.Abs(from.Y - to.Y) <= PointTolerance)
                {
                    yield return "a line annotation has the same point at both ends.";
                }

                break;

            case PdfAnnotationKind.Ink:
                if (Strokes.Count == 0)
                {
                    yield return "an ink annotation needs at least one stroke.";
                }
                else if (Strokes.Any(stroke => stroke is null || stroke.Count < 2))
                {
                    yield return "an ink annotation has a stroke with fewer than two points.";
                }

                break;
        }
    }
}
