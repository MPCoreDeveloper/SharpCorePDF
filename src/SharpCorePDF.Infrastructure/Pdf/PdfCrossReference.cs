namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>The kind of entry a cross-reference table holds.</summary>
internal enum XrefEntryType
{
    /// <summary>The object number is free.</summary>
    Free = 0,

    /// <summary>The object is stored at a byte offset in the file.</summary>
    Uncompressed = 1,

    /// <summary>The object is stored inside an object stream.</summary>
    Compressed = 2,
}

/// <summary>One cross-reference entry.</summary>
/// <param name="ObjectNumber">The object number.</param>
/// <param name="Offset">The byte offset of the object, or the object number of its object stream.</param>
/// <param name="Generation">The generation number, or the index inside the object stream.</param>
/// <param name="Type">The kind of entry.</param>
internal readonly record struct XrefEntry(long ObjectNumber, long Offset, int Generation, XrefEntryType Type)
{
    /// <summary>Gets a value indicating whether the entry refers to an object that exists.</summary>
    public bool IsInUse => Type != XrefEntryType.Free;
}

/// <summary>
/// The merged cross-reference table of a document plus its trailer.
/// </summary>
/// <remarks>
/// Entries are added newest revision first and never overwritten, which is exactly how incremental
/// updates are meant to be resolved.
/// </remarks>
internal sealed class PdfCrossReferenceTable
{
    /// <summary>Gets the entries by object number.</summary>
    public Dictionary<long, XrefEntry> Entries { get; } = [];

    /// <summary>Gets the trailer of the newest revision.</summary>
    public PdfDictionary Trailer { get; } = new();

    /// <summary>Gets the highest object number seen, or zero.</summary>
    public long HighestObjectNumber { get; private set; }

    /// <summary>Gets the offset the newest <c>startxref</c> pointed at, or zero.</summary>
    public long StartXrefOffset { get; internal set; }

    /// <summary>Gets a value indicating whether the table was rebuilt by scanning the file.</summary>
    public bool WasReconstructed { get; internal set; }

    /// <summary>Gets the number of entries.</summary>
    public int Count => Entries.Count;

    /// <summary>Adds an entry, keeping an entry that is already present.</summary>
    /// <param name="entry">The entry to add.</param>
    public void Add(XrefEntry entry)
    {
        if (entry.ObjectNumber < 0)
        {
            return;
        }

        if (Entries.TryAdd(entry.ObjectNumber, entry))
        {
            Track(entry.ObjectNumber);
        }
    }

    /// <summary>Adds or replaces an entry. Later revisions and later scan hits win.</summary>
    /// <param name="entry">The entry to set.</param>
    public void AddOrReplace(XrefEntry entry)
    {
        if (entry.ObjectNumber < 0)
        {
            return;
        }

        Entries[entry.ObjectNumber] = entry;
        Track(entry.ObjectNumber);
    }

    /// <summary>Gets an entry.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public XrefEntry? Get(long objectNumber) =>
        Entries.TryGetValue(objectNumber, out var entry) ? entry : null;

    /// <summary>Copies the keys of a trailer dictionary that are not present yet.</summary>
    /// <param name="trailer">The trailer to merge.</param>
    /// <param name="onlyMissingKeys">When <see langword="true"/>, existing keys are preserved.</param>
    public void MergeTrailer(PdfDictionary trailer, bool onlyMissingKeys)
    {
        foreach (var entry in trailer.Entries)
        {
            if (entry.Key is "Prev" or "XRefStm" or "Length" or "Filter" or "DecodeParms" or "Type" or "W" or "Index")
            {
                continue;
            }

            if (onlyMissingKeys && Trailer.ContainsKey(entry.Key))
            {
                continue;
            }

            Trailer.Set(entry.Key, entry.Value);
        }
    }

    private void Track(long objectNumber)
    {
        if (objectNumber > HighestObjectNumber)
        {
            HighestObjectNumber = objectNumber;
        }
    }
}
