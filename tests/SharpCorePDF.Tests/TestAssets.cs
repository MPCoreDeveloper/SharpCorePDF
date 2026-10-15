namespace SharpCorePDF.Tests;

/// <summary>
/// The PDF fixtures that ship next to the test assembly, and the temporary files tests may write to.
/// </summary>
internal static class TestAssets
{
    /// <summary>Gets the folder that holds the fixtures.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>Gets a real-world 6 page document written by "Microsoft: Print To PDF".</summary>
    public static string SafeWebCore => Path.Combine(Folder, "SafeWebCore.pdf");

    /// <summary>Gets a 2 page document that uses object streams and a cross-reference stream.</summary>
    public static string Modern => Path.Combine(Folder, "Modern.pdf");

    /// <summary>Gets a 3 page document with bookmarks, page labels, an annotation and a rotated page.</summary>
    public static string Outlined => Path.Combine(Folder, "Outlined.pdf");

    /// <summary>Gets a 1 page document that is encrypted with a user password.</summary>
    public static string Encrypted => Path.Combine(Folder, "Encrypted.pdf");

    /// <summary>Gets every fixture.</summary>
    public static IReadOnlyList<string> All => [SafeWebCore, Modern, Outlined];

    /// <summary>Creates a path in a fresh folder that is removed by the returned scope.</summary>
    /// <param name="name">The file name to use inside the folder.</param>
    /// <returns>A scope that deletes the folder when it is disposed.</returns>
    public static TemporaryFolder NewTemporaryFolder(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "SharpCorePDF.Tests", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TemporaryFolder(path);
    }

    /// <summary>A folder that deletes itself.</summary>
    internal sealed class TemporaryFolder(string path) : IDisposable
    {
        /// <summary>Gets the path of the folder.</summary>
        public string Path { get; } = path;

        /// <summary>Returns the path of a file inside the folder.</summary>
        /// <param name="fileName">The name of the file.</param>
        /// <returns>The full path of the file.</returns>
        public string File(string fileName) => System.IO.Path.Combine(Path, fileName);

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A test that leaves a lock behind must not fail because of the cleanup.
            }
        }
    }
}
