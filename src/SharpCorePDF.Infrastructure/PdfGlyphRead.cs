using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The size a page declares, as a read of the glyphs saw it: the page number and the extent of the page
/// in points, before any rotation a viewer applies.
/// </summary>
/// <remarks>
/// A read that groups glyphs does not need the page's size, but an export does: the coordinates of the
/// read are in page space with the origin at the bottom left, and every export format states them with the
/// origin at the top left. The height is the only number needed to flip one into the other, and it belongs
/// to the page rather than to any glyph, so it travels beside the glyphs.
/// </remarks>
/// <param name="PageNumber">The one-based number of the page.</param>
/// <param name="Width">The width of the page, in points.</param>
/// <param name="Height">The height of the page, in points.</param>
internal sealed record PdfPageBox(int PageNumber, double Width, double Height);

/// <summary>
/// What a glyph read produced: the glyphs of a range with their remarks, and the box of every page the
/// read visited.
/// </summary>
/// <remarks>
/// A page whose content could not be read still appears in <see cref="Pages"/> — it exists, and an export
/// has to state its size even when it draws nothing — while <see cref="Glyphs"/> carries no glyph for it.
/// </remarks>
/// <param name="Glyphs">The glyphs of the range, page by page and in the order each page draws them.</param>
/// <param name="Pages">The boxes of the pages of the range, in ascending page order.</param>
internal sealed record PdfGlyphRead(PdfReadResult<PdfGlyph> Glyphs, IReadOnlyList<PdfPageBox> Pages);
