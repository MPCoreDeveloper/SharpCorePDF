namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>The separable blend mode a painted shape uses when it is composited.</summary>
public enum PdfBlendMode
{
    /// <summary>The source replaces the destination.</summary>
    Normal = 0,

    /// <summary>The source is multiplied with the destination.</summary>
    Multiply = 1,

    /// <summary>The source and destination are screened together.</summary>
    Screen = 2,

    /// <summary>The result depends on the destination and combines multiply and screen.</summary>
    Overlay = 3,

    /// <summary>The darker of the source and destination is kept.</summary>
    Darken = 4,

    /// <summary>The lighter of the source and destination is kept.</summary>
    Lighten = 5,
}
