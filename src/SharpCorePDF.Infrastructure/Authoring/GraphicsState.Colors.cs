using SharpCorePDF.Domain.ValueObjects;
using PdfAffine = SharpCorePDF.Domain.ValueObjects.PdfMatrix;

namespace SharpCorePDF.Infrastructure.Authoring;

internal sealed partial class GraphicsState
{
    private void WriteColor(PdfPaint paint, bool fill)
    {
        var (red, green, blue) = (paint.Color.Red / 255.0, paint.Color.Green / 255.0, paint.Color.Blue / 255.0);

        switch (paint.ColorSpace)
        {
            case PdfColorSpace.DeviceGray:
                builder.WriteNumberOperator(fill ? "g" : "G", (0.299 * red) + (0.587 * green) + (0.114 * blue));
                break;

            case PdfColorSpace.DeviceRGB:
                builder.WriteNumber(red);
                builder.Write(" ");
                builder.WriteNumber(green);
                builder.Write(" ");
                builder.WriteNumber(blue);
                builder.Write(fill ? " rg\n" : " RG\n");
                break;

            case PdfColorSpace.DeviceCMYK:
                WriteCmyk(red, green, blue, fill ? "k" : "K");
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(paint),
                    paint.ColorSpace,
                    "The colour space is not one the engine writes.");
        }
    }

    private void WriteCmyk(double red, double green, double blue, string operatorName)
    {
        var black = 1.0 - Math.Max(red, Math.Max(green, blue));
        var denominator = 1.0 - black;
        var cyan = denominator <= 0.0 ? 0.0 : (1.0 - red - black) / denominator;
        var magenta = denominator <= 0.0 ? 0.0 : (1.0 - green - black) / denominator;
        var yellow = denominator <= 0.0 ? 0.0 : (1.0 - blue - black) / denominator;

        builder.WriteNumber(Math.Clamp(cyan, 0.0, 1.0));
        builder.Write(" ");
        builder.WriteNumber(Math.Clamp(magenta, 0.0, 1.0));
        builder.Write(" ");
        builder.WriteNumber(Math.Clamp(yellow, 0.0, 1.0));
        builder.Write(" ");
        builder.WriteNumber(Math.Clamp(black, 0.0, 1.0));
        builder.Write(" ");
        builder.WriteOperator(operatorName);
    }

    /// <summary>Selects a device colour space and writes <c>cs</c> or <c>CS</c>.</summary>
    /// <param name="colorSpace">The colour space.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour space is selected.</param>
    public void SetColorSpace(PdfColorSpace colorSpace, bool fill = true)
    {
        if (!Enum.IsDefined(colorSpace))
        {
            throw new ArgumentOutOfRangeException(nameof(colorSpace), colorSpace, "The colour space is not one the engine writes.");
        }

        builder.WriteNameOperator(fill ? "cs" : "CS", ColorSpaceName(colorSpace));
    }

    /// <summary>Sets the colour components in the selected colour space and writes <c>sc</c> or <c>SC</c>.</summary>
    /// <param name="components">One, three or four finite components.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour is set.</param>
    public void SetColor(IReadOnlyList<double> components, bool fill = true)
    {
        ValidateComponents(components, allowEmpty: false);
        WriteComponents(components);
        builder.Write(" ");
        builder.WriteOperator(fill ? "sc" : "SC");
    }

    /// <summary>Sets a colour and a pattern or spot name and writes <c>scn</c> or <c>SCN</c>.</summary>
    /// <param name="name">The resource name of the pattern or separation.</param>
    /// <param name="components">The colour components, or an empty list for an uncoloured pattern.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour is set.</param>
    public void SetColor(string name, IReadOnlyList<double> components, bool fill = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A colour name must not be blank.", nameof(name));
        }

        ValidateComponents(components, allowEmpty: true);

        if (components.Count > 0)
        {
            WriteComponents(components);
            builder.Write(" ");
        }

        builder.Write("/");
        builder.Write(name);
        builder.Write(" ");
        builder.WriteOperator(fill ? "scn" : "SCN");
    }

    /// <summary>Selects a colour space a caller names and writes <c>cs</c> or <c>CS</c>.</summary>
    /// <param name="name">The name, with or without its leading slash.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour space is selected.</param>
    /// <remarks>
    /// The name is written as the caller states it, so it must already be a legal PDF name: a resource name
    /// such as <c>Cs1</c>, or one of the device names <c>DeviceGray</c>, <c>DeviceRGB</c> or <c>DeviceCMYK</c>.
    /// A name that needs <c>#</c> escaping is refused rather than escaped silently, because the caller is the
    /// only one that knows what the name refers to.
    /// </remarks>
    /// <exception cref="ArgumentException">The name is blank or is not a legal PDF name.</exception>
    public void SetColorSpaceName(string name, bool fill = true) =>
        builder.WriteNameOperator(fill ? "cs" : "CS", LegalColorSpaceName(name));

    /// <summary>Returns the name a colour-space selection may carry, or refuses it.</summary>
    /// <param name="name">The name, with or without its leading slash.</param>
    /// <returns>The name without its leading slash.</returns>
    /// <remarks>
    /// The name is written as the caller states it, so it must already be a legal PDF name: a resource name
    /// such as <c>Cs1</c>, or one of the device names <c>DeviceGray</c>, <c>DeviceRGB</c> or <c>DeviceCMYK</c>.
    /// A name that needs <c>#</c> escaping is refused rather than escaped silently, because the caller is the
    /// only one that knows what the name refers to.
    /// </remarks>
    /// <exception cref="ArgumentException">The name is blank or is not a legal PDF name.</exception>
    internal static string LegalColorSpaceName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name[0] == '/' ? name[1..] : name;

        ArgumentException.ThrowIfNullOrWhiteSpace(trimmed);

        var illegal = trimmed.FirstOrDefault(character =>
            char.IsWhiteSpace(character)
            || char.IsControl(character)
            || character is '#' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%');

        if (illegal != '\0')
        {
            throw new ArgumentException(
                $"The colour space name '{name}' is not a legal PDF name: it contains '{illegal}', which a name may not state without escaping.",
                nameof(name));
        }

        return trimmed;
    }

    /// <summary>Sets the colour components in the selected colour space and writes <c>scn</c> or <c>SCN</c>.</summary>
    /// <param name="components">The components the selected space states, at least one, all finite.</param>
    /// <param name="fill">Whether the non-stroking or the stroking colour is set.</param>
    /// <remarks>
    /// The operator is <c>scn</c> rather than <c>sc</c> because a named space may be a separation or a
    /// device-N space, which <c>scn</c> states and <c>sc</c> does not. The count is the space's own — one for a
    /// gray or a separation, three for RGB, four for CMYK, more for device-N — so it is not validated here.
    /// </remarks>
    /// <exception cref="ArgumentException">No component is given or one is not finite.</exception>
    public void SetColorComponents(IReadOnlyList<double> components, bool fill = true)
    {
        ArgumentNullException.ThrowIfNull(components);

        ValidateColorComponents(components);

        WriteComponents(components);
        builder.Write(" ");
        builder.WriteOperator(fill ? "scn" : "SCN");
    }

    /// <summary>Refuses a colour whose components no space could state.</summary>
    /// <param name="components">The components.</param>
    /// <exception cref="ArgumentException">No component is given or one is not finite.</exception>
    internal static void ValidateColorComponents(IReadOnlyList<double> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (components.Count == 0)
        {
            throw new ArgumentException("A colour in a selected space states at least one component.", nameof(components));
        }

        if (components.Any(component => !double.IsFinite(component)))
        {
            throw new ArgumentException("A colour component must be finite.", nameof(components));
        }
    }

    private static string ColorSpaceName(PdfColorSpace colorSpace) => colorSpace switch
    {
        PdfColorSpace.DeviceGray => "DeviceGray",
        PdfColorSpace.DeviceRGB => "DeviceRGB",
        PdfColorSpace.DeviceCMYK => "DeviceCMYK",
        _ => throw new ArgumentOutOfRangeException(nameof(colorSpace), colorSpace, "The colour space is not one the engine writes."),
    };

    private static void ValidateComponents(IReadOnlyList<double> components, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (!allowEmpty && components.Count is not (1 or 3 or 4))
        {
            throw new ArgumentException("A device colour needs one, three or four components.", nameof(components));
        }

        for (var index = 0; index < components.Count; index++)
        {
            if (!double.IsFinite(components[index]))
            {
                throw new ArgumentException("Every colour component must be finite.", nameof(components));
            }
        }
    }

    private void WriteComponents(IReadOnlyList<double> components)
    {
        for (var index = 0; index < components.Count; index++)
        {
            if (index > 0)
            {
                builder.Write(" ");
            }

            builder.WriteNumber(components[index]);
        }
    }

    private static PdfGraphicsState ReplaceTransform(PdfGraphicsState state, PdfAffine matrix) =>
        new(state.Transform.Multiply(matrix), state.Fill, state.Stroke, state.Alpha, state.BlendMode, state.ClipPath, state.ClipFillRule);

    private static PdfGraphicsState ReplaceFill(PdfGraphicsState state, PdfPaint paint) =>
        new(state.Transform, paint, state.Stroke, state.Alpha, state.BlendMode, state.ClipPath, state.ClipFillRule);

    private static PdfGraphicsState ReplaceStroke(PdfGraphicsState state, PdfStroke stroke) =>
        new(state.Transform, state.Fill, stroke, state.Alpha, state.BlendMode, state.ClipPath, state.ClipFillRule);

    private static PdfGraphicsState ReplaceAlpha(PdfGraphicsState state, double alpha, PdfBlendMode blendMode) =>
        new(state.Transform, state.Fill, state.Stroke, alpha, blendMode, state.ClipPath, state.ClipFillRule);

    private static PdfGraphicsState ReplaceClip(PdfGraphicsState state, PdfPath path, PdfFillRule fillRule) =>
        new(state.Transform, state.Fill, state.Stroke, state.Alpha, state.BlendMode, path, fillRule);
}
