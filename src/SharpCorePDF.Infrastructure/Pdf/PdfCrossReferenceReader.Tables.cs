namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The classic cross-reference table reader.
/// </summary>
/// <remarks>
/// Entries are read as tokens rather than as fixed width columns: the specification prescribes twenty
/// bytes per entry, but files exist whose entries are separated differently, and tokenising copes
/// with both.
/// </remarks>
internal sealed partial class PdfCrossReferenceReader
{
    private (long? Previous, long? Hybrid) ReadClassicTable(int offset, bool isNewest)
    {
        var parser = new PdfObjectParser(data, sourceName, offset, null, options.MaxNestingDepth);
        parser.TryReadKeyword("xref");

        while (true)
        {
            var position = parser.Position;

            if (!parser.TryReadInteger(out var first)
                || !parser.TryReadInteger(out var count)
                || count is < 0 or > 50_000_000)
            {
                parser.Position = position;
                break;
            }

            var complete = true;

            for (var index = 0; index < count; index++)
            {
                if (!TryReadTableEntry(parser, out var entryOffset, out var generation, out var inUse))
                {
                    complete = false;
                    break;
                }

                table.Add(new XrefEntry(
                    first + index,
                    entryOffset,
                    generation,
                    inUse ? XrefEntryType.Uncompressed : XrefEntryType.Free));
            }

            if (!complete)
            {
                break;
            }
        }

        if (!parser.TryReadKeyword("trailer") || parser.ParseObject() is not PdfDictionary trailer)
        {
            return (null, null);
        }

        var previous = trailer.GetInteger("Prev");
        var hybrid = trailer.GetInteger("XRefStm");
        table.MergeTrailer(trailer, onlyMissingKeys: !isNewest);

        return (previous, hybrid);
    }

    private static bool TryReadTableEntry(PdfObjectParser parser, out long offset, out int generation, out bool inUse)
    {
        offset = 0;
        generation = 0;
        inUse = false;
        var position = parser.Position;

        if (!parser.TryReadInteger(out offset) || !parser.TryReadInteger(out var generationValue))
        {
            parser.Position = position;
            return false;
        }

        var token = parser.Lexer.ReadToken();

        if (token.Type != PdfTokenType.Keyword || (token.Text != "n" && token.Text != "f"))
        {
            parser.Position = position;
            return false;
        }

        inUse = token.Text == "n";
        generation = (int)generationValue;
        return true;
    }
}
