namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>The eight PDF object types plus the indirect reference.</summary>
internal enum PdfObjectKind
{
    /// <summary>The null object.</summary>
    Null,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>An integer or real number.</summary>
    Number,

    /// <summary>A string, literal or hexadecimal.</summary>
    String,

    /// <summary>A name.</summary>
    Name,

    /// <summary>An array.</summary>
    Array,

    /// <summary>A dictionary.</summary>
    Dictionary,

    /// <summary>A stream: a dictionary followed by raw bytes.</summary>
    Stream,

    /// <summary>An indirect reference to another object.</summary>
    Reference,
}

/// <summary>
/// Base class of the PDF object model. Objects in a PDF file are either scalars, containers or
/// indirect references; the engine parses files into this model, moves objects between documents in
/// this model, and serialises this model back to bytes.
/// </summary>
internal abstract class PdfObject // NOSONAR S1694: a closed hierarchy of file values with state, not a capability
{
    /// <summary>Gets the kind of this object.</summary>
    public abstract PdfObjectKind Kind { get; }
}

/// <summary>The PDF null object.</summary>
internal sealed class PdfNull : PdfObject
{
    private PdfNull()
    {
    }

    /// <summary>Gets the single instance.</summary>
    public static PdfNull Instance { get; } = new();

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Null;

    /// <inheritdoc />
    public override string ToString() => "null";
}

/// <summary>A PDF boolean.</summary>
internal sealed class PdfBoolean : PdfObject
{
    private PdfBoolean(bool value) => Value = value;

    /// <summary>Gets the true value.</summary>
    public static PdfBoolean True { get; } = new(true);

    /// <summary>Gets the false value.</summary>
    public static PdfBoolean False { get; } = new(false);

    /// <summary>Gets the underlying value.</summary>
    public bool Value { get; }

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Boolean;

    /// <summary>Returns the shared instance for <paramref name="value"/>.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>The matching instance.</returns>
    public static PdfBoolean From(bool value) => value ? True : False;

    /// <inheritdoc />
    public override string ToString() => Value ? "true" : "false";
}

/// <summary>A PDF integer number.</summary>
internal sealed class PdfInteger : PdfObject
{
    /// <summary>Initializes a new instance of the <see cref="PdfInteger"/> class.</summary>
    /// <param name="value">The value.</param>
    public PdfInteger(long value) => Value = value;

    /// <summary>Gets the value.</summary>
    public long Value { get; }

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Number;

    /// <inheritdoc />
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A PDF real number.</summary>
internal sealed class PdfReal : PdfObject
{
    /// <summary>Initializes a new instance of the <see cref="PdfReal"/> class.</summary>
    /// <param name="value">The value.</param>
    public PdfReal(double value) => Value = value;

    /// <summary>Gets the value.</summary>
    public double Value { get; }

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Number;

    /// <inheritdoc />
    public override string ToString() => PdfNumberFormat.Format(Value);
}

/// <summary>Formats numbers the way PDF expects them: invariant, shortest round-trippable form.</summary>
internal static class PdfNumberFormat
{
    /// <summary>Formats a real number.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value, for example <c>-3.25</c> or <c>0.5</c>.</returns>
    public static string Format(double value)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        if (IsWholeNumber(value))
        {
            return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var text = value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

        if (text[0] == '.')
        {
            return string.Concat("0", text);
        }

        return text.StartsWith("-.", System.StringComparison.Ordinal)
            ? string.Concat("-0", text[1..])
            : text;
    }

    /// <summary>Determines whether a value is a whole number that a <see cref="long"/> holds exactly.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value has no fractional part and fits into a long.</returns>
    /// <remarks>
    /// The test is exact on purpose: a value that arrived as <c>3.0</c> is exactly three, and rounding it
    /// would write a number the producer did not write. Values at or above 10^15 are written as reals
    /// instead, because a double has no digits left to spare above that point.
    /// </remarks>
    public static bool IsWholeNumber(double value) => double.IsInteger(value) && Math.Abs(value) < 1e15;

    /// <summary>
    /// Formats a real number into bytes, without materialising a string. A document holds thousands of
    /// numbers, and a string per number is a string per number thrown away.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="destination">The buffer to format into.</param>
    /// <param name="bytesWritten">The number of bytes written when formatting succeeded.</param>
    /// <returns><see langword="true"/> when the value fitted into <paramref name="destination"/>.</returns>
    public static bool TryFormat(double value, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;

        if (!double.IsFinite(value))
        {
            if (destination.IsEmpty)
            {
                return false;
            }

            destination[0] = (byte)'0';
            bytesWritten = 1;
            return true;
        }

        if (IsWholeNumber(value))
        {
            return ((long)value).TryFormat(destination, out bytesWritten, default, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!value.TryFormat(destination, out bytesWritten, "0.#####", System.Globalization.CultureInfo.InvariantCulture))
        {
            return false;
        }

        // PDF has no exponent form and wants a digit before the decimal point: "0.5", never ".5".
        if (destination[0] == (byte)'.')
        {
            destination[..bytesWritten].CopyTo(destination[1..]);
            destination[0] = (byte)'0';
            bytesWritten++;
        }
        else if (bytesWritten > 1 && destination[0] == (byte)'-' && destination[1] == (byte)'.')
        {
            destination[1..bytesWritten].CopyTo(destination[2..]);
            destination[1] = (byte)'0';
            bytesWritten++;
        }

        return true;
    }
}
