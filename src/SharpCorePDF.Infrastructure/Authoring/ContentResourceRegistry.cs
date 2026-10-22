using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>One <c>/ExtGState</c> dictionary and the name a content stream selects it by.</summary>
internal sealed record ExtGStateResource(string Name, PdfDictionary Dictionary);

/// <summary>
/// The graphics-state resources one content stream selects: alpha, blend mode and stroke adjustment.
/// </summary>
/// <remarks>
/// An identical state is registered once, so drawing a hundred translucent shapes writes one
/// <c>/ExtGState</c> entry rather than a hundred. The registry is owned by the page that writes the
/// stream and is merged into that page's resource dictionary when the page is written.
/// </remarks>
internal sealed class ContentResourceRegistry
{
    private readonly Dictionary<string, ExtGStateResource> byKey = new(StringComparer.Ordinal);
    private readonly List<ExtGStateResource> inOrder = [];
    private int nextName;

    /// <summary>Gets the registered graphics states, in the order they were first used.</summary>
    public IReadOnlyList<ExtGStateResource> ExtGStates => inOrder;

    /// <summary>Gets a value indicating whether no resource has been registered.</summary>
    public bool IsEmpty => inOrder.Count == 0;

    /// <summary>
    /// Gets the name of a graphics state, registering it when the same state has not been used before.
    /// </summary>
    /// <param name="fillAlpha">The non-stroking alpha, from 0 to 1.</param>
    /// <param name="strokeAlpha">The stroking alpha, from 0 to 1.</param>
    /// <param name="blendMode">The blend mode.</param>
    /// <param name="strokeAdjustment">The optional stroke adjustment.</param>
    /// <returns>The resource name, for example <c>GS1</c>.</returns>
    public string Register(double fillAlpha, double strokeAlpha, PdfBlendMode blendMode, PdfStrokeAdjustment? strokeAdjustment)
    {
        // Default means "let the viewer decide", which is the same dictionary as leaving /SA out.
        var adjustment = strokeAdjustment == PdfStrokeAdjustment.Default ? null : strokeAdjustment;
        var key = Key(fillAlpha, strokeAlpha, blendMode, adjustment);

        if (!byKey.TryGetValue(key, out var resource))
        {
            resource = new ExtGStateResource($"GS{++nextName}", Build(fillAlpha, strokeAlpha, blendMode, adjustment));
            byKey[key] = resource;
            inOrder.Add(resource);
        }

        return resource.Name;
    }

    /// <summary>Builds the <c>/ExtGState</c> dictionary of the page, or null when no state was used.</summary>
    /// <returns>The dictionary, with one entry per registered state.</returns>
    public PdfDictionary? ToDictionary()
    {
        if (inOrder.Count == 0)
        {
            return null;
        }

        var dictionary = new PdfDictionary();

        foreach (var resource in inOrder)
        {
            dictionary.Set(resource.Name, resource.Dictionary);
        }

        return dictionary;
    }

    /// <summary>Gets the name of the blend mode as PDF writes it.</summary>
    /// <param name="blendMode">The blend mode.</param>
    /// <returns>The PDF name.</returns>
    internal static string BlendModeName(PdfBlendMode blendMode) => blendMode switch
    {
        PdfBlendMode.Normal => "Normal",
        PdfBlendMode.Multiply => "Multiply",
        PdfBlendMode.Screen => "Screen",
        PdfBlendMode.Overlay => "Overlay",
        PdfBlendMode.Darken => "Darken",
        PdfBlendMode.Lighten => "Lighten",
        _ => throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, "The blend mode is not one the engine writes."),
    };

    private static PdfDictionary Build(
        double fillAlpha,
        double strokeAlpha,
        PdfBlendMode blendMode,
        PdfStrokeAdjustment? strokeAdjustment)
    {
        var dictionary = new PdfDictionary()
            .Set("ca", new PdfReal(fillAlpha))
            .Set("CA", new PdfReal(strokeAlpha))
            .SetName("BM", BlendModeName(blendMode));

        if (strokeAdjustment is PdfStrokeAdjustment.Enabled or PdfStrokeAdjustment.Disabled)
        {
            dictionary.Set("SA", PdfBoolean.From(strokeAdjustment == PdfStrokeAdjustment.Enabled));
        }

        return dictionary;
    }

    private static string Key(
        double fillAlpha,
        double strokeAlpha,
        PdfBlendMode blendMode,
        PdfStrokeAdjustment? strokeAdjustment) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{fillAlpha:R}|{strokeAlpha:R}|{blendMode}|{strokeAdjustment?.ToString() ?? "-"}");
}
