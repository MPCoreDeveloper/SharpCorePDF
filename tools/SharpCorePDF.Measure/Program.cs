using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Measure;

// The §7.1 / §7.2 harness of addendum A7. Run it from the repository root:
//
//   dotnet run -c Release --project .\tools\SharpCorePDF.Measure
//   dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --single
//   $env:DOTNET_gcServer = "1"; dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --single
//
// Every row is a merge of the same total input bytes spread over a different number of sources: one
// source of 1 MB, ten of 100 KB, one hundred and fifty of 6.7 KB. That is what separates the cost of the
// bytes from the fixed cost of a source, which is the shape §7.1 budgets are stated in. The scaling gate
// compares rows that use the same document padded to 1 MB and to 0.5 MB at a constant total of 75 MB and
// of 150 MB, because comparing two different documents measures the corpus and not the source count.
// --single samples the managed heap during one isolated merge, which is the only way to attribute a peak
// to one scenario (§7.2).
const int TotalInputBytes = 1_000_000;
const int LargeInputBytes = 1_000_000;
const int Warmups = 2;
const int Iterations = 5;

var assets = Path.Combine(RepositoryRoot(), "tests", "SharpCorePDF.Tests", "Assets");
var scratch = Path.Combine(Path.GetTempPath(), "scpdf-assets");
Directory.CreateDirectory(scratch);

// Padding is inserted before the trailer keyword, so every padded file stays a valid PDF.
var oneLarge = Pad(Path.Combine(assets, "SafeWebCore.pdf"), Path.Combine(scratch, "large.pdf"), TotalInputBytes);
var tenMedium = Enumerable.Range(0, 10)
    .Select(index => Pad(Path.Combine(assets, "Modern.pdf"), Path.Combine(scratch, $"ten-{index}.pdf"), TotalInputBytes / 10))
    .ToArray();
var manySmall = Enumerable.Range(0, 150)
    .Select(index => Pad(Path.Combine(assets, "Modern.pdf"), Path.Combine(scratch, $"many-{index}.pdf"), TotalInputBytes / 150))
    .ToArray();
var manyHalf = Enumerable.Range(0, 300)
    .Select(index => Pad(Path.Combine(assets, "Modern.pdf"), Path.Combine(scratch, $"half-{index}.pdf"), LargeInputBytes / 2))
    .ToArray();
var manyLarge = Enumerable.Range(0, 150)
    .Select(index => Pad(Path.Combine(assets, "SafeWebCore.pdf"), Path.Combine(scratch, $"big-{index}.pdf"), LargeInputBytes))
    .ToArray();

// The pair the §7.1 scaling gate needs: the same document padded to 1 MB and to 0.5 MB, so that doubling
// the number of sources at constant total bytes is the only thing that changed.
var seventyFiveLarge = Enumerable.Range(0, 75)
    .Select(index => Pad(Path.Combine(assets, "Modern.pdf"), Path.Combine(scratch, $"quarter-{index}.pdf"), LargeInputBytes))
    .ToArray();
var hundredFiftyHalf = Enumerable.Range(0, 150)
    .Select(index => Pad(Path.Combine(assets, "Modern.pdf"), Path.Combine(scratch, $"half-of-half-{index}.pdf"), LargeInputBytes / 2))
    .ToArray();
var hundredFiftyLarge = Enumerable.Range(0, 150)
    .Select(index => Pad(Path.Combine(assets, "Modern.pdf"), Path.Combine(scratch, $"large-of-large-{index}.pdf"), LargeInputBytes))
    .ToArray();

// The bytes of the stream-backed sources are read once, outside the measured region, so that what is
// measured for a stream source is the library's own work and not the harness reading a file.
var oneLargeBytes = await File.ReadAllBytesAsync(oneLarge);
var manySmallBytes = new byte[manySmall.Length][];

for (var index = 0; index < manySmall.Length; index++)
{
    manySmallBytes[index] = await File.ReadAllBytesAsync(manySmall[index]);
}

var plain = new PdfMergerService(new PdfDocumentReader(PdfEngineOptions.Default), new PdfMergeEngine(PdfEngineOptions.Default));
var verifying = new PdfMergerService(
    new PdfDocumentReader(PdfEngineOptions.Default with { VerifyOutput = true }),
    new PdfMergeEngine(PdfEngineOptions.Default with { VerifyOutput = true }));

Console.WriteLine(
    $"inputs: 1 x {new FileInfo(oneLarge).Length:N0} B, 10 x {new FileInfo(tenMedium[0]).Length:N0} B, "
    + $"150 x {new FileInfo(manySmall[0]).Length:N0} B (about {TotalInputBytes:N0} B of input each way), "
    + $"150 x {new FileInfo(manyLarge[0]).Length:N0} B and 300 x {new FileInfo(manyHalf[0]).Length:N0} B");
Console.WriteLine(
    $"environment: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSArchitecture}, "
    + $"{Environment.ProcessorCount} logical cores, server GC {GCSettings.IsServerGC}");
Console.WriteLine();

// A single isolated merge under a heap sampler: this is what "peak" means for §7.2, because a number
// measured after ten other merges belongs to none of them. The process peak working set cannot be reset
// inside a process, so one scenario per process is the honest way to read that column:
//
//   dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --single large
if (args.Contains("--single"))
{
    var wanted = args.SkipWhile(argument => argument != "--single").Skip(1).FirstOrDefault() ?? "all";

    if (wanted is "all" or "large")
    {
        await Single("150 sources x 1.0 MB (files)", 150L * LargeInputBytes, plain, () => PlanOfFiles(manyLarge));
    }

    if (wanted is "all" or "one")
    {
        await Single("1 source   x 1.0 MB (files)", LargeInputBytes, plain, () => PlanOfFiles([oneLarge]));
    }

    if (wanted is "all" or "half")
    {
        await Single("300 sources x 0.5 MB (files)", 300L * (LargeInputBytes / 2), plain, () => PlanOfFiles(manyHalf));
    }

    return;
}

var oneSource = await Report("1 source   x 1.0 MB (files)          ", TotalInputBytes, plain, () => PlanOfFiles([oneLarge]), Iterations);
var tenSources = await Report("10 sources x 100 KB (files)         ", TotalInputBytes, plain, () => PlanOfFiles(tenMedium), Iterations);
var manySources = await Report("150 sources x 6.7 KB (files)        ", TotalInputBytes, plain, () => PlanOfFiles(manySmall), Iterations);
await Report("1 source   x 1.0 MB (verify on)     ", TotalInputBytes, verifying, () => PlanOfFiles([oneLarge]), Iterations);
await Report("150 sources x 6.7 KB (verify on)    ", TotalInputBytes, verifying, () => PlanOfFiles(manySmall), Iterations);
await Report("1 source   x 1.0 MB (stream capture)", TotalInputBytes, plain, () => PlanOfStreams([oneLargeBytes]), Iterations);
await Report("150 sources x 6.7 KB (stream capture)", TotalInputBytes, plain, () => PlanOfStreams(manySmallBytes), 3);
var largeSources = await Report("150 sources x 1.0 MB (corpus doc)   ", 150L * LargeInputBytes, plain, () => PlanOfFiles(manyLarge), 3);
var halfSources = await Report("300 sources x 0.5 MB (one document) ", 300L * (LargeInputBytes / 2), plain, () => PlanOfFiles(manyHalf), 3);
var quarterSources = await Report("75 sources x 1.0 MB (one document)  ", 75L * LargeInputBytes, plain, () => PlanOfFiles(seventyFiveLarge), 3);
var doubledSources = await Report("150 sources x 0.5 MB (one document)", 150L * (LargeInputBytes / 2), plain, () => PlanOfFiles(hundredFiftyHalf), 3);
var fullSources = await Report("150 sources x 1.0 MB (one document) ", 150L * LargeInputBytes, plain, () => PlanOfFiles(hundredFiftyLarge), 3);

// The budgets are stated in prd.md §7.1. The scaling ratio is the like-for-like pair (the same document
// padded to 1 MB and to 0.5 MB, both 75 MB in total); the ratio of the two 150 MB rows is printed next to
// it because those rows use different documents. The per-source figure comes from the constant-total-bytes
// family, because the 150 x 1.0 MB row is dominated by the bytes and would fold the copy cost into the
// fixed cost of a source.
Console.WriteLine();
Console.WriteLine(
    $"gates (NFR 7.1): 150 x 1.0 MB in {largeSources / 1000.0:N2} s / {150.0 * LargeInputBytes / 1024 / 1024 / (largeSources / 1000.0):N1} MB/s "
    + $"(budget 10.00 s / 15.0 MB/s); doubling at constant bytes is {doubledSources / quarterSources:N2}x at 75 MB and {halfSources / fullSources:N2}x at 150 MB (budget 1.20x); "
    + $"fixed cost {(manySources - oneSource) / 149.0:N2} ms per extra source over 150 and {(tenSources - oneSource) / 9.0:N2} ms over ten (budget 1.00 ms)");
Console.WriteLine($"peak working set after the large runs: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0:N1} MB");

Console.WriteLine();
Console.WriteLine($"peak working set over the whole run: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0:N1} MB");
Console.WriteLine($"managed heap retained after the run: {ManagedHeapBytes() / 1024.0 / 1024.0:N2} MB");

/// <summary>Joins the paths into one plan, every page of every source.</summary>
static MergePlan PlanOfFiles(IReadOnlyList<string> paths)
{
    var plan = MergePlan.Create();

    foreach (var path in paths)
    {
        plan = plan.AddFile(path);
    }

    return plan;
}

/// <summary>Joins the byte arrays into one plan of caller-supplied streams.</summary>
static MergePlan PlanOfStreams(IReadOnlyList<byte[]> contents)
{
    var plan = MergePlan.Create();

    // The bytes are handed over as caller streams, so each source captures its stream on first open.
    foreach (var content in contents)
    {
        plan = plan.AddStream(new MemoryStream(content, writable: false), "source.pdf");
    }

    return plan;
}

/// <summary>Runs a scenario and writes one row: allocations per merge, wall time, ratio and output size.</summary>
/// <param name="label">The name of the scenario.</param>
/// <param name="inputBytes">The bytes of input the scenario reads, which is what the ratio is against.</param>
/// <param name="merger">The merger under test.</param>
/// <param name="plan">Creates the plan, once per merge, so planning is not shared state.</param>
/// <param name="iterations">How many measured merges to take the median of.</param>
/// <returns>The median wall time of one merge, in milliseconds.</returns>
static async Task<double> Report(string label, long inputBytes, PdfMergerService merger, Func<MergePlan> plan, int iterations)
{
    for (var index = 0; index < Warmups; index++)
    {
        await Merge(merger, plan(), new CountingStream());
    }

    var allocated = new List<long>(iterations);
    var elapsed = new List<double>(iterations);
    var output = 0L;
    var live = 0L;

    for (var index = 0; index < iterations; index++)
    {
        var measured = await Merge(merger, plan(), new CountingStream());
        allocated.Add(measured.Allocated);
        elapsed.Add(measured.Milliseconds);
        output = measured.OutputBytes;
        live = ManagedHeapBytes();
    }

    allocated.Sort();
    elapsed.Sort();

    var medianAllocated = allocated[allocated.Count / 2];
    var medianElapsed = elapsed[elapsed.Count / 2];
    var megabytesPerSecond = inputBytes / 1024.0 / 1024.0 / (medianElapsed / 1000.0);

    Console.WriteLine(
        $"{label} : {medianAllocated / 1024.0,9:N0} KB/merge  {medianElapsed,8:N2} ms/merge  "
        + $"{medianAllocated / (double)inputBytes,5:N2}x input  {megabytesPerSecond,7:N1} MB/s  "
        + $"live after GC {live / 1024.0 / 1024.0,5:N2} MB  output {output:N0} B");

    return medianElapsed;
}

/// <summary>Merges once, with the heap collected before and the allocation counter read precisely.</summary>
static async Task<(long Allocated, long OutputBytes, double Milliseconds)> Merge(
    PdfMergerService merger,
    MergePlan plan,
    CountingStream destination)
{
    CollectForMeasurement();

    var before = GC.GetTotalAllocatedBytes(precise: true);
    var watch = Stopwatch.StartNew();

    await merger.MergeAsync(plan, destination, CancellationToken.None);

    watch.Stop();

    return (GC.GetTotalAllocatedBytes(precise: true) - before, destination.Written, watch.Elapsed.TotalMilliseconds);
}

/// <summary>Collects the heap so that a merge starts from a known one, which is what makes a peak its own.</summary>
/// <remarks>
/// Forcing a collection is the measurement: a peak read on top of the garbage of the previous merge belongs
/// to no scenario at all (§7.2). This is a harness, not a library path, so the usual advice does not apply.
/// </remarks>
static void CollectForMeasurement()
{
    GC.Collect(); // NOSONAR: forcing the collection is the measurement itself, not an optimisation to skip.
    GC.WaitForPendingFinalizers();
    GC.Collect(); // NOSONAR: a finalizer can resurrect its object, so one pass is not a collection.
}

/// <summary>Reads the retained managed heap, collecting first so that only what is reachable is counted.</summary>
/// <returns>The number of bytes the managed heap holds.</returns>
static long ManagedHeapBytes() =>
    GC.GetTotalMemory(forceFullCollection: true); // NOSONAR: the retained heap is the number the harness reports.

/// <summary>Runs one merge in this process while a background thread samples the managed heap.</summary>
/// <param name="label">The name of the scenario.</param>
/// <param name="inputBytes">The bytes of input the scenario reads.</param>
/// <param name="merger">The merger under test.</param>
/// <param name="plan">Creates the plan.</param>
static async Task Single(string label, long inputBytes, PdfMergerService merger, Func<MergePlan> plan)
{
    // One warm-up so that JIT compilation is not part of the peak.
    await Merge(merger, plan(), new CountingStream());
    CollectForMeasurement();

    var running = true;
    var peakHeap = 0L;
    var sampler = new Thread(() =>
    {
        while (Volatile.Read(ref running))
        {
            var current = GC.GetTotalMemory(forceFullCollection: false);

            if (current > Volatile.Read(ref peakHeap))
            {
                Volatile.Write(ref peakHeap, current);
            }

            Thread.Sleep(1);
        }
    })
    {
        IsBackground = true,
    };

    var watch = Stopwatch.StartNew();
    sampler.Start();

    var measured = await Merge(merger, plan(), new CountingStream());

    watch.Stop();
    running = false;
    sampler.Join();

    CollectForMeasurement();

    var managedPeak = Volatile.Read(ref peakHeap);

    Console.WriteLine(
        $"{label} : {measured.Allocated / 1024.0 / 1024.0,7:N1} MB allocated, {watch.Elapsed.TotalMilliseconds,7:N0} ms, "
        + $"peak managed heap {managedPeak / 1024.0 / 1024.0,6:N1} MB ({managedPeak / (double)inputBytes,5:N2}x input), "
        + $"live after GC {ManagedHeapBytes() / 1024.0 / 1024.0,5:N1} MB, "
        + $"process peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0,6:N1} MB, "
        + $"server GC {GCSettings.IsServerGC}, output {measured.OutputBytes:N0} B");
}

/// <summary>Finds the repository root by walking up to the folder that holds the fixture corpus.</summary>
/// <returns>The absolute path of the repository root.</returns>
/// <exception cref="DirectoryNotFoundException">The harness is running outside the repository.</exception>
static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "tests", "SharpCorePDF.Tests", "Assets", "SafeWebCore.pdf")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException(
        $"The fixture corpus was not found above {AppContext.BaseDirectory}. "
        + @"This harness reads the documents of tests\SharpCorePDF.Tests\Assets.");
}

/// <summary>Copies a fixture to the scratch folder, padded to the wanted size.</summary>
/// <param name="source">The fixture to copy.</param>
/// <param name="destination">Where the padded copy goes.</param>
/// <param name="size">The wanted size in bytes.</param>
/// <returns>The path of the padded copy.</returns>
static string Pad(string source, string destination, int size)
{
    var text = Encoding.Latin1.GetString(File.ReadAllBytes(source));

    if (text.Length < size)
    {
        var extra = size - text.Length;
        var padding = new char[extra];
        var written = 0;

        // Comment lines are whitespace to a reader and cannot contain the trailer keyword; the tail is
        // spaces, so that the keyword itself is never swallowed by a comment.
        while (written + 81 <= extra)
        {
            padding[written++] = '%';

            for (var index = 0; index < 79; index++)
            {
                padding[written++] = 'p';
            }

            padding[written++] = '\n';
        }

        while (written < extra)
        {
            padding[written++] = ' ';
        }

        var at = text.LastIndexOf("startxref", StringComparison.Ordinal);
        text = string.Concat(text.AsSpan(0, at), new string(padding), text.AsSpan(at));
    }

    File.WriteAllBytes(destination, Encoding.Latin1.GetBytes(text));

    return destination;
}
