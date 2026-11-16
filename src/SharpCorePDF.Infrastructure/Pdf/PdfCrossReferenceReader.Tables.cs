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
    private (long? Previous, long? Hybrid, HashSet<long>? Freed) ReadClassicTable(int offset, bool isNewest)
    {
        var parser = new PdfObjectParser(data, sourceName, offset, null, options.MaxNestingDepth);
        var freed = new HashSet<long>();
        parser.TryReadKeyword("xref");
        ReadSubsections(parser, freed);

        if (!parser.TryReadKeyword("trailer") || parser.ParseObject() is not PdfDictionary trailer)
        {
            return (null, null, null);
        }

        var previous = trailer.GetInteger("Prev");
        var hybrid = trailer.GetInteger("XRefStm");
        table.MergeTrailer(trailer, onlyMissingKeys: !isNewest);

        // The numbers this table frees are the ones a hybrid section's stream may describe: a hybrid file lists
        // the objects it keeps in object streams as free here, so a reader that knows no streams skips them.
        return (previous, hybrid, hybrid is null ? null : freed);
    }

    /// <summary>Reads the subsections of the table, which are as many as the file wrote.</summary>
    /// <param name="parser">The parser positioned at the first subsection.</param>
    /// <param name="freed">The set that receives the numbers the table lists as free.</param>
    private void ReadSubsections(PdfObjectParser parser, HashSet<long> freed)
    {
        while (true)
        {
            var position = parser.Position;

            if (!TryReadSubsectionHeader(parser, out var first, out var count))
            {
                parser.Position = position;
                return;
            }

            if (!ReadSubsectionEntries(parser, first, count, freed))
            {
                return;
            }
        }
    }

    /// <summary>Reads the first object number and the length of a subsection.</summary>
    /// <param name="parser">The parser to read from.</param>
    /// <param name="first">The object number the subsection starts at.</param>
    /// <param name="count">The number of entries the subsection announces.</param>
    /// <returns><see langword="true"/> when the subsection announced a usable number of entries.</returns>
    private static bool TryReadSubsectionHeader(PdfObjectParser parser, out long first, out int count)
    {
        count = 0;

        if (!parser.TryReadInteger(out first) || !parser.TryReadInteger(out var length))
        {
            return false;
        }

        if (length is < 0 or > 50_000_000)
        {
            return false;
        }

        count = (int)length;
        return true;
    }

    /// <summary>Reads the entries of one subsection.</summary>
    /// <param name="parser">The parser to read from.</param>
    /// <param name="first">The object number the subsection starts at.</param>
    /// <param name="count">The number of entries the subsection announces.</param>
    /// <param name="freed">The set that receives the numbers the table lists as free.</param>
    /// <returns><see langword="true"/> when every announced entry was read.</returns>
    private bool ReadSubsectionEntries(PdfObjectParser parser, long first, int count, HashSet<long> freed)
    {
        for (var index = 0; index < count; index++)
        {
            if (!TryReadTableEntry(parser, out var entryOffset, out var generation, out var inUse))
            {
                // A promise of ten entries followed by eight entries is a truncated table, not a table that
                // continues somewhere else, so the tail is dropped instead of being read as data.
                return false;
            }

            table.Add(new XrefEntry(
                first + index,
                entryOffset,
                generation,
                inUse ? XrefEntryType.Uncompressed : XrefEntryType.Free));

            if (!inUse)
            {
                freed.Add(first + index);
            }
        }

        return true;
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
