using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One annotation on a page: a comment, a stamp, a form widget's appearance, a hyperlink.
/// </summary>
/// <remarks>
/// <para>
/// The subtype is kept as the document spells it (<c>Text</c>, <c>Highlight</c>, <c>Stamp</c>,
/// <c>Link</c>, …) rather than reduced to a closed enum, so an annotation this library does not model
/// is still returned with its known fields populated instead of being dropped.
/// </para>
/// <para>
/// A link is an annotation whose <see cref="Target"/> is set; <see cref="Subtype"/> is then
/// <c>Link</c>. Nothing is rendered: the appearance stream is reported as present, not produced.
/// </para>
/// </remarks>
public sealed record PdfAnnotationInfo
{
    /// <summary>Gets the one-based number of the page the annotation belongs to.</summary>
    public required int PageNumber { get; init; }

    /// <summary>Gets the annotation subtype as the document spells it, without the leading slash.</summary>
    public required string Subtype { get; init; }

    /// <summary>
    /// Gets the rectangle the annotation occupies in page space, or <see langword="null"/> when the
    /// annotation states none that can be read.
    /// </summary>
    public PdfRectangle? Rectangle { get; init; }

    /// <summary>Gets the annotation flags as the document states them.</summary>
    public int Flags { get; init; }

    /// <summary>Gets the text of the annotation, or <see langword="null"/> when it carries none.</summary>
    public string? Contents { get; init; }

    /// <summary>Gets the author of the annotation, or <see langword="null"/> when it states none.</summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the moment the annotation was last modified, or <see langword="null"/> when it states none
    /// or states one that cannot be read.
    /// </summary>
    public DateTimeOffset? ModificationDate { get; init; }

    /// <summary>Gets a value indicating whether the annotation carries an appearance stream.</summary>
    public bool HasAppearance { get; init; }

    /// <summary>
    /// Gets the identifier the annotation states in its <c>/NM</c> entry, or <see langword="null"/>.
    /// Annotations that do not state one have no identifier in the document at all.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets where the annotation points, for a link or a widget with an action, or
    /// <see langword="null"/> for an annotation that points nowhere.
    /// </summary>
    public PdfDestination? Target { get; init; }

    /// <summary>Gets a value indicating whether the annotation is a hyperlink.</summary>
    public bool IsLink => string.Equals(Subtype, "Link", StringComparison.Ordinal);

    /// <summary>Formats the annotation for diagnostics.</summary>
    /// <returns>The subtype, the page and the target when it has one.</returns>
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{Subtype} on page {PageNumber}{(Target is null ? string.Empty : $" -> {Target}")}");
}
