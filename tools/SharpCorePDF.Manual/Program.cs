using System.Globalization;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Manual;

/// <summary>
/// Builds the SharpCorePDF manual as a PDF from the Markdown chapters.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is deliberately short: Markdown to HTML here, HTML to PDF in Chromium, and the
/// finishing pass here again. Two passes over Chromium are made, because the contents can only state a
/// page number that has been measured, and measuring needs a layout. The first pass lays the document
/// out with placeholders; the headings are then located in the produced PDF by reading its text; the
/// second pass prints the measured numbers. The second layout has the same pagination as the first
/// because the numbers sit in a fixed-width, right-aligned column, and the result is verified.
/// </para>
/// <para>
/// Usage:
/// <c>dotnet run --project tools/SharpCorePDF.Manual -- [--out file.pdf] [--root path] [--version x.y.z] [--keep-html] [--no-stamp]</c>
/// </para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(argument => argument is "-h" or "--help"))
        {
            PrintUsage();
            return 0;
        }

        var options = ParseArguments(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return await BuildAsync(options).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            return 1;
        }
    }

    private static async Task<int> BuildAsync(Options options)
    {
        var manualDirectory = Path.Combine(options.Root, "docs", "manual");
        var sourceDirectory = Path.Combine(manualDirectory, "src");
        var buildDirectory = Path.Combine(manualDirectory, "build");
        var stylesheet = Path.Combine(manualDirectory, "manual.css");
        var logo = Path.Combine(options.Root, "Logo-256.png");

        var files = Directory
            .GetFiles(sourceDirectory, "*.md")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            Console.Error.WriteLine($"no chapters in {sourceDirectory}");
            return 1;
        }

        var printer = new ChromiumPrinter(buildDirectory);
        if (!printer.IsAvailable)
        {
            Console.Error.WriteLine("no Chrome or Edge found: the PDF is printed by a browser engine.");
            return 1;
        }

        var identity = new ManualIdentity(
            options.Version,
            "SharpCorePDF",
            string.Create(CultureInfo.InvariantCulture, $"SharpCorePDF {options.Version} — The Manual"));

        var cancellationToken = CancellationToken.None;

        Console.WriteLine($"chapters : {files.Length}");
        Console.WriteLine($"browser  : {printer.BrowserPath}");

        var bodies = new List<string>(files.Length);
        foreach (var file in files)
        {
            var markdown = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            bodies.Add(MarkdownConverter.Convert(markdown));
        }

        var usedAnchors = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < bodies.Count; index++)
        {
            bodies[index] = MarkdownConverter.UniquifyHeadingIds(bodies[index], usedAnchors);
        }

        var headings = bodies
            .SelectMany(body => MarkdownConverter.CollectHeadings(body))
            .ToList();

        // Pass 1: layout with placeholder numbers, so the body's pagination is final.
        var firstHtml = ManualHtml.Assemble(headings, bodies, null, identity, stylesheet, logo);
        var first = await printer
            .RenderAsync(firstHtml, "pass1.pdf", options.KeepHtml, cancellationToken)
            .ConfigureAwait(false);

        var firstProbe = await PageIndex
            .ReadAsync(new PdfByteArraySource(first, "pass1.pdf"), cancellationToken)
            .ConfigureAwait(false);

        var start = PageIndex.BodyStart(firstProbe.Pages);
        var (pages, total) = PageIndex.Locate(firstProbe.Pages, headings, start);

        // Pass 2: the measured numbers. The layout must not move; verify, and settle if it did.
        var second = await printer
            .RenderAsync(
                ManualHtml.Assemble(headings, bodies, pages, identity, stylesheet, logo),
                "pass2.pdf",
                options.KeepHtml,
                cancellationToken)
            .ConfigureAwait(false);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var probe = await PageIndex
                .ReadAsync(new PdfByteArraySource(second, "pass2.pdf"), cancellationToken)
                .ConfigureAwait(false);

            var (measured, _) = PageIndex.Locate(probe.Pages, headings, start);
            if (PageIndex.Same(pages, measured))
            {
                break;
            }

            pages = measured;
            second = await printer
                .RenderAsync(
                    ManualHtml.Assemble(headings, bodies, pages, identity, stylesheet, logo),
                    "pass2.pdf",
                    options.KeepHtml,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var finished = options.NoStamp
            ? second
            : await ManualFinisher
                .FinishAsync(second, headings, pages, identity, cancellationToken)
                .ConfigureAwait(false);

        var outputDirectory = Path.GetDirectoryName(options.Output);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllBytesAsync(options.Output, finished, cancellationToken).ConfigureAwait(false);

        // Verify what was written: it must reopen, keep its pages, and carry the footer.
        var check = await PageIndex
            .ReadAsync(new PdfByteArraySource(finished, "manual.pdf"), cancellationToken)
            .ConfigureAwait(false);

        var footerOk = options.NoStamp
            || (check.Pages.Count > 1 && check.Pages[1].Contains("Page 2 of", StringComparison.Ordinal));

        Console.WriteLine(
            $"headings : {headings.Count} (contents lists {headings.Count(heading => heading.Level <= 2)})");
        Console.WriteLine($"pages    : {check.Pages.Count} (measured {total})");
        Console.WriteLine($"outline  : {check.Info.OutlineItemCount} bookmark(s)");
        Console.WriteLine($"footer   : {(options.NoStamp ? "skipped" : footerOk ? "ok" : "MISSING")}");
        Console.WriteLine(
            $"output   : {options.Output} ({new FileInfo(options.Output).Length / 1024} KB)");

        return footerOk ? 0 : 1;
    }

    private static Options? ParseArguments(string[] args)
    {
        string? root = null;
        string? output = null;
        var version = "1.3.0";
        var keepHtml = false;
        var noStamp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--root":
                    index++;
                    if (index >= args.Length)
                    {
                        Console.Error.WriteLine("--root needs a path.");
                        return null;
                    }

                    root = args[index];
                    break;
                case "--out":
                    index++;
                    if (index >= args.Length)
                    {
                        Console.Error.WriteLine("--out needs a path.");
                        return null;
                    }

                    output = args[index];
                    break;
                case "--version":
                    index++;
                    if (index >= args.Length)
                    {
                        Console.Error.WriteLine("--version needs a value.");
                        return null;
                    }

                    version = args[index];
                    break;
                case "--keep-html":
                    keepHtml = true;
                    break;
                case "--no-stamp":
                    noStamp = true;
                    break;
                default:
                    Console.Error.WriteLine($"unknown argument: {args[index]}");
                    return null;
            }
        }

        root ??= FindRepositoryRoot();
        if (root is null)
        {
            Console.Error.WriteLine("could not find the repository root: pass --root <path>.");
            return null;
        }

        root = Path.GetFullPath(root);
        if (!Directory.Exists(Path.Combine(root, "docs", "manual", "src")))
        {
            Console.Error.WriteLine($"{root} does not contain docs/manual/src.");
            return null;
        }

        var resolvedOutput = output is null
            ? Path.Combine(
                root,
                "docs",
                string.Create(CultureInfo.InvariantCulture, $"SharpCorePDF-Manual-{version}.pdf"))
            : Path.GetFullPath(output);

        return new Options(root, resolvedOutput, version, keepHtml, noStamp);
    }

    private static string? FindRepositoryRoot()
    {
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "docs", "manual", "src"))
                    && File.Exists(Path.Combine(directory.FullName, "docs", "manual", "manual.css")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SharpCorePDF manual builder");
        Console.WriteLine();
        Console.WriteLine("  --root <path>      the repository root (default: found from the tool's location)");
        Console.WriteLine("  --out <path>       the PDF to write (default: docs/SharpCorePDF-Manual-<version>.pdf)");
        Console.WriteLine("  --version <x.y.z>  the version the cover and metadata state (default: 1.3.0)");
        Console.WriteLine("  --keep-html        keep the intermediate HTML in docs/manual/build");
        Console.WriteLine("  --no-stamp         skip the footer, the outline and the metadata");
        Console.WriteLine("  --help             show this message");
    }

    private sealed record Options(
        string Root,
        string Output,
        string Version,
        bool KeepHtml,
        bool NoStamp);
}

