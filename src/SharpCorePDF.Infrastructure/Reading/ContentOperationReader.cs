using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// Reads the operations of a page's decoded content stream: one entry per operator, with the operands the
/// stream gives it and the offset it starts at.
/// </summary>
/// <remarks>
/// <para>
/// The stream is tokenized rather than interpreted, so an operator this library implements nothing for is
/// reported with its operands exactly like one it does: an audit of a document has to see what the
/// document says (FR-7.15). Inline images are one operation, <c>BI</c>, whose operands are the entries of
/// its header; its samples are skipped rather than reported, because they are not operators and the image
/// read is where an inline image is described.
/// </para>
/// <para>
/// Nesting is bounded by <see cref="PdfEngineOptions.MaxNestingDepth"/>: a stream that writes deeper
/// containers stops being descended at that depth and says so through
/// <see cref="PdfWarningKind.ContentNestingTooDeep"/>, so a hostile document cannot turn an inspection into
/// an unbounded walk.
/// </para>
/// </remarks>
internal static class ContentOperationReader
{
    /// <summary>Reads the operations of a decoded content stream.</summary>
    /// <param name="content">The decoded bytes of the page's content streams.</param>
    /// <param name="pageNumber">The one-based number of the page, carried into every operation.</param>
    /// <param name="options">The engine options; only the nesting limit is used.</param>
    /// <param name="warnings">The list that receives the remark about a stream that nests too deeply.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The operations of the stream, in the order they are written.</returns>
    public static IReadOnlyList<PdfContentOperationView> Read(
        byte[] content,
        int pageNumber,
        PdfEngineOptions options,
        List<PdfWarning>? warnings,
        string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        return new Scanner(content, pageNumber, options.MaxNestingDepth, warnings, sourceName).Run();
    }

    /// <summary>Walks one content stream and collects the operations it states.</summary>
    private sealed class Scanner
    {
        private readonly byte[] content;
        private readonly int pageNumber;
        private readonly int maxDepth;
        private readonly List<PdfWarning>? warnings;
        private readonly string? sourceName;
        private readonly PdfLexer lexer;
        private readonly List<PdfContentOperationView> operations = [];
        private readonly List<PdfObjectView> operands = [];
        private bool notedDepth;

        public Scanner(
            byte[] content,
            int pageNumber,
            int maxDepth,
            List<PdfWarning>? warnings,
            string? sourceName)
        {
            this.content = content;
            this.pageNumber = pageNumber;
            this.maxDepth = maxDepth;
            this.warnings = warnings;
            this.sourceName = sourceName;
            lexer = new PdfLexer(content);
        }

        public List<PdfContentOperationView> Run()
        {
            while (true)
            {
                var token = lexer.ReadToken();

                if (token.Type == PdfTokenType.None)
                {
                    return operations;
                }

                if (IsOperator(token))
                {
                    if (string.Equals(token.Text, "BI", StringComparison.Ordinal))
                    {
                        ReadInlineHeader();
                    }

                    operations.Add(new PdfContentOperationView
                    {
                        Operator = token.Text,
                        PageNumber = pageNumber,
                        Offset = token.Start,
                        Operands = [.. operands],
                    });

                    operands.Clear();
                    continue;
                }

                var operand = ReadOperand(token, 0);

                if (operand is not null)
                {
                    operands.Add(operand);
                }
            }
        }

        private PdfObjectView? ReadOperand(PdfToken token, int depth) => token.Type switch
        {
            PdfTokenType.Number => token.IsInteger
                ? new PdfObjectView { Kind = PdfObjectViewKind.Integer, Scalar = token.IntegerValue }
                : new PdfObjectView { Kind = PdfObjectViewKind.Real, Scalar = token.RealValue },
            PdfTokenType.Name => new PdfObjectView { Kind = PdfObjectViewKind.Name, Scalar = token.Text },
            PdfTokenType.LiteralString => StringView(token, isHexadecimal: false),
            PdfTokenType.HexString => StringView(token, isHexadecimal: true),
            PdfTokenType.ArrayOpen => ReadArray(depth + 1),
            PdfTokenType.DictionaryOpen => ReadDictionary(depth + 1),
            PdfTokenType.Keyword when token.Text is "true" or "false" =>
                new PdfObjectView { Kind = PdfObjectViewKind.Boolean, Scalar = string.Equals(token.Text, "true", StringComparison.Ordinal) },
            PdfTokenType.Keyword when string.Equals(token.Text, "null", StringComparison.Ordinal) =>
                new PdfObjectView { Kind = PdfObjectViewKind.Null },

            // A stray closer, an operator in the middle of operands, or the end of the stream: none of them
            // is a value, so the caller is left to decide what they mean.
            _ => null,
        };

        /// <summary>Reads the elements of an array operand up to its closer.</summary>
        /// <param name="depth">The nesting depth of the array itself.</param>
        private PdfObjectView ReadArray(int depth)
        {
            if (depth > maxDepth)
            {
                NoteDepth();
                return new PdfObjectView { Kind = PdfObjectViewKind.Array };
            }

            var items = new List<PdfObjectView>();

            while (true)
            {
                var token = lexer.ReadToken();

                if (token.Type is PdfTokenType.None or PdfTokenType.ArrayClose)
                {
                    return new PdfObjectView { Kind = PdfObjectViewKind.Array, Children = [.. items] };
                }

                // An operator where an element should be is a stream that ended its array badly. The array
                // stops there and the token is read again as the operator it is, so the rest of the page is
                // still reported.
                if (IsOperator(token))
                {
                    lexer.Position = token.Start;
                    return new PdfObjectView { Kind = PdfObjectViewKind.Array, Children = [.. items] };
                }

                var item = ReadOperand(token, depth);

                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        /// <summary>Reads the entries of a dictionary operand up to its closer.</summary>
        /// <param name="depth">The nesting depth of the dictionary itself.</param>
        private PdfObjectView ReadDictionary(int depth)
        {
            if (depth > maxDepth)
            {
                NoteDepth();
                return new PdfObjectView { Kind = PdfObjectViewKind.Dictionary };
            }

            var entries = new List<PdfObjectView>();

            while (true)
            {
                var name = lexer.ReadToken();

                if (name.Type is PdfTokenType.None or PdfTokenType.DictionaryClose)
                {
                    return new PdfObjectView { Kind = PdfObjectViewKind.Dictionary, Children = [.. entries] };
                }

                if (name.Type != PdfTokenType.Name)
                {
                    if (IsOperator(name))
                    {
                        lexer.Position = name.Start;
                    }

                    return new PdfObjectView { Kind = PdfObjectViewKind.Dictionary, Children = [.. entries] };
                }

                var token = lexer.ReadToken();

                if (token.Type == PdfTokenType.None)
                {
                    return new PdfObjectView { Kind = PdfObjectViewKind.Dictionary, Children = [.. entries] };
                }

                if (IsOperator(token))
                {
                    lexer.Position = token.Start;
                    return new PdfObjectView { Kind = PdfObjectViewKind.Dictionary, Children = [.. entries] };
                }

                var value = ReadOperand(token, depth);

                if (value is not null)
                {
                    entries.Add(value with { Key = name.Text });
                }
            }
        }

        /// <summary>Reads the header entries of an inline image as the operands of its <c>BI</c>.</summary>
        private void ReadInlineHeader()
        {
            while (true)
            {
                var token = lexer.ReadToken();

                if (token.Type == PdfTokenType.None)
                {
                    return;
                }

                if (IsOperator(token))
                {
                    if (string.Equals(token.Text, "ID", StringComparison.Ordinal))
                    {
                        SkipInlineData();
                        return;
                    }

                    // A header that never reaches ID is a broken inline image. The token is read again as an
                    // operator of its own and the BI keeps the entries it managed to collect.
                    lexer.Position = token.Start;
                    return;
                }

                var operand = ReadOperand(token, 0);

                if (operand is not null)
                {
                    operands.Add(operand);
                }
            }
        }

        /// <summary>
        /// Moves past the samples of an inline image to the <c>EI</c> that closes it.
        /// </summary>
        /// <remarks>
        /// The samples are binary, so they are found by looking for EI on its own rather than by reading
        /// tokens; a stream that never writes one leaves the scanner at its end.
        /// </remarks>
        private void SkipInlineData()
        {
            var index = lexer.Position;

            // Exactly one separator follows ID, and it belongs to the header rather than to the samples.
            if (index < content.Length && PdfLexer.IsWhitespace(content[index]))
            {
                index++;
            }

            var start = index;

            for (var candidate = index; candidate + 1 < content.Length; candidate++)
            {
                if (content[candidate] != (byte)'E' || content[candidate + 1] != (byte)'I')
                {
                    continue;
                }

                if (candidate > start && !PdfLexer.IsWhitespaceOrDelimiter(content[candidate - 1]))
                {
                    continue;
                }

                var after = candidate + 2;

                if (after < content.Length && !PdfLexer.IsWhitespaceOrDelimiter(content[after]))
                {
                    continue;
                }

                lexer.Position = after;
                return;
            }

            lexer.Position = content.Length;
        }

        private void NoteDepth()
        {
            if (notedDepth)
            {
                return;
            }

            notedDepth = true;
            warnings?.Add(new PdfWarning(
                PdfWarningKind.ContentNestingTooDeep,
                $"'{sourceName}': an operand in the content stream of page {pageNumber} nests deeper than {maxDepth} levels, so the read stops descending there."));
        }

        private static PdfObjectView StringView(PdfToken token, bool isHexadecimal) =>
            new()
            {
                Kind = PdfObjectViewKind.String,
                Scalar = new PdfString(token.Bytes ?? [], isHexadecimal).ToText(),
            };

        /// <summary>Tells an operator from the keywords that are values.</summary>
        private static bool IsOperator(PdfToken token) =>
            token.Type == PdfTokenType.Keyword && token.Text is not ("true" or "false" or "null");
    }
}
