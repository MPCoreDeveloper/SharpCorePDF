using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The face a page sets text in: one of the twelve standard PDF fonts, which needs no font program, or
/// a TrueType font program embedded in the document.
/// </summary>
/// <remarks>
/// <para>
/// A standard font is written with <c>/WinAnsiEncoding</c>. It keeps the document small and needs no
/// font file, but text must be expressible in Windows-1252 — Latin-1 plus the typographic characters
/// such as the curly apostrophe and the euro sign.
/// </para>
/// <para>
/// A TrueType typeface carries its font program in the document, subsetted to the glyphs the pages use,
/// with a <c>/ToUnicode</c> map so the text can be extracted again. Nothing is read from the machine the
/// code runs on: the bytes the caller supplies are the only source of the face. An instance is immutable
/// and can be shared by any number of builders.
/// </para>
/// </remarks>
public sealed class PdfTypeface
{
    private readonly TrueTypeFont? embedded;

    private PdfTypeface(StandardFont family) => Family = family;

    private PdfTypeface(TrueTypeFont font)
    {
        embedded = font;
        FontProgram = font.Data;
    }

    /// <summary>Gets the standard family, or <see langword="null"/> when this is an embedded font.</summary>
    public StandardFont? Family { get; }

    /// <summary>Gets a value indicating whether the face carries its own font program.</summary>
    public bool IsEmbedded => embedded is not null;

    /// <summary>Gets the name of the face, for diagnostics.</summary>
    public string Name => embedded?.Name ?? Family?.ToString() ?? "font";

    /// <summary>Gets the bytes of the embedded font program, or <see langword="null"/> for a standard font.</summary>
    public ReadOnlyMemory<byte>? FontProgram { get; }

    /// <summary>Gets the parsed program of an embedded face.</summary>
    internal TrueTypeFont Embedded =>
        embedded ?? throw new InvalidOperationException("This typeface is a standard font and has no font program.");

    /// <summary>Creates a standard typeface.</summary>
    /// <param name="family">The standard font to use.</param>
    /// <returns>The typeface.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="family"/> is not a defined standard font.</exception>
    public static PdfTypeface Standard(StandardFont family)
    {
        if (!Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(nameof(family), family, "The standard font is not one the engine defines.");
        }

        return new PdfTypeface(family);
    }

    /// <summary>Creates a typeface from a TrueType font program.</summary>
    /// <param name="fontProgram">The bytes of the TrueType (or sfnt wrapped TrueType) file.</param>
    /// <returns>The typeface.</returns>
    /// <remarks>
    /// The bytes are copied and parsed immediately, so a caller may reuse or change its buffer, and a
    /// malformed program is reported where it was handed over rather than at the first page that uses
    /// it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="fontProgram"/> is empty or not a TrueType font.</exception>
    /// <exception cref="NotSupportedException">The program uses PostScript outlines.</exception>
    public static PdfTypeface FromTrueType(ReadOnlyMemory<byte> fontProgram)
    {
        if (fontProgram.IsEmpty)
        {
            throw new ArgumentException("A font program is needed to create an embedded typeface.", nameof(fontProgram));
        }

        var bytes = fontProgram.ToArray();

        return new PdfTypeface(TrueTypeFont.Parse(bytes));
    }

    /// <summary>Returns the name of the face.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
