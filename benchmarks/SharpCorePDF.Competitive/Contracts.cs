using System.Reflection;

namespace SharpCorePDF.Competitive;

internal readonly record struct PageSelection(int FirstPage = 1, int? PageCount = null)
{
    public static PageSelection All { get; } = new(1, null);
}

internal readonly record struct Box(double Left, double Bottom, double Right, double Top)
{
    public double CentreX => (Left + Right) / 2d;
    public double CentreY => (Bottom + Top) / 2d;

    public bool IsWithin(Box other, double tolerance) =>
        Math.Abs(Left - other.Left) <= tolerance
        && Math.Abs(Bottom - other.Bottom) <= tolerance
        && Math.Abs(Right - other.Right) <= tolerance
        && Math.Abs(Top - other.Top) <= tolerance;

    /// <summary>
    /// The overlap of two boxes as a share of the area they cover together. A fair cross-convention
    /// measure: a glyph-ink box and a baseline box of the same character overlap heavily even though
    /// their edges differ, so this number separates "different convention" from "missed the character".
    /// </summary>
    public double IntersectionOverUnion(Box other)
    {
        var width = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        var height = Math.Min(Top, other.Top) - Math.Max(Bottom, other.Bottom);

        if (width <= 0d || height <= 0d)
        {
            return 0d;
        }

        var intersection = width * height;
        var mine = Math.Max(0d, (Right - Left) * (Top - Bottom));
        var theirs = Math.Max(0d, (other.Right - other.Left) * (other.Top - other.Bottom));
        var union = mine + theirs - intersection;

        return union <= 0d ? 0d : intersection / union;
    }
}

/// <summary>How a word read groups letters; names the alternate rows an adapter can offer.</summary>
internal static class WordStrategies
{
    public const string NearestNeighbour = "nearest-neighbour";
}

internal readonly record struct LetterObservation(int PageNumber, string Text, Box Bounds);
internal readonly record struct WordObservation(
    int PageNumber,
    string Text,
    Box Bounds,
    IReadOnlyList<LetterObservation> Letters);

internal sealed record ExtractionObservation(
    int PagesInspected,
    IReadOnlyList<WordObservation> Words,
    int WarningCount,
    int NoteCount = 0)
{
    public int LetterCount => Words.Sum(word => word.Letters.Count);

    public string WorkSummary() =>
        $"pages={PagesInspected}; words={Words.Count}; letters={LetterCount}; warnings={WarningCount}"
        + (NoteCount == 0 ? string.Empty : $"; notes={NoteCount}");
}

internal sealed record WriteObservation(long OutputBytes, int? PageCount)
{
    public string WorkSummary() =>
        $"pages={(PageCount?.ToString() ?? "not reported")}; outputBytes={OutputBytes}";
}

/// <summary>What a block read produced: how many blocks, and how many words inside them.</summary>
internal sealed record BlockObservation(int PagesInspected, int Blocks, int Words, int NoteCount = 0)
{
    public string WorkSummary() =>
        $"pages={PagesInspected}; blocks={Blocks}; words={Words}"
        + (NoteCount == 0 ? string.Empty : $"; notes={NoteCount}");
}

internal abstract class EngineAdapter
{
    protected EngineAdapter(string name, Assembly assembly)
    {
        Name = name;
        Version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
    }

    public string Name { get; }
    public string Version { get; }
    public virtual bool SupportsExtraction => false;
    public virtual bool SupportsCreation => false;
    public virtual bool SupportsMerge => false;

    /// <summary>Gets a value indicating whether this adapter can segment a page into blocks.</summary>
    public virtual bool SupportsBlocks => false;

    /// <summary>
    /// Reads the blocks of a document with the Docstrum segmentation, for the block-level comparison.
    /// </summary>
    /// <remarks>
    /// Both engines ship a Docstrum: SharpCorePDF's is a <c>PdfPageSegmentationStrategy</c> value, PdfPig's
    /// is a page segmenter that takes the words of a page. The row compares them as block producers on the
    /// same document — it is not a word-level row, so its work counts are the blocks and the words inside
    /// them, never a word count to compare with the extraction rows.
    /// </remarks>
    public virtual ValueTask<BlockObservation> ExtractBlocksAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{Name} has no block-segmentation adapter.");

    public virtual ValueTask<ExtractionObservation> ExtractAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{Name} has no text-extraction adapter.");

    /// <summary>
    /// Opens the document once, then extracts the selection — the warm path a reusable session serves.
    /// Defaults to <see cref="ExtractAsync"/> so an adapter without a session still participates.
    /// </summary>
    public virtual ValueTask<ExtractionObservation> ExtractWarmAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken) =>
        ExtractAsync(content, selection, cancellationToken);

    /// <summary>
    /// Answers two questions about the same selection — the letters, then the words — which is what a
    /// reusable session exists for. The default asks twice through <see cref="ExtractAsync"/>, so an
    /// adapter without a session still participates and the row still compares equal work.
    /// </summary>
    public virtual async ValueTask<ExtractionObservation> ExtractTwiceAsync(
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken)
    {
        _ = await ExtractAsync(content, selection, cancellationToken).ConfigureAwait(false);

        return await ExtractAsync(content, selection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The named alternate word-grouping strategies this adapter can offer as opt-in rows. The default
    /// extraction row is unchanged by these; an alternate row is a second measurement of the same work,
    /// never a substitute for the default apples-to-apples comparison.
    /// </summary>
    public virtual IReadOnlyList<string> AlternateExtractionStrategies => [];

    /// <summary>Extracts with a named alternate strategy; only called for entries of <see cref="AlternateExtractionStrategies"/>.</summary>
    public virtual ValueTask<ExtractionObservation> ExtractStrategyAsync(
        string strategy,
        byte[] content,
        PageSelection selection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{Name} offers no word strategy named '{strategy}'.");

    public virtual ValueTask<WriteObservation> CreateAsync(
        int pages,
        int linesPerPage,
        CancellationToken cancellationToken,
        string? capturePath = null) =>
        throw new NotSupportedException($"{Name} has no creation adapter.");

    public virtual ValueTask<WriteObservation> MergeAsync(
        IReadOnlyList<byte[]> sources,
        CancellationToken cancellationToken,
        string? capturePath = null) =>
        throw new NotSupportedException($"{Name} has no merge adapter.");
}

internal sealed class CountingStream : Stream
{
    private long length;

    public override long Length => length;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Position
    {
        get => Length;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => length += count;
    public override void Write(ReadOnlySpan<byte> buffer) => length += buffer.Length;
    public override void WriteByte(byte value) => length++;

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        length += count;
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        length += buffer.Length;
        return ValueTask.CompletedTask;
    }
}
