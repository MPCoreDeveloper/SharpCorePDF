using SharpCorePDF.Domain.Exceptions;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Parses PDF objects into the object model.
/// </summary>
/// <remarks>
/// Indirect references are recognised by lookahead: <c>12 0 R</c> is a reference while <c>12 0</c>
/// followed by anything else is two numbers. A dictionary immediately followed by the
/// <c>stream</c> keyword becomes a stream, and the bytes of that stream are taken verbatim so the
/// engine never has to understand the compression of image, font or content data.
/// </remarks>
internal sealed partial class PdfObjectParser
{
    private readonly PdfLexer lexer;
    private readonly Func<long, long?>? indirectLengthResolver;
    private readonly int maxDepth;

    /// <summary>Initializes a new instance of the <see cref="PdfObjectParser"/> class.</summary>
    /// <param name="data">The bytes of the document.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="position">The offset to start parsing at.</param>
    /// <param name="indirectLengthResolver">Resolves a <c>/Length</c> that is an indirect reference.</param>
    /// <param name="maxDepth">The maximum nesting depth of containers.</param>
    public PdfObjectParser(
        byte[] data,
        string sourceName,
        int position = 0,
        Func<long, long?>? indirectLengthResolver = null,
        int maxDepth = 64)
    {
        lexer = new PdfLexer(data, position);
        this.indirectLengthResolver = indirectLengthResolver;
        this.maxDepth = maxDepth;
        SourceName = sourceName;
    }

    /// <summary>Gets the name of the source being parsed.</summary>
    public string SourceName { get; }

    /// <summary>Gets the underlying lexer.</summary>
    public PdfLexer Lexer => lexer;

    /// <summary>Gets or sets the current offset.</summary>
    public int Position
    {
        get => lexer.Position;
        set => lexer.Position = value;
    }

    /// <summary>Reads a keyword when it is next.</summary>
    /// <param name="keyword">The keyword to match.</param>
    /// <returns><see langword="true"/> when the keyword was read and consumed.</returns>
    public bool TryReadKeyword(string keyword) => lexer.TryReadKeyword(keyword);

    /// <summary>Reads an integer when one is next.</summary>
    /// <param name="value">The integer that was read.</param>
    /// <returns><see langword="true"/> when an integer was read.</returns>
    public bool TryReadInteger(out long value) => lexer.TryReadInteger(out value);

    /// <summary>Skips whitespace and comments.</summary>
    public void SkipWhitespaceAndComments() => lexer.SkipWhitespaceAndComments();

    /// <summary>Reads an indirect object header, <c>12 0 obj</c>.</summary>
    /// <param name="objectNumber">The object number that was read.</param>
    /// <param name="generation">The generation number that was read.</param>
    /// <returns><see langword="true"/> when a header was read and consumed.</returns>
    public bool TryReadIndirectHeader(out long objectNumber, out int generation)
    {
        objectNumber = 0;
        generation = 0;
        var position = Position;

        if (!lexer.TryReadInteger(out var number)
            || !lexer.TryReadInteger(out var gen)
            || !lexer.TryReadKeyword("obj"))
        {
            Position = position;
            return false;
        }

        objectNumber = number;
        generation = (int)gen;
        return true;
    }

    /// <summary>Parses one object.</summary>
    /// <param name="depth">The current nesting depth.</param>
    /// <returns>The parsed object; <see cref="PdfNull.Instance"/> when nothing valid was found.</returns>
    /// <exception cref="PdfSourceFormatException">Containers are nested beyond the configured limit.</exception>
    public PdfObject ParseObject(int depth = 0)
    {
        if (depth > maxDepth)
        {
            throw new PdfSourceFormatException(
                SourceName,
                $"objects are nested more than {maxDepth} levels deep, which suggests the file is damaged.");
        }

        var token = lexer.ReadToken();

        return token.Type switch
        {
            PdfTokenType.ArrayOpen => ParseArray(depth),
            PdfTokenType.DictionaryOpen => ParseDictionaryOrStream(depth),
            PdfTokenType.Number when token.IsInteger => ParseIntegerOrReference(token),
            PdfTokenType.Number => new PdfReal(token.RealValue),
            PdfTokenType.Name => new PdfName(token.Text),
            PdfTokenType.LiteralString => new PdfString(token.Bytes ?? [], false),
            PdfTokenType.HexString => new PdfString(token.Bytes ?? [], true),
            PdfTokenType.Keyword => ParseKeyword(token),
            _ => PdfNull.Instance,
        };
    }
}
