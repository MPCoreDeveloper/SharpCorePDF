namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>Whether the viewer may adjust stroke coordinates to produce cleaner lines.</summary>
public enum PdfStrokeAdjustment
{
    /// <summary>The viewer's default is used, which is normally enabled.</summary>
    Default = 0,

    /// <summary>Stroke adjustment is requested.</summary>
    Enabled = 1,

    /// <summary>Stroke adjustment is suppressed.</summary>
    Disabled = 2,
}
