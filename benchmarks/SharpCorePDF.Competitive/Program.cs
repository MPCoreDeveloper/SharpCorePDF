using System.Globalization;
using SharpCorePDF.Competitive;

var options = CliOptions.Parse(args);
var root = FindRoot();
var output = Path.GetFullPath(options.Output ?? Path.Combine(root, "artifacts", "competitive"));
Directory.CreateDirectory(output);

var font = await File.ReadAllBytesAsync(Path.Combine(
    root,
    "tests",
    "SharpCorePDF.Tests",
    "Assets",
    "DejaVuSans-subset.ttf"));

EngineAdapter[] adapters =
[
    new SharpCorePdfAdapter(font),
    new PdfPigAdapter(font),
    new PdfSharpAdapter(font),
];

// One engine per process is the release-cite recipe: the filter runs a single adapter in a process of
// its own, so one engine's heap, GC mode and JIT state cannot colour another engine's numbers.
if (options.Engine is { } engineName)
{
    adapters = [.. adapters.Where(adapter => string.Equals(adapter.Name, engineName, StringComparison.OrdinalIgnoreCase))];

    if (adapters.Length == 0)
    {
        Console.Error.WriteLine($"No adapter named '{engineName}'. Known engines: SharpCorePDF, PdfPig, PDFsharp.");
        return 2;
    }
}

Console.WriteLine($"Competitive harness output: {output}");
Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Engines: {string.Join(", ", adapters.Select(adapter => $"{adapter.Name} {adapter.Version}"))}"));
Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Warm-ups={options.Warmups}, iterations={options.Iterations}, correctness={!options.SkipCorrectness}, performance={!options.SkipPerformance}"));

IReadOnlyList<CorrectnessResult> correctness = [];
if (!options.SkipCorrectness)
{
    Console.WriteLine("Running labelled correctness...");
    var labels = CorrectnessRunner.LoadLabels(root);
    correctness = await CorrectnessRunner.RunAsync(adapters, labels, CancellationToken.None);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {correctness.Count} document/engine rows."));
}

IReadOnlyList<PerformanceSample> samples = [];
IReadOnlyList<PerformanceSummary> performance = [];
if (!options.SkipPerformance)
{
    Console.WriteLine("Running performance scenarios...");
    (samples, performance) = await PerformanceRunner.RunAsync(
        adapters,
        new PerformanceOptions(options.Warmups, options.Iterations),
        CancellationToken.None);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {performance.Count} scenario/engine summaries."));
}

IReadOnlyList<RobustnessResult> robustness = [];
if (options.RobustnessFolder is { } folder)
{
    Console.WriteLine($"Running robustness on {folder}...");
    robustness = await RobustnessRunner.RunAsync(
        adapters,
        new RobustnessOptions(
            Path.GetFullPath(folder),
            options.RobustnessLimit,
            options.RobustnessMaxBytes,
            TimeSpan.FromSeconds(options.RobustnessTimeoutSeconds),
            options.RobustnessLock,
            options.WriteRobustnessLock),
        CancellationToken.None);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {robustness.Count} file/engine rows."));
}

if (options.Capture)
{
    Console.WriteLine("Capturing one unmeasured PDF per writer...");
    var captured = await PerformanceRunner.CaptureOutputs(
        adapters,
        Path.Combine(output, "captures"),
        CancellationToken.None);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {captured.Count} files under captures/."));
}

var report = new BenchmarkReport(
    DateTimeOffset.UtcNow,
    EnvironmentInfo.Capture(new PerformanceOptions(options.Warmups, options.Iterations)),
    adapters.Select(adapter => new EngineInfo(adapter.Name, adapter.Version)).ToArray(),
    correctness,
    samples,
    performance,
    robustness);

var written = await ReportWriter.WriteAsync(output, report, CancellationToken.None);
foreach (var path in written)
{
    Console.WriteLine($"Wrote {path}");
}

return 0;

static string FindRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "SharpCorePDF.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Could not locate the repository root.");
}