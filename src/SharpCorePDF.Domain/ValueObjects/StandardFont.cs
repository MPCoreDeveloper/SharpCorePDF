namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// One of the twelve standard PDF fonts a page can be written with, without embedding a font program.
/// </summary>
/// <remarks>
/// <para>
/// A standard font is not stored in the document: a reader supplies it from its own resources, which is
/// what makes a page that uses one small and portable. Every font listed here is written with
/// <c>/WinAnsiEncoding</c>, so text must be expressible in that encoding — Latin-1 plus the
/// typographic characters of Windows-1252. Use an embedded TrueType font for anything else.
/// </para>
/// <para>
/// The two symbolic standard fonts, Symbol and ZapfDingbats, are deliberately absent: their codes do
/// not stand for Unicode text, so text written with them could not be read back.
/// </para>
/// </remarks>
public enum StandardFont
{
    /// <summary>Helvetica, the sans-serif face of the standard set.</summary>
    Helvetica,

    /// <summary>Helvetica in bold.</summary>
    HelveticaBold,

    /// <summary>Helvetica in italic.</summary>
    HelveticaOblique,

    /// <summary>Helvetica in bold italic.</summary>
    HelveticaBoldOblique,

    /// <summary>Times Roman, the serif face of the standard set.</summary>
    TimesRoman,

    /// <summary>Times Roman in bold.</summary>
    TimesBold,

    /// <summary>Times Roman in italic.</summary>
    TimesItalic,

    /// <summary>Times Roman in bold italic.</summary>
    TimesBoldItalic,

    /// <summary>Courier, the monospaced face of the standard set.</summary>
    Courier,

    /// <summary>Courier in bold.</summary>
    CourierBold,

    /// <summary>Courier in italic.</summary>
    CourierOblique,

    /// <summary>Courier in bold italic.</summary>
    CourierBoldOblique,
}
