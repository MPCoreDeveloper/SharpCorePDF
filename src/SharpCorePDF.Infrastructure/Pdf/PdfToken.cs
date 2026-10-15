namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>The kinds of token a PDF file is made of.</summary>
internal enum PdfTokenType
{
    /// <summary>No token; the reader reached the end of the data.</summary>
    None,

    /// <summary>An integer or real number.</summary>
    Number,

    /// <summary>A name, <c>/Name</c>.</summary>
    Name,

    /// <summary>A literal string, <c>(text)</c>.</summary>
    LiteralString,

    /// <summary>A hexadecimal string, <c>&lt;74657874&gt;</c>.</summary>
    HexString,

    /// <summary>An opening bracket, <c>[</c>.</summary>
    ArrayOpen,

    /// <summary>A closing bracket, <c>]</c>.</summary>
    ArrayClose,

    /// <summary>An opening double angle bracket, <c>&lt;&lt;</c>.</summary>
    DictionaryOpen,

    /// <summary>A closing double angle bracket, <c>&gt;&gt;</c>.</summary>
    DictionaryClose,

    /// <summary>A bare keyword such as <c>obj</c>, <c>stream</c>, <c>true</c> or <c>R</c>.</summary>
    Keyword,
}

/// <summary>A token read from a PDF file.</summary>
internal readonly struct PdfToken
{
    /// <summary>Gets the kind of the token.</summary>
    public PdfTokenType Type { get; init; }

    /// <summary>Gets the value of an integer token.</summary>
    public long IntegerValue { get; init; }

    /// <summary>Gets the value of a real token.</summary>
    public double RealValue { get; init; }

    /// <summary>Gets a value indicating whether a number token was written without a fraction.</summary>
    public bool IsInteger { get; init; }

    /// <summary>Gets the decoded text of a name or keyword token.</summary>
    public string Text { get; init; }

    /// <summary>Gets the bytes of a string token.</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>Gets the offset at which the token starts.</summary>
    public int Start { get; init; }

    /// <summary>Gets the offset just behind the token.</summary>
    public int End { get; init; }

    /// <inheritdoc />
    public override string ToString() => Type switch
    {
        PdfTokenType.Number => IsInteger ? IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : PdfNumberFormat.Format(RealValue),
        PdfTokenType.Name => $"/{Text}",
        PdfTokenType.Keyword => Text,
        PdfTokenType.LiteralString => $"({Text})",
        PdfTokenType.HexString => "<hex>",
        _ => Type.ToString(),
    };
}
