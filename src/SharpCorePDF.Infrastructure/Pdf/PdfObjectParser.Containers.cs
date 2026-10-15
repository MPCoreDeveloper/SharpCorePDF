namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The array and dictionary half of <see cref="PdfObjectParser"/>.
/// </summary>
internal sealed partial class PdfObjectParser
{
    private PdfArray ParseArray(int depth)
    {
        var array = new PdfArray();

        while (true)
        {
            var token = lexer.ReadToken();

            if (token.Type is PdfTokenType.ArrayClose or PdfTokenType.None or PdfTokenType.DictionaryClose)
            {
                return array;
            }

            Position = token.Start;
            array.Items.Add(ParseObject(depth + 1));
        }
    }

    private PdfObject ParseDictionaryOrStream(int depth)
    {
        var dictionary = new PdfDictionary();

        while (true)
        {
            var token = lexer.ReadToken();

            if (token.Type is PdfTokenType.DictionaryClose or PdfTokenType.None)
            {
                break;
            }

            if (token.Type != PdfTokenType.Name)
            {
                // A dictionary key that is not a name cannot be interpreted at all; skipping it keeps
                // the rest of the dictionary usable.
                continue;
            }

            dictionary.Set(token.Text, ParseObject(depth + 1));
        }

        var position = Position;

        if (lexer.TryReadKeyword("stream"))
        {
            return ReadStream(dictionary);
        }

        Position = position;
        return dictionary;
    }

    private PdfObject ParseIntegerOrReference(PdfToken token)
    {
        var position = Position;
        var second = lexer.ReadToken();

        if (second.Type == PdfTokenType.Number && second.IsInteger)
        {
            var third = lexer.ReadToken();

            if (third.Type == PdfTokenType.Keyword && third.Text == "R")
            {
                return new PdfReference(token.IntegerValue, (int)second.IntegerValue);
            }
        }

        Position = position;
        return new PdfInteger(token.IntegerValue);
    }

    private static PdfObject ParseKeyword(PdfToken token) => token.Text switch
    {
        "true" => PdfBoolean.True,
        "false" => PdfBoolean.False,
        _ => PdfNull.Instance,
    };
}
