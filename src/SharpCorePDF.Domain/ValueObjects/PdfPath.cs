namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// An immutable sequence of path segments that starts with a <see cref="PdfPathSegment.MoveTo"/>.
/// </summary>
/// <remarks>
/// A path carries the geometry only. The paint and stroke that fill or outline it are separate values,
/// so the same path can be reused with different colours and line styles.
/// </remarks>
public sealed record PdfPath
{
    private readonly PdfPathSegment[] segments;

    /// <summary>Initializes a new instance of the <see cref="PdfPath"/> record.</summary>
    /// <param name="segments">The segments, in drawing order.</param>
    /// <param name="isClosed">Whether the path is closed after its last segment.</param>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> is empty, starts with anything other than a move, or contains a null segment.</exception>
    public PdfPath(IEnumerable<PdfPathSegment> segments, bool isClosed = false)
    {
        ArgumentNullException.ThrowIfNull(segments);

        this.segments = segments.ToArray();
        if (this.segments.Length == 0)
        {
            throw new ArgumentException("A path must contain at least one segment.", nameof(segments));
        }

        if (this.segments[0] is not PdfPathSegment.MoveTo)
        {
            throw new ArgumentException("A path must start with a MoveTo segment.", nameof(segments));
        }

        if (Array.IndexOf(this.segments, null) >= 0)
        {
            throw new ArgumentException("A path cannot contain a null segment.", nameof(segments));
        }

        IsClosed = isClosed;
    }

    /// <summary>Gets the segments in drawing order.</summary>
    public IReadOnlyList<PdfPathSegment> Segments => segments;

    /// <summary>Gets a value indicating whether the path is closed after its last segment.</summary>
    public bool IsClosed { get; }

    /// <summary>Formats the path for diagnostics.</summary>
    /// <returns>The segment count, for example <c>Path(5 segments, closed)</c>.</returns>
    public override string ToString() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Path({segments.Length} segments, {(IsClosed ? "closed" : "open")})");
}
