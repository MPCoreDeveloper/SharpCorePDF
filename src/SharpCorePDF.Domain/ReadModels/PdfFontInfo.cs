using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// One font a document uses, as the document itself states it, produced by
/// <see cref="Ports.IPdfFontInspector.ReadFontsAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// A font is reported once however many pages select it: the pages travel together in
/// <see cref="PageNumbers"/>, so a document that sets one face on two hundred pages answers with one entry
/// rather than two hundred. <see cref="ResourceName"/> is the name the first page that selects the font
/// gives it, because a resource name is a property of the page that states it and not of the font.
/// </para>
/// <para>
/// Nothing here is inferred. A document that states no descriptor carries no program, and
/// <see cref="Program"/> says so; a subset is reported only when the base font name carries the tag the
/// specification defines for one.
/// </para>
/// </remarks>
public sealed record PdfFontInfo
{
    /// <summary>Gets the name the page resources give the font, for example <c>F1</c>, without a slash.</summary>
    public required string ResourceName { get; init; }

    /// <summary>
    /// Gets the base font name the document states, for example <c>ABCDEF+Arial</c>, or the resource name
    /// when the dictionary states none.
    /// </summary>
    public required string BaseFont { get; init; }

    /// <summary>
    /// Gets the kind of font, resolved through a composite font's descendant: a <c>Type0</c> font is
    /// reported as the <c>CIDFontType0</c> or <c>CIDFontType2</c> it points at.
    /// </summary>
    public required PdfFontKind Kind { get; init; }

    /// <summary>
    /// Gets how an embedded font program is stored, or <see cref="PdfEmbeddedFontProgramKind.None"/> when
    /// the document carries no program for this font.
    /// </summary>
    public PdfEmbeddedFontProgramKind Program { get; init; }

    /// <summary>
    /// Gets the encoding the document states, for example <c>WinAnsiEncoding</c> or <c>Identity-H</c>, or
    /// <see langword="null"/> when it states none.
    /// </summary>
    public string? Encoding { get; init; }

    /// <summary>
    /// Gets a value indicating whether the font states a <c>/ToUnicode</c> map, which is what makes the
    /// text it draws readable as characters rather than as codes.
    /// </summary>
    public bool HasToUnicode { get; init; }

    /// <summary>
    /// Gets a value indicating whether the base font name carries a subset tag — six capital letters and a
    /// plus sign — which is a document's statement that the program holds only the glyphs it draws.
    /// </summary>
    public bool IsSubset { get; init; }

    /// <summary>
    /// Gets the one-based numbers of the pages whose resources select this font, in ascending order.
    /// </summary>
    public IReadOnlyList<int> PageNumbers { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the document carries the program of this font. A Type 3 font draws
    /// its glyphs with content streams rather than a program file; when it states its <c>/CharProcs</c> the
    /// document carries everything it needs, and when it states none it draws nothing at all.
    /// </summary>
    public bool IsEmbedded => Program != PdfEmbeddedFontProgramKind.None;

    /// <summary>Formats the font for diagnostics.</summary>
    /// <returns>The formatted font.</returns>
    public override string ToString() =>
        $"{BaseFont}: {Kind}, {(IsEmbedded ? "embedded" : "not embedded")}"
        + (IsSubset ? ", subset" : string.Empty)
        + (PageNumbers.Count > 0 ? $", {PageNumbers.Count} page(s)" : string.Empty);
}
