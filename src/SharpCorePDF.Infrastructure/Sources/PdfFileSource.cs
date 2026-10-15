using SharpCorePDF.Domain.Ports;

namespace SharpCorePDF.Infrastructure.Sources;

/// <summary>
/// A PDF document stored in a file.
/// </summary>
/// <remarks>
/// Every <see cref="OpenReadAsync"/> opens the file again, so a source can be inspected and merged,
/// or merged twice, without the two operations interfering with each other. The file is opened with
/// <see cref="FileShare.Read"/>, so other readers are not locked out.
/// </remarks>
public sealed class PdfFileSource : IPdfContentSource
{
    /// <summary>Initializes a new instance of the <see cref="PdfFileSource"/> class.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="displayName">The name used in messages; defaults to the file name.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or whitespace.</exception>
    public PdfFileSource(string path, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = System.IO.Path.GetFullPath(path);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? System.IO.Path.GetFileName(Path) : displayName;
    }

    /// <summary>Gets the full path of the file.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public long? Length
    {
        get
        {
            var info = new FileInfo(Path);
            return info.Exists ? info.Length : null;
        }
    }

    /// <summary>Creates a source for a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="displayName">The name used in messages; defaults to the file name.</param>
    /// <returns>The created source.</returns>
    public static PdfFileSource From(string path, string? displayName = null) => new(path, displayName);

    /// <inheritdoc />
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stream stream = new FileStream(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        return new ValueTask<Stream>(stream);
    }

    /// <summary>Formats the source as its display name followed by its path.</summary>
    /// <returns>The formatted source.</returns>
    public override string ToString() => $"{DisplayName} ({Path})";
}
