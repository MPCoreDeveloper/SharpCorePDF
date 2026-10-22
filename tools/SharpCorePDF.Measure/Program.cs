using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Pdf;
using SharpCorePDF.Infrastructure.Sources;
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

// The content-reading rows answer a different question — what a read costs, not what a merge costs — so
// they run on their own and generate the two documents they need:
//
//   dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --content
if (args.Contains("--content"))
{
    await ContentReading();
    return;
}

// The authoring rows answer a third question — what it costs to write a page — and need no corpus:
//
//   dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --author
if (args.Contains("--author"))
{
    Authoring();
    return;
}

// The creator and editor rows are the Release 1.1 acceptance shapes. They are recorded before the
// implementation so that the first passing phase already has a baseline to be judged against:
//
//   dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --creator
//   dotnet run -c Release --project .\tools\SharpCorePDF.Measure -- --editor
if (args.Contains("--creator"))
{
    CreatorParity();
    return;
}

if (args.Contains("--editor"))
{
    await EditorParity();
    return;
}

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

/// <summary>Measures the two content-reading shapes the manual and <c>docs/measurements.md</c> quote.</summary>
static async Task ContentReading()
{
    var reader = new PdfContentReaderService(new PdfPageContentReader());

    Console.WriteLine(
        $"environment: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSArchitecture}, "
        + $"{Environment.ProcessorCount} logical cores, server GC {GCSettings.IsServerGC}");
    Console.WriteLine(
        "content: page details, text extraction and payload copies; the documents are generated before the measured window");
    Console.WriteLine();

    var indexed = new PdfByteArraySource(ContentFixtures.Indexed(200), "indexed-200.pdf");

    // One warm-up, so JIT compilation is not part of the first row.
    await CountPages(indexed, reader, 200);

    await ContentRow("200 pages, every page enumerated", "pages", 1, () => CountPages(indexed, reader, 200));
    await ContentRow("200 pages, abandoned after 10", "pages", 1, () => CountPages(indexed, reader, 10));

    // The same 200 pages read for their text: one document whose pages draw a heading and six lines each,
    // so the row pays for the glyphs, the words and the blocks rather than for the page structure alone.
    var written = new PdfByteArraySource(ContentFixtures.Text(200), "text-200.pdf");

    await CountWords(written, reader, PageRange.All);

    await ContentRow("200 pages of text, every block read", "words", 1, () => CountWords(written, reader, PageRange.All));
    await ContentRow(
        "200 pages of text, ten pages read",
        "words",
        1,
        () => CountWords(written, reader, PageRange.FromTo(1, 10)));

    foreach (var megabytes in new[] { 1, 10, 50 })
    {
        var document = new PdfByteArraySource(
            ContentFixtures.Attachment(megabytes * 1024 * 1024),
            $"payload-{megabytes}mb.pdf");

        var files = await reader.ReadEmbeddedFilesAsync(document);
        var file = files.Items[0];

        // One copy outside the measured window, so the first measured copy is not the first code path.
        await CopyPayload(document, reader, file);

        await ContentRow(
            $"{megabytes,2} MB payload through the copy route",
            "MB",
            1024.0 * 1024.0,
            () => CopyPayload(document, reader, file));

        Console.WriteLine(
            $"{"",46}   source {document.Length:N0} B, payload {file.ByteLength:N0} B, "
            + $"buffered {file.IsBuffered}");
    }
}

/// <summary>Enumerates page details, stopping after the requested number of pages.</summary>
/// <param name="source">The document to read.</param>
/// <param name="reader">The reader to read it with.</param>
/// <param name="stopAfter">The number of pages after which the enumeration is abandoned.</param>
/// <returns>The number of pages that were yielded.</returns>
static async Task<long> CountPages(IPdfContentSource source, PdfContentReaderService reader, int stopAfter)
{
    var seen = 0;

    await foreach (var page in reader.ReadPageDetailsAsync(
        source,
        PageRange.All,
        cancellationToken: CancellationToken.None))
    {
        seen++;

        if (seen == stopAfter)
        {
            break;
        }
    }

    return seen;
}

/// <summary>Reads the text of a page range and returns the number of words it produced.</summary>
/// <param name="source">The document to read.</param>
/// <param name="reader">The reader to read it with.</param>
/// <param name="range">The pages whose text is wanted.</param>
/// <returns>The number of words of every block the read returned.</returns>
static async Task<long> CountWords(IPdfContentSource source, PdfContentReaderService reader, PageRange range)
{
    var text = await reader.ReadTextAsync(source, range, cancellationToken: CancellationToken.None);

    return text.Items.Sum(block => (long)block.Words.Count);
}

/// <summary>Copies the payload of one attachment to a stream that counts and keeps nothing.</summary>
/// <param name="source">The document to read.</param>
/// <param name="reader">The reader to read it with.</param>
/// <param name="file">The attachment to copy.</param>
/// <returns>The number of bytes the copy route wrote.</returns>
static async Task<long> CopyPayload(
    IPdfContentSource source,
    PdfContentReaderService reader,
    SharpCorePDF.Domain.ReadModels.PdfEmbeddedFileInfo file)
{
    var destination = new CountingStream();

    await reader.CopyEmbeddedFileDataAsync(source, file, destination);

    return destination.Written;
}

/// <summary>Runs one content-reading row three times and prints the median of each number.</summary>
/// <param name="label">The name of the row.</param>
/// <param name="unit">The unit the rate is stated in.</param>
/// <param name="unitScale">How many bytes one unit holds.</param>
/// <param name="action">The read to measure.</param>
static async Task ContentRow(string label, string unit, double unitScale, Func<Task<long>> action)
{
    const int Rounds = 3;

    var allocated = new List<long>(Rounds);
    var elapsed = new List<double>(Rounds);
    var completed = 0L;

    for (var index = 0; index < Rounds; index++)
    {
        CollectForMeasurement();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();

        completed = await action();

        watch.Stop();
        allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
        elapsed.Add(watch.Elapsed.TotalMilliseconds);
    }

    allocated.Sort();
    elapsed.Sort();

    var medianAllocated = allocated[allocated.Count / 2];
    var medianElapsed = elapsed[elapsed.Count / 2];
    var rate = completed / unitScale / (medianElapsed / 1000.0);

    Console.WriteLine(
        $"{label,-46} : {medianAllocated / 1024.0,9:N0} KB  {medianElapsed,8:N2} ms  "
        + $"{rate,7:N1} {unit}/s  live after GC {ManagedHeapBytes() / 1024.0 / 1024.0,5:N2} MB  "
        + $"units {completed:N0}");
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

/// <summary>Measures what writing a page costs: one cover and a 200 page document.</summary>
static void Authoring()
{
    var root = RepositoryRoot();
    var rgbLogo = File.ReadAllBytes(Path.Combine(root, "Logo-256.png"));
    var rgbaLogo = ContentFixtures.RgbaLogo;
    var body = PdfTypeface.FromTrueType(
        File.ReadAllBytes(Path.Combine(root, "examples", "SharpCorePDF.Examples", "Assets", "DejaVuSans-subset.ttf")));
    var heading = PdfTypeface.Standard(StandardFont.Helvetica);

    var options = PdfAuthoringOptions.Default with
    {
        CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        DocumentId = "00112233445566778899AABBCCDDEEFF",
    };

    Console.WriteLine(
        $"environment: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSArchitecture}, "
        + $"{Environment.ProcessorCount} logical cores, server GC {GCSettings.IsServerGC}");
    Console.WriteLine("authoring: a cover with a logo and six lines; the typeface is parsed outside the measured window");
    Console.WriteLine();

    byte[] Cover(byte[] logo) => PdfDocumentBuilder.Create(options)
        .AddPage(PdfPageSize.A4, page => page
            .DrawImage(logo, x: 40, y: 30, width: 152, height: 87)
            .DrawText("Décomptes SPRL", 330, 180, heading, 12)
            .DrawText("Rue de l'Église 1", 330, 194, heading, 12)
            .DrawText("1000 Bruxelles", 330, 208, heading, 12)
            .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", 150, 400, body, 12)
            .DrawText("pour la période du 01/01/2025 au 31/12/2025.", 150, 414, body, 12)
            .DrawText("Sending 1/3", 150, 550, body, 12))
        .ToArray();

    byte[] Pages(int count)
    {
        var builder = PdfDocumentBuilder.Create(options);

        for (var index = 0; index < count; index++)
        {
            var number = index + 1;
            builder.AddPage(PdfPageSize.A4, page => page
                .DrawText($"Sending {number}/{count}", 150, 550, body, 12)
                .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", 150, 400, body, 12));
        }

        return builder.ToArray();
    }

    AuthoringRow("1 page, RGB logo, 6 text lines", () => Cover(rgbLogo));
    AuthoringRow("1 page, RGBA logo, 6 text lines", () => Cover(rgbaLogo));
    AuthoringRow("200 pages, 2 text lines each", () => Pages(200));
}

/// <summary>Measures one authoring shape and prints its median allocations, time and size.</summary>
/// <param name="label">The row label.</param>
/// <param name="build">The document under measurement.</param>
static void AuthoringRow(string label, Func<byte[]> build)
{
    const int Rounds = 5;

    var allocated = new List<long>(Rounds);
    var elapsed = new List<double>(Rounds);
    var size = 0;

    // One warm-up, so JIT compilation is not part of the first measured build.
    _ = build();

    for (var round = 0; round < Rounds; round++)
    {
        CollectForMeasurement();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var document = build();

        watch.Stop();
        allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
        elapsed.Add(watch.Elapsed.TotalMilliseconds);
        size = document.Length;
    }

    allocated.Sort();
    elapsed.Sort();

    Console.WriteLine(
        $"{label,-46} : {allocated[Rounds / 2] / 1024.0,9:N0} KB  {elapsed[Rounds / 2],8:N2} ms  "
        + $"{size,9:N0} B output  live after GC {ManagedHeapBytes() / 1024.0 / 1024.0,5:N2} MB");
}

/// <summary>
/// Measures the creator acceptance shape: ten A4 pages carrying two hundred positioned text lines and
/// one RGBA image, using the same parsed typeface for every round.
/// </summary>
static void CreatorParity()
{
    const int PageCount = 10;
    const int LinesPerPage = 20;

    var root = RepositoryRoot();
    var body = PdfTypeface.FromTrueType(
        File.ReadAllBytes(Path.Combine(root, "examples", "SharpCorePDF.Examples", "Assets", "DejaVuSans-subset.ttf")));
    var heading = PdfTypeface.Standard(StandardFont.Helvetica);
    var logo = ContentFixtures.RgbaLogo;

    var options = PdfAuthoringOptions.Default with
    {
        CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        DocumentId = "00112233445566778899AABBCCDDEEFF",
    };

    Console.WriteLine(
        $"environment: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSArchitecture}, "
        + $"{Environment.ProcessorCount} logical cores, server GC {GCSettings.IsServerGC}");
    Console.WriteLine(
        $"creator: {PageCount} A4 pages, {PageCount * LinesPerPage} positioned lines, one RGBA logo; "
        + "typeface and image bytes are prepared outside the measured window");
    Console.WriteLine();

    byte[] Build(bool withLogo)
    {
        var builder = PdfDocumentBuilder.Create(options);

        for (var index = 0; index < PageCount; index++)
        {
            var number = index + 1;
            builder.AddPage(PdfPageSize.A4, page =>
            {
                page.DrawText($"Parity page {number}/{PageCount}", 40, 40, heading, 16);

                for (var line = 0; line < LinesPerPage; line++)
                {
                    page.DrawText(
                        $"Line {line + 1:D2}: the quick brown fox jumps over the lazy dog — 0123456789",
                        40,
                        80 + (line * 28),
                        body,
                        12);
                }

                if (withLogo)
                {
                    page.DrawImage(logo, x: 40, y: 700, width: 152, height: 87);
                }
            });
        }

        return builder.ToArray();
    }

    AuthoringRow("creator: 10 pages, 200 lines, RGBA logo", () => Build(withLogo: true));
    AuthoringRow("creator: 10 pages, 200 lines, no image", () => Build(withLogo: false));
}

/// <summary>
/// Measures the editor acceptance shape: two hundred operations over a hundred-page source (rotate every
/// page, duplicate fifty, move twenty-five, delete twenty-five) and writes the result to a counting sink.
/// </summary>
static async Task EditorParity()
{
    const int SourcePages = 100;
    const int Rounds = 5;

    var source = new PdfByteArraySource(ContentFixtures.EditorSource(SourcePages), "editor-source.pdf");
    var editor = new PdfDocumentEditorService(
        new PdfDocumentReader(PdfEngineOptions.Default),
        new PdfEditEngine(PdfEngineOptions.Default));

    var options = PdfEditOutputOptions.Default with
    {
        ModificationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        DocumentId = "00112233445566778899AABBCCDDEEFF",
    };

    static PdfEditPlan Plan(PdfEditOutputOptions output)
    {
        var plan = PdfEditPlan.Create(output);

        for (var page = 1; page <= SourcePages; page++)
        {
            plan = plan.RotatePage(page, PageRotation.Clockwise90);
        }

        for (var index = 0; index < 50; index++)
        {
            plan = plan.DuplicatePage(1, 101 + index);
        }

        for (var index = 0; index < 25; index++)
        {
            plan = plan.MovePage(150, 1);
        }

        for (var index = 0; index < 25; index++)
        {
            plan = plan.DeletePage(125);
        }

        return plan;
    }

    var plan = Plan(options);

    Console.WriteLine(
        $"environment: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSArchitecture}, "
        + $"{Environment.ProcessorCount} logical cores, server GC {GCSettings.IsServerGC}");
    Console.WriteLine(
        $"editor: {SourcePages} pages in, {plan.OperationCount} operations ({SourcePages} rotate, 50 duplicate, "
        + "25 move, 25 delete), 125 pages out, written to a counting sink");
    Console.WriteLine();

    // One warm-up, so JIT compilation and the first PDF parse are outside the measured rounds.
    await editor.ApplyAsync(source, plan, new CountingStream()).ConfigureAwait(false);

    var allocated = new List<long>(Rounds);
    var elapsed = new List<double>(Rounds);
    var size = 0L;

    for (var round = 0; round < Rounds; round++)
    {
        CollectForMeasurement();

        var destination = new CountingStream();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        await editor.ApplyAsync(source, plan, destination).ConfigureAwait(false);
        watch.Stop();

        allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
        elapsed.Add(watch.Elapsed.TotalMilliseconds);
        size = destination.Written;
    }

    allocated.Sort();
    elapsed.Sort();

    Console.WriteLine(
        $"{"editor: 200 operations, 100 to 125 pages",-46} : {allocated[Rounds / 2] / 1024.0,9:N0} KB  "
        + $"{elapsed[Rounds / 2],8:N2} ms  {size,9:N0} B output  "
        + $"live after GC {ManagedHeapBytes() / 1024.0 / 1024.0,5:N2} MB");
}
