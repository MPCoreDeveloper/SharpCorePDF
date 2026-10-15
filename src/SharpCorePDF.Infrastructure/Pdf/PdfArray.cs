namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>A PDF array.</summary>
internal sealed class PdfArray : PdfObject
{
    /// <summary>Initializes a new instance of the <see cref="PdfArray"/> class.</summary>
    public PdfArray()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfArray"/> class.</summary>
    /// <param name="items">The items of the array.</param>
    public PdfArray(IEnumerable<PdfObject> items) => Items.AddRange(items);

    /// <summary>Gets the items of the array.</summary>
    public List<PdfObject> Items { get; } = [];

    /// <summary>Gets the number of items.</summary>
    public int Count => Items.Count;

    /// <inheritdoc />
    public override PdfObjectKind Kind => PdfObjectKind.Array;

    /// <summary>Appends an item.</summary>
    /// <param name="item">The item to append.</param>
    /// <returns>This array, so calls can be chained.</returns>
    public PdfArray Add(PdfObject item)
    {
        Items.Add(item);
        return this;
    }

    /// <inheritdoc />
    public override string ToString() => $"[{string.Join(" ", Items)}]";
}
