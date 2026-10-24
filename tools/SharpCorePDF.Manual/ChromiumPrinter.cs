using System.Diagnostics;
using System.Text;

namespace SharpCorePDF.Manual;

/// <summary>
/// Prints one HTML document to PDF with a headless Chromium browser.
/// </summary>
/// <remarks>
/// Chromium is the only external process in the pipeline, and it is used for what a browser is good at:
/// page boxes, pagination, tables that break across pages, font fallback and colour management. The
/// flags match the ones the Python builder used, so the geometry of the output is the same.
/// </remarks>
internal sealed class ChromiumPrinter
{
    private const int PrintTimeoutMinutes = 5;

    private readonly string buildDirectory;
    private readonly string? browser;

    /// <summary>Initializes a new instance of the <see cref="ChromiumPrinter"/> class.</summary>
    /// <param name="buildDirectory">Where the intermediate HTML and PDF are written.</param>
    /// <param name="browser">The browser to use, or null to find an installed one.</param>
    public ChromiumPrinter(string buildDirectory, string? browser = null)
    {
        this.buildDirectory = buildDirectory;
        this.browser = browser ?? FindBrowser();
    }

    /// <summary>Gets a value indicating whether a browser was found.</summary>
    public bool IsAvailable => browser is not null;

    /// <summary>Gets the path of the browser this printer uses, when one was found.</summary>
    public string? BrowserPath => browser;

    /// <summary>Prints one HTML document to PDF.</summary>
    /// <param name="html">The complete HTML document.</param>
    /// <param name="fileName">The name of the PDF inside the build directory.</param>
    /// <param name="keepHtml">Whether the intermediate HTML is kept after printing.</param>
    /// <param name="cancellationToken">The token the print observes.</param>
    /// <returns>The bytes of the printed PDF.</returns>
    public async Task<byte[]> RenderAsync(
        string html,
        string fileName,
        bool keepHtml,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (browser is null)
        {
            throw new InvalidOperationException(
                "no Chrome or Edge found: the PDF is printed by a browser engine. Install one, put it on "
                + "PATH, or point SHARPCOREPDF_CHROME at the executable.");
        }

        Directory.CreateDirectory(buildDirectory);

        var pagePath = Path.Combine(buildDirectory, "manual.html");
        await File.WriteAllTextAsync(
                pagePath,
                html,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);

        var pdfPath = Path.Combine(buildDirectory, fileName);
        if (File.Exists(pdfPath))
        {
            File.Delete(pdfPath);
        }

        var profile = Path.Combine(
            Path.GetTempPath(),
            "sharpcorepdf-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);

        try
        {
            var arguments = new List<string>
            {
                "--headless=new",
                "--disable-gpu",
                "--no-sandbox",
                "--no-first-run",
                "--disable-extensions",
                "--no-pdf-header-footer",
                "--print-to-pdf-no-header",
                "--run-all-compositor-stages-before-draw",
                "--virtual-time-budget=20000",
                "--user-data-dir=" + profile,
                "--print-to-pdf=" + pdfPath,
                new Uri(Path.GetFullPath(pagePath), UriKind.Absolute).AbsoluteUri,
            };

            var startInfo = new ProcessStartInfo(browser)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("the browser did not start.");

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(PrintTimeoutMinutes));

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw new TimeoutException(
                    $"the browser did not finish printing within {PrintTimeoutMinutes} minutes.");
            }

            var stdout = await output.ConfigureAwait(false);
            var stderr = await error.ConfigureAwait(false);

            if (!File.Exists(pdfPath))
            {
                throw new InvalidOperationException(
                    $"the browser did not produce a PDF.\n{stdout}\n{stderr}");
            }

            return await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(profile);

            if (!keepHtml)
            {
                TryDeleteFile(pagePath);
            }
        }
    }

    private static string? FindBrowser()
    {
        var configured = Environment.GetEnvironmentVariable("SHARPCOREPDF_CHROME");

        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
            "/Applications/Chromium.app/Contents/MacOS/Chromium",
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = new[]
        {
            "chrome.exe",
            "msedge.exe",
            "chrome",
            "msedge",
            "google-chrome",
            "google-chrome-stable",
            "chromium",
            "chromium-browser",
            "microsoft-edge",
        };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // A PATH entry that is not a legal path cannot hold a browser.
                }
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Nothing more can be done about a browser that refuses to die.
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A browser that is still shutting down may hold a file in its profile.
        }
        catch (UnauthorizedAccessException)
        {
            // The temporary profile is not worth failing a build over.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The build directory is temporary in every sense that matters.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}

