using System.Diagnostics;
using System.Security.Cryptography;

namespace SharpCorePDF.Competitive;

internal sealed record RobustnessOptions(
    string Folder,
    int Limit,
    long MaximumFileBytes,
    TimeSpan Timeout,
    string? LockManifest = null,
    bool WriteLockManifest = false);
internal sealed record RobustnessResult(
    string Engine,
    string File,
    string Sha256,
    long FileBytes,
    string Status,
    double ElapsedMilliseconds,
    int Pages,
    int Words,
    int Letters,
    int Warnings,
    int Notes,
    string? Error);

internal static class RobustnessRunner
{
    /// <summary>The one serialization shape a manifest is written with, cached once.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    public static async Task<IReadOnlyList<RobustnessResult>> RunAsync(
        IReadOnlyList<EngineAdapter> adapters,
        RobustnessOptions options,
        CancellationToken cancellationToken)
    {
        var results = new List<RobustnessResult>();
        var files = Directory.EnumerateFiles(options.Folder, "*.pdf", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Take(options.Limit)
            .ToArray();

        // A hash lock pins the corpus the run claims to measure: the manifest names each file and its
        // SHA-256, a mismatch stops that file's row before any engine opens it, and the manifest itself
        // is never committed — it belongs beside the local corpus, exactly as the corpus does.
        SortedDictionary<string, string>? manifest = null;
        if (options.LockManifest is { } manifestPath)
        {
            if (options.WriteLockManifest)
            {
                manifest = await WriteManifestAsync(options.Folder, files, manifestPath, cancellationToken);
            }
            else if (File.Exists(manifestPath))
            {
                manifest = await ReadManifestAsync(manifestPath, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException(
                    $"The robustness lock '{manifestPath}' does not exist. " +
                    "Pass --write-robustness-lock once after reviewing the corpus, locally only.");
            }
        }

        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(options.Folder, path).Replace('\\', '/');
            var info = new FileInfo(path);
            if (info.Length > options.MaximumFileBytes)
            {
                results.AddRange(adapters.Where(adapter => adapter.SupportsExtraction).Select(adapter =>
                    new RobustnessResult(
                        adapter.Name, relative, string.Empty, info.Length, "Skipped", 0, 0, 0, 0, 0, 0,
                        $"Exceeds the {options.MaximumFileBytes}-byte per-file limit.")));
                continue;
            }

            var content = await File.ReadAllBytesAsync(path, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

            if (manifest is not null
                && (!manifest.TryGetValue(relative, out var pinned) || !string.Equals(pinned, hash, StringComparison.Ordinal)))
            {
                results.AddRange(adapters.Where(adapter => adapter.SupportsExtraction).Select(adapter =>
                    new RobustnessResult(
                        adapter.Name, relative, hash, content.LongLength, "LockMismatch", 0, 0, 0, 0, 0, 0,
                        "The file does not match the SHA-256 manifest; no engine opened it.")));
                continue;
            }

            foreach (var adapter in adapters.Where(adapter => adapter.SupportsExtraction))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(options.Timeout);
                var started = Stopwatch.GetTimestamp();
                var operation = Task.Run(() => adapter.ExtractAsync(
                    content,
                    PageSelection.All,
                    timeout.Token).AsTask(),
                    CancellationToken.None);
                try
                {
                    var observed = await operation.WaitAsync(options.Timeout, cancellationToken);
                    results.Add(new RobustnessResult(
                        adapter.Name,
                        relative,
                        hash,
                        content.LongLength,
                        "Completed",
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        observed.PagesInspected,
                        observed.Words.Count,
                        observed.LetterCount,
                        observed.WarningCount,
                        observed.NoteCount,
                        null));
                }
                catch (TimeoutException)
                {
                    timeout.Cancel();
                    results.Add(new RobustnessResult(
                        adapter.Name, relative, hash, content.LongLength, "Timeout",
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0, 0, 0, 0, 0,
                        $"Exceeded {options.Timeout.TotalSeconds:0.###} seconds; the runner stops here so no abandoned work continues into the next document."));
                    return results;
                }
                catch (Exception exception)
                {
                    results.Add(new RobustnessResult(
                        adapter.Name, relative, hash, content.LongLength, "Error",
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0, 0, 0, 0, 0,
                        exception.GetType().Name + ": " + exception.Message));
                }
            }
        }

        return results;
    }

    /// <summary>Reads a hash-lock manifest: one relative path and one SHA-256 hex digest per entry.</summary>
    private static async Task<SortedDictionary<string, string>> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var manifest = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken));

        foreach (var entry in json.RootElement.EnumerateObject())
        {
            manifest[entry.Name] = entry.Value.GetString() ?? string.Empty;
        }

        return manifest;
    }

    /// <summary>Writes the manifest for the corpus and returns it, so the same run is verified against it.</summary>
    /// <remarks>
    /// Writing the lock is a review step the operator performs after checking the corpus locally; it never
    /// downloads anything and the manifest lives wherever the operator pointed --robustness-lock at. Every
    /// later run against the same manifest proves it measured exactly these bytes.
    /// </remarks>
    private static async Task<SortedDictionary<string, string>> WriteManifestAsync(
        string folder,
        IReadOnlyList<string> files,
        string path,
        CancellationToken cancellationToken)
    {
        var manifest = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            var content = await File.ReadAllBytesAsync(file, cancellationToken);
            manifest[relative] = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(manifest, ManifestJsonOptions),
            cancellationToken);
        Console.WriteLine($"Wrote robustness lock with {manifest.Count} entries to {path}");

        return manifest;
    }
}
