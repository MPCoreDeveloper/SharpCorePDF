namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// The kinds of annotation the editor writes: the eleven a reader can be asked to synthesise or to
/// draw from an appearance stream.
/// </summary>
/// <remarks>
/// <para>
/// The list is closed on purpose. A kind the engine does not model, a 3D view or a movie for example,
/// would be written without a working appearance and without the behaviour its viewer-side reader
/// expects, so it is not offered at all rather than offered half-done.
/// </para>
/// <para>
/// A form widget is not one of these kinds: a widget belongs to a field, and the form surface writes it
/// with its field so the two never disagree about the value being shown.
/// </para>
/// </remarks>
public enum PdfAnnotationKind
{
    /// <summary>A sticky note: an icon on the page, with its text shown by the reader.</summary>
    Text = 0,

    /// <summary>A link: an invisible rectangle that goes to a page or to a URL.</summary>
    Link = 1,

    /// <summary>Text drawn on the page, in a rectangle the caller states.</summary>
    FreeText = 2,

    /// <summary>A stamp such as <c>Approved</c> or <c>Draft</c>, drawn as a marked rectangle.</summary>
    Stamp = 3,

    /// <summary>A translucent mark over one or more quads of text.</summary>
    Highlight = 4,

    /// <summary>A rule under one or more quads of text.</summary>
    Underline = 5,

    /// <summary>A rule through one or more quads of text.</summary>
    StrikeOut = 6,

    /// <summary>A rectangle outline.</summary>
    Square = 7,

    /// <summary>An ellipse outline, inscribed in its rectangle.</summary>
    Circle = 8,

    /// <summary>One straight line between two points.</summary>
    Line = 9,

    /// <summary>Freehand strokes, each a list of points.</summary>
    Ink = 10,
}

/// <summary>
/// What a reader does with an annotation: whether it is printed, whether it zooms with the page, and
/// whether it can be changed.
/// </summary>
/// <remarks>
/// The names are the PDF specification's, and the values are the bits of the <c>/F</c> entry, so a
/// caller that already reads flags from another tool recognises them. <see cref="Print"/> is the flag
/// an annotation needs to survive printing; the editor sets it on every annotation it creates, because
/// an annotation that silently disappears on paper is rarely what a caller meant.
/// </remarks>
#pragma warning disable CA1711 // Describes a bit set, as the PDF specification and the contract name it.
#pragma warning disable S2344 // The same name the contract and the PDF entry use.
[System.Flags]
public enum PdfAnnotationFlags
{
    /// <summary>No flag is set.</summary>
    None = 0,

    /// <summary>The annotation is not drawn and does not react to the pointer.</summary>
    Invisible = 1,

    /// <summary>The annotation is not drawn and does not print, but can still be activated.</summary>
    Hidden = 2,

    /// <summary>The annotation is printed with the page.</summary>
    Print = 4,

    /// <summary>The annotation does not scale when the page is zoomed.</summary>
    NoZoom = 8,

    /// <summary>The annotation does not rotate with the page.</summary>
    NoRotate = 16,

    /// <summary>The annotation is not drawn, but it prints.</summary>
    NoView = 32,

    /// <summary>The annotation must not be changed by a reader.</summary>
    ReadOnly = 64,

    /// <summary>The annotation must not be deleted or moved by a reader.</summary>
    Locked = 128,

    /// <summary>
    /// The annotation is drawn only while the surrounding group is being edited, which is what a
    /// popup of a closed note uses.
    /// </summary>
    ToggleNoView = 256,

    /// <summary>The contents of the annotation must not be changed by a reader.</summary>
    LockedContents = 512,
}
#pragma warning restore S2344
#pragma warning restore CA1711
