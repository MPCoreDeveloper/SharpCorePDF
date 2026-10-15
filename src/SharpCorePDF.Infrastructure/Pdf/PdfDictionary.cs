namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// A PDF dictionary, keyed by decoded name.
/// </summary>
/// <remarks>
/// Insertion order is preserved, which keeps a merged document readable when its objects are
/// inspected by hand and keeps the output stable between runs.
/// </remarks>
internal sealed class PdfDictionary : PdfObject
{
    private readonly List<string> order = [];
    private readonly Dictionary<string, PdfObject> entries = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="PdfDictionary"/> class.</summary>
    public PdfDictionary()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfDictionary"/> class.</summary>
    /// <param name="entries">The entries to copy, in order.</param>
    public PdfDictionary(IEnumerable<KeyValuePair<string, PdfObject>> entries)
    {
        foreach (var entry in entries)
        {
            Set(entry.Key, entry.Value);
        }
    }

    /// <summary>Gets the keys in insertion order.</summary>
    public IReadOnlyList<string> Keys => order;

    /// <summary>Gets the number of entries.</summary>
    public int Count => order.Count;

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Dictionary;

    /// <summary>Gets every entry in insertion order.</summary>
    public IEnumerable<KeyValuePair<string, PdfObject>> Entries
    {
        get
        {
            foreach (var key in order)
            {
                yield return new KeyValuePair<string, PdfObject>(key, entries[key]);
            }
        }
    }

    /// <summary>Sets an entry.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <param name="value">The value; <see langword="null"/> removes the entry.</param>
    /// <returns>This dictionary, so calls can be chained.</returns>
    public PdfDictionary Set(string key, PdfObject? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (value is null)
        {
            Remove(key);
            return this;
        }

        if (!entries.ContainsKey(key))
        {
            order.Add(key);
        }

        entries[key] = value;
        return this;
    }

    /// <summary>Sets an entry to an integer.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>This dictionary, so calls can be chained.</returns>
    public PdfDictionary SetInteger(string key, long value) => Set(key, new PdfInteger(value));

    /// <summary>Sets an entry to a name, removing the entry when the name is null or empty.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <param name="value">The name value.</param>
    /// <returns>This dictionary, so calls can be chained.</returns>
    public PdfDictionary SetName(string key, string? value) => Set(key, PdfName.From(value));

    /// <summary>Removes an entry.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns><see langword="true"/> when the entry existed.</returns>
    public bool Remove(string key)
    {
        if (!entries.Remove(key))
        {
            return false;
        }

        order.Remove(key);
        return true;
    }

    /// <summary>Determines whether the dictionary carries an entry.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool ContainsKey(string key) => entries.ContainsKey(key);

    /// <summary>Gets a value without resolving an indirect reference.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public PdfObject? Get(string key) => entries.GetValueOrDefault(key);

    /// <summary>Gets an entry as a reference.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The reference, or <see langword="null"/>.</returns>
    public PdfReference? GetReference(string key) => Get(key) as PdfReference;

    /// <summary>Gets an entry as a dictionary.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public PdfDictionary? GetDictionary(string key) => Get(key) as PdfDictionary;

    /// <summary>Gets an entry as an array.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The array, or <see langword="null"/>.</returns>
    public PdfArray? GetArray(string key) => Get(key) as PdfArray;

    /// <summary>Gets an entry as a decoded name.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The name value, or <see langword="null"/>.</returns>
    public string? GetName(string key) => (Get(key) as PdfName)?.Value;

    /// <summary>Gets an entry as an integer.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public long? GetInteger(string key) => Get(key) switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real when real.Value == Math.Floor(real.Value) => (long)real.Value,
        _ => null,
    };

    /// <summary>Gets an entry as a number.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public double? GetNumber(string key) => Get(key) switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => null,
    };

    /// <summary>Gets an entry as decoded text.</summary>
    /// <param name="key">The decoded name of the entry.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public string? GetText(string key) => (Get(key) as PdfString)?.ToText();

    /// <inheritdoc />
    public override string ToString() =>
        $"<< {string.Join(" ", Entries.Select(entry => $"/{entry.Key} {entry.Value}"))} >>";
}
