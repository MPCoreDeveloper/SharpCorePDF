namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// An identity for a <see cref="Entities.PdfSource"/> inside a <see cref="Aggregates.MergePlan"/>.
/// </summary>
/// <remarks>
/// Two sources may read from the same file, so identity can never be derived from a path or a
/// stream: a plan keyed by location would silently collapse such duplicates.
/// </remarks>
public readonly record struct PdfSourceId
{
    /// <summary>Initializes a new instance of the <see cref="PdfSourceId"/> struct.</summary>
    /// <param name="value">A non-empty identifier.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty or whitespace.</exception>
    public PdfSourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the identifier value.</summary>
    public string Value { get; }

    /// <summary>Creates a new, unique identifier.</summary>
    /// <returns>A new identifier.</returns>
    public static PdfSourceId New() => new(Guid.NewGuid().ToString("n"));

    /// <summary>Formats the identifier.</summary>
    /// <returns>The identifier value.</returns>
    public override string ToString() => Value;
}
