using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Annotations;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Forms;

/// <summary>
/// Builds the appearance of a form field: the small form XObject a viewer draws inside the widget
/// rectangle, and the operators a flattened page draws instead.
/// </summary>
/// <remarks>
/// <para>
/// The stream is written in the field's own space: the lower-left corner of the box is the origin, and
/// the box is mapped onto the widget rectangle by whoever draws it. One builder serves both routes — an
/// interactive widget names the stream as its <c>/AP</c>, and a flattened page draws the same operators
/// where the widget stood — so a value looks the same before and after a flatten.
/// </para>
/// <para>
/// A field that states an appearance of its own keeps it: the content the caller supplied is written
/// instead of the one this class would draw, and only the box and the font are taken from it. The
/// standard-font restriction of <see cref="PdfStandardTextStyle"/> is what makes that safe: an
/// appearance never carries a font program.
/// </para>
/// </remarks>
internal static class FieldAppearance
{
    /// <summary>The resource name the font of a field appearance is selected by.</summary>
    public const string FontName = "Helv";

    /// <summary>The distance between the border of a field and the text inside it.</summary>
    private const double Padding = 2.0;

    /// <summary>The width the marks of a check box and a radio button are stroked with.</summary>
    private const double MarkWidth = 1.5;

    /// <summary>The colour of a border a field does not state a colour for.</summary>
    private static PdfColor DefaultBorderColor { get; } = new(0x33, 0x33, 0x33);

    /// <summary>The colour a selected list item is highlighted with.</summary>
    private static PdfColor SelectionColor { get; } = new(0xCC, 0xDD, 0xF5);

    /// <summary>Gets the box the appearance is drawn in, in appearance space.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The box: the caller's, or the size of the widget rectangle at the origin.</returns>
    public static PdfRectangle BoxOf(PdfFormField field) => field.Appearance?.BoundingBox
        ?? new PdfRectangle(0, 0, Math.Max(field.Rectangle.Width, 1.0), Math.Max(field.Rectangle.Height, 1.0));

    /// <summary>States whether a field draws text, which decides whether it needs a font resource.</summary>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> for the kinds that carry text.</returns>
    public static bool DrawsText(PdfFormField field) => field.Appearance?.Font is not null
        || field.Kind is PdfFormFieldKind.Text or PdfFormFieldKind.Choice or PdfFormFieldKind.PushButton;

    /// <summary>Builds the appearance stream of a field.</summary>
    /// <param name="field">The field.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="on">Whether a check box or a radio button is on; ignored by the other kinds.</param>
    /// <param name="fontObjectNumber">The object number of the font the stream names, or zero for one that draws no text.</param>
    /// <returns>The stream to write.</returns>
    public static PdfStream Build(PdfFormField field, PdfStandardTextStyle style, bool on, int fontObjectNumber)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(style);

        var box = BoxOf(field);
        var operators = field.Appearance is { Content.Length: > 0 } supplied
            ? Encoding.ASCII.GetBytes(supplied.Content)
            : Draw(field, style, on, box);

        return Wrap(operators, fontObjectNumber, box);
    }

    /// <summary>Wraps appearance operators in the form XObject dictionary that carries them.</summary>
    /// <param name="operators">The operators, uncompressed.</param>
    /// <param name="fontObjectNumber">The font the appearance names, or zero.</param>
    /// <param name="box">The box the operators are drawn in.</param>
    /// <returns>The stream to write.</returns>
    public static PdfStream Wrap(byte[] operators, int fontObjectNumber, PdfRectangle box)
    {
        var dictionary = new PdfDictionary()
            .SetName("Type", "XObject")
            .SetName("Subtype", "Form")
            .Set("BBox", AppearanceStreams.ToArray(box))
            .SetName("Filter", "FlateDecode");

        if (fontObjectNumber != 0)
        {
            dictionary.Set(
                "Resources",
                new PdfDictionary().Set("Font", new PdfDictionary().Set(FontName, new PdfReference(fontObjectNumber, 0))));
        }

        return new PdfStream(dictionary, Authoring.EmbeddedImage.Compress(operators));
    }

    /// <summary>Draws the operators of one appearance.</summary>
    /// <param name="field">The field.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="on">Whether a button is on.</param>
    /// <param name="box">The box of the appearance.</param>
    /// <returns>The operators, uncompressed.</returns>
    private static byte[] Draw(PdfFormField field, PdfStandardTextStyle style, bool on, PdfRectangle box)
    {
        var content = new AppearanceContent();
        content.Line("q");
        DrawBackground(content, field, box);

        switch (field.Kind)
        {
            case PdfFormFieldKind.Text:
                DrawFrame(content, field, box);
                DrawValue(content, field, style, box);
                break;

            case PdfFormFieldKind.Choice:
                DrawFrame(content, field, box);

                if (field.IsCombo)
                {
                    DrawValue(content, field, style, box);
                }
                else
                {
                    DrawList(content, field, style, box);
                }

                break;

            case PdfFormFieldKind.CheckBox:
                DrawCheckBox(content, field, box, on);
                break;

            case PdfFormFieldKind.RadioButton:
                DrawRadioButton(content, field, box, on);
                break;

            case PdfFormFieldKind.PushButton:
                DrawPushButton(content, field, style, box);
                break;

            default:
                break;
        }

        content.Line("Q");
        return content.ToBytes();
    }

    /// <summary>Fills the area behind the value, when the field states a colour for it.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="box">The box of the appearance.</param>
    private static void DrawBackground(AppearanceContent content, PdfFormField field, PdfRectangle box)
    {
        if (field.FillColor is not { } fill)
        {
            return;
        }

        content.Rgb(fill.Red, fill.Green, fill.Blue).Line(" rg")
            .Number(0).Number(0).Number(box.Width).Number(box.Height).Line(" re f");
    }

    /// <summary>Strokes the border of a field, unless it states a width of zero.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="box">The box of the appearance.</param>
    private static void DrawFrame(AppearanceContent content, PdfFormField field, PdfRectangle box)
    {
        if (field.BorderWidth <= 0.0)
        {
            return;
        }

        var border = field.BorderColor ?? DefaultBorderColor;
        var half = field.BorderWidth / 2.0;

        content.Rgb(border.Red, border.Green, border.Blue).Line(" RG")
            .Number(field.BorderWidth).Line(" w");

        // A border wider than the box would be drawn outside it, which the viewer clips away; the
        // frame is simply left out instead.
        if (box.Width > field.BorderWidth && box.Height > field.BorderWidth)
        {
            content
                .Number(half).Number(half)
                .Number(box.Width - field.BorderWidth).Number(box.Height - field.BorderWidth)
                .Line(" re S");
        }
    }

    /// <summary>Draws the value of a text field or a combo box, clipped to its box.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="box">The box of the appearance.</param>
    private static void DrawValue(AppearanceContent content, PdfFormField field, PdfStandardTextStyle style, PdfRectangle box)
    {
        var value = field.Value;

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        content.Number(0).Number(0).Number(box.Width).Number(box.Height).Line(" re W n");

        // A password field is drawn masked: the appearance is what a reader shows, and writing the
        // characters into it would undo the reason the field is masked in the first place.
        DrawWrapped(content, field, style, box, field.IsPassword ? new string('*', value.Length) : value);
    }

    /// <summary>Draws a list box: its options, with the selection highlighted.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="style">The style the options are drawn in.</param>
    /// <param name="box">The box of the appearance.</param>
    private static void DrawList(AppearanceContent content, PdfFormField field, PdfStandardTextStyle style, PdfRectangle box)
    {
        content.Number(0).Number(0).Number(box.Width).Number(box.Height).Line(" re W n");

        var typeface = PdfTypeface.Standard(style.Font);
        var leading = style.Size * 1.2;
        var y = box.Height - Padding - (style.Size * 0.8);

        foreach (var option in field.Options)
        {
            if (y < Padding)
            {
                break;
            }

            if (string.Equals(option, field.Value, StringComparison.Ordinal))
            {
                content.Rgb(SelectionColor.Red, SelectionColor.Green, SelectionColor.Blue).Line(" rg")
                    .Number(Padding).Number(y - (style.Size * 0.25))
                    .Number(Math.Max(box.Width - (2.0 * Padding), 1.0)).Number(leading)
                    .Line(" re f");
            }

            DrawLine(content, typeface, style, style.Size, option, box, PdfFormFieldAlignment.Left, Padding, y);
            y -= leading;
        }
    }

    /// <summary>Draws a value as one line or as several wrapped lines.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="style">The style the value is drawn in.</param>
    /// <param name="box">The box of the appearance.</param>
    /// <param name="value">The text to draw.</param>
    private static void DrawWrapped(
        AppearanceContent content,
        PdfFormField field,
        PdfStandardTextStyle style,
        PdfRectangle box,
        string value)
    {
        var typeface = PdfTypeface.Standard(style.Font);
        var available = Math.Max(box.Width - (2.0 * Padding), 1.0);
        var size = FittingSize(typeface, style.Size, value, available, field.IsMultiline);
        var lines = field.IsMultiline ? WrapLines(typeface, value, size, available) : [value];
        var leading = size * 1.2;
        var baseline = box.Height - Padding - (size * 0.8);

        foreach (var line in lines)
        {
            if (baseline < Padding)
            {
                break;
            }

            DrawLine(content, typeface, style, size, line, box, field.Alignment, Padding, baseline);
            baseline -= leading;
        }
    }

    /// <summary>Draws one line of a value at the alignment and baseline it is given.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="typeface">The face the line is measured with.</param>
    /// <param name="style">The style the line is drawn in.</param>
    /// <param name="size">The size the line is drawn at.</param>
    /// <param name="line">The text of the line.</param>
    /// <param name="box">The box of the appearance.</param>
    /// <param name="alignment">The horizontal alignment of the line.</param>
    /// <param name="inset">The distance from the border to the text.</param>
    /// <param name="baseline">The baseline the line is set on.</param>
    private static void DrawLine(
        AppearanceContent content,
        PdfTypeface typeface,
        PdfStandardTextStyle style,
        double size,
        string line,
        PdfRectangle box,
        PdfFormFieldAlignment alignment,
        double inset,
        double baseline)
    {
        var width = typeface.Measure(line, size);
        var room = Math.Max(box.Width - (2.0 * inset), 0.0);
        var x = alignment switch
        {
            PdfFormFieldAlignment.Center => inset + Math.Max((room - width) / 2.0, 0.0),
            PdfFormFieldAlignment.Right => inset + Math.Max(room - width, 0.0),
            _ => inset,
        };

        content.Rgb(style.Color.Red, style.Color.Green, style.Color.Blue).Line(" rg")
            .Name(FontName).Number(size).Line(" Tf")
            .Line("BT")
            .Number(1).Number(0).Number(0).Number(1).Number(x).Number(baseline).Line(" Tm")
            .Literal(line).Line(" Tj")
            .Line("ET");
    }

    /// <summary>Gets the size a value is drawn at, shrunk when one line does not fit.</summary>
    /// <param name="typeface">The face the value is measured with.</param>
    /// <param name="size">The size the field states.</param>
    /// <param name="value">The value.</param>
    /// <param name="available">The width the value has to fit in.</param>
    /// <param name="multiline">Whether the value may be wrapped.</param>
    /// <returns>The size to draw at.</returns>
    /// <remarks>
    /// A field that wraps keeps the size the caller stated — wrapping is what makes it fit — while a
    /// single-line field shrinks, because a clipped value is a value the reader cannot use.
    /// </remarks>
    private static double FittingSize(PdfTypeface typeface, double size, string value, double available, bool multiline)
    {
        if (multiline)
        {
            return size;
        }

        var width = typeface.Measure(value, size);

        return width > available && width > 0.0
            ? Math.Max(1.0, size * available / width)
            : size;
    }

    /// <summary>Breaks a value into the lines that fit the width, at the spaces.</summary>
    /// <param name="typeface">The face the value is measured with.</param>
    /// <param name="value">The value.</param>
    /// <param name="size">The size the value is drawn at.</param>
    /// <param name="available">The width a line has to fit in.</param>
    /// <returns>The lines, in the order they are drawn.</returns>
    private static List<string> WrapLines(PdfTypeface typeface, string value, double size, double available)
    {
        var lines = new List<string>();

        foreach (var paragraph in value.Split('\n'))
        {
            var line = string.Empty;

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";

                if (line.Length > 0 && typeface.Measure(candidate, size) > available)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Draws a check box: its frame, and a tick when it is on.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="box">The box of the appearance.</param>
    /// <param name="on">Whether the box is ticked.</param>
    private static void DrawCheckBox(AppearanceContent content, PdfFormField field, PdfRectangle box, bool on)
    {
        DrawFrame(content, field, box);

        if (!on)
        {
            return;
        }

        var border = field.BorderColor ?? DefaultBorderColor;
        var width = box.Width;
        var height = box.Height;

        // The tick is two rules rather than a filled polygon, so it survives a print in grey and a
        // scale down to a thumbnail.
        content.Rgb(border.Red, border.Green, border.Blue).Line(" RG")
            .Number(MarkWidth).Line(" w")
            .Number(width * 0.22).Number(height * 0.55)
            .Number(width * 0.42).Number(height * 0.28).Line(" m l")
            .Number(width * 0.80).Number(height * 0.78).Line(" l S");
    }

    /// <summary>Draws a radio button: its circle, and a filled dot when it is on.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="box">The box of the appearance.</param>
    /// <param name="on">Whether the button is on.</param>
    private static void DrawRadioButton(AppearanceContent content, PdfFormField field, PdfRectangle box, bool on)
    {
        var radius = Math.Min(box.Width, box.Height) / 2.0;
        var centreX = box.Width / 2.0;
        var centreY = box.Height / 2.0;
        var border = field.BorderColor ?? DefaultBorderColor;

        Circle(content, centreX, centreY, radius);
        content.Rgb(border.Red, border.Green, border.Blue).Line(" RG")
            .Number(field.BorderWidth > 0.0 ? field.BorderWidth : 1.0).Line(" w S");

        if (!on)
        {
            return;
        }

        Circle(content, centreX, centreY, radius * 0.45);
        content.Rgb(border.Red, border.Green, border.Blue).Line(" rg f");
    }

    /// <summary>Appends the four curves of a circle to the path.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="centreX">The centre on the x axis.</param>
    /// <param name="centreY">The centre on the y axis.</param>
    /// <param name="radius">The radius.</param>
    private static void Circle(AppearanceContent content, double centreX, double centreY, double radius)
    {
        // The control points of a quarter circle, as the Bézier approximation of a circle states them.
        const double Kappa = 0.5522847498;
        var offset = radius * Kappa;

        content
            .Number(centreX + radius).Number(centreY).Line(" m")
            .Number(centreX + radius).Number(centreY + offset)
            .Number(centreX + offset).Number(centreY + radius)
            .Number(centreX).Number(centreY + radius).Line(" c")
            .Number(centreX - offset).Number(centreY + radius)
            .Number(centreX - radius).Number(centreY + offset)
            .Number(centreX - radius).Number(centreY).Line(" c")
            .Number(centreX - radius).Number(centreY - offset)
            .Number(centreX - offset).Number(centreY - radius)
            .Number(centreX).Number(centreY - radius).Line(" c")
            .Number(centreX + offset).Number(centreY - radius)
            .Number(centreX + radius).Number(centreY - offset)
            .Number(centreX + radius).Number(centreY).Line(" c");
    }

    /// <summary>Draws a push button: its frame and the caption it carries.</summary>
    /// <param name="content">The stream being built.</param>
    /// <param name="field">The field.</param>
    /// <param name="style">The style the caption is drawn in.</param>
    /// <param name="box">The box of the appearance.</param>
    private static void DrawPushButton(AppearanceContent content, PdfFormField field, PdfStandardTextStyle style, PdfRectangle box)
    {
        DrawFrame(content, field, box);

        var caption = field.Caption;

        if (string.IsNullOrEmpty(caption))
        {
            return;
        }

        var typeface = PdfTypeface.Standard(style.Font);
        var available = Math.Max(box.Width - (2.0 * Padding), 1.0);
        var size = FittingSize(typeface, style.Size, caption, available, multiline: false);
        var baseline = (box.Height - (size * 0.7)) / 2.0;

        content.Number(0).Number(0).Number(box.Width).Number(box.Height).Line(" re W n");
        DrawLine(content, typeface, style, size, caption, box, PdfFormFieldAlignment.Center, Padding, baseline);
    }

    /// <summary>Formats a field for a diagnostic, which is how a warning names it.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The kind and the name.</returns>
    public static string Describe(PdfFormField field) => string.Create(
        CultureInfo.InvariantCulture,
        $"the {field.Kind} field '{field.Name}'");
}
