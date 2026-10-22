namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>What a text layout does with content that does not fit its maximum width.</summary>
public enum PdfTextOverflow
{
    /// <summary>The call is refused rather than dropping or clipping text.</summary>
    Refuse = 0,

    /// <summary>The overflowing text is clipped at the maximum width.</summary>
    Clip = 1,

    /// <summary>The overflowing text is placed beyond the maximum width.</summary>
    Continue = 2,
}
