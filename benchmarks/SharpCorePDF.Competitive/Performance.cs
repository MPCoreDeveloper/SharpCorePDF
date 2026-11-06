using System.Diagnostics;
using System.Runtime.CompilerServices;
using SharpCorePDF.Measure;

namespace SharpCorePDF.Competitive;

internal sealed record PerformanceOptions(int Warmups, int Iterations);
internal sealed record PerformanceSample(
    string Engine,
    string Scenario,
    int Iteration,
    string Status,
    double ElapsedMilliseconds,
    long AllocatedBytes,
    long ApproximatePeakManagedBytes,
    long RetainedManagedBytes,
    string Work,
    string? Error);

internal sealed record PerformanceSummary(
    string Engine,
    string Scenario,
    int SuccessfulSamples,
    double MedianMilliseconds,
    double P95Milliseconds,
    double MinimumMilliseconds,
    double MaximumMilliseconds,
    long MedianAllocatedBytes,
    long MedianApproximatePeakManagedBytes,
    long MedianRetainedManagedBytes,
    string Work,
    string? Error);

internal static class PerformanceRunner
{
    private sealed record Scenario(
        string Name,
        Func<EngineAdapter, bool> Supports,
        Func<EngineAdapter, CancellationToken, Task<string>> Invoke);

    public static async Task<(IReadOnlyList<PerformanceSample> Samples, IReadOnlyList<PerformanceSummary> Summaries)> RunAsync(
        IReadOnlyList<EngineAdapter> adapters,
        PerformanceOptions options,
        CancellationToken cancellationToken)
    {
        var text = ContentFixtures.Text(200);
        var inlineImage = ContentFixtures.InlineImage(1024, 1024);
        var mergeSources = Enumerable.Range(0, 10).Select(_ => ContentFixtures.Text(10)).ToArray();
        var scenarios = new List<Scenario>
        {
            new Scenario("text-200-all", adapter => adapter.SupportsExtraction, async (adapter, token) =>
                (await adapter.ExtractAsync(text, PageSelection.All, token)).WorkSummary()),
            new Scenario("text-200-all-warm", adapter => adapter.SupportsExtraction, async (adapter, token) =>
                (await adapter.ExtractWarmAsync(text, PageSelection.All, token)).WorkSummary()),
            new Scenario("text-200-pages-1-10", adapter => adapter.SupportsExtraction, async (adapter, token) =>
                (await adapter.ExtractAsync(text, new PageSelection(1, 10), token)).WorkSummary()),
            new Scenario("create-10x20-truetype", adapter => adapter.SupportsCreation, async (adapter, token) =>
                (await adapter.CreateAsync(10, 20, token)).WorkSummary()),
            new Scenario("text-200-all-blocks-docstrum", adapter => adapter.SupportsBlocks, async (adapter, token) =>
                (await adapter.ExtractBlocksAsync(text, PageSelection.All, token)).WorkSummary()),
            new Scenario("merge-10x10", adapter => adapter.SupportsMerge, async (adapter, token) =>
                (await adapter.MergeAsync(mergeSources, token)).WorkSummary()),
            new Scenario("extract-1-inline-image-1mb", adapter => adapter.SupportsExtraction, async (adapter, token) =>
                (await adapter.ExtractAsync(inlineImage, PageSelection.All, token)).WorkSummary()),
            new Scenario("text-200-two-questions", adapter => adapter.SupportsExtraction, async (adapter, token) =>
                (await adapter.ExtractTwiceAsync(text, PageSelection.All, token)).WorkSummary() + "; questions=2"),
        };
        // Alternate word strategies are opt-in rows per adapter: the same document and the same work
        // summary, grouped by a named algorithm instead of the default. They never replace the default
        // extraction row above.
        foreach (var adapter in adapters)
        {
            foreach (var strategy in adapter.AlternateExtractionStrategies)
            {
                var name = $"text-200-all-{strategy}-alt";
                var scenarios2 = scenarios.ToList();
                scenarios2.Add(new Scenario(
                    name,
                    candidate => ReferenceEquals(candidate, adapter),
                    async (candidate, token) =>
                        (await candidate.ExtractStrategyAsync(strategy, text, PageSelection.All, token)).WorkSummary()));
                scenarios = [.. scenarios2];
            }
        }

        // One pass over every scenario before anything is measured: the first call of a method pays
        // tiered compilation, and a row that includes it says more about the JIT than about the engine.
        // A failure here is discarded; the measured pass reports it with the engine and scenario named.
        foreach (var scenario in scenarios)
        {
            foreach (var adapter in adapters.Where(scenario.Supports))
            {
                try
                {
                    _ = await scenario.Invoke(adapter, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Console.Error.WriteLine(
                        $"prewarm {adapter.Name}/{scenario.Name} failed: {exception.GetType().Name}");
                }
            }
        }

        var samples = new List<PerformanceSample>();
        var summaries = new List<PerformanceSummary>();

        foreach (var scenario in scenarios)
        {
            var supported = adapters.Where(scenario.Supports).ToArray();
            var ready = new List<EngineAdapter>();
            foreach (var adapter in supported)
            {
                try
                {
                    for (var warmup = 0; warmup < options.Warmups; warmup++)
                    {
                        _ = await scenario.Invoke(adapter, cancellationToken);
                    }

                    ready.Add(adapter);
                }
                catch (Exception exception)
                {
                    var error = exception.GetType().Name + ": " + exception.Message;
                    samples.Add(new PerformanceSample(
                        adapter.Name, scenario.Name, -1, "Error", 0, 0, 0, 0, string.Empty, error));
                    summaries.Add(ErrorSummary(adapter.Name, scenario.Name, error));
                }
            }

            supported = ready.ToArray();

            for (var iteration = 0; iteration < options.Iterations; iteration++)
            {
                var ordered = iteration % 2 == 0 ? supported : supported.Reverse().ToArray();
                foreach (var adapter in ordered)
                {
                    var sample = await Measure(adapter, scenario, iteration, cancellationToken);
                    samples.Add(sample);
                    if (sample.Status == "Error")
                    {
                        summaries.Add(ErrorSummary(adapter.Name, scenario.Name, sample.Error));
                        supported = supported.Where(candidate => candidate != adapter).ToArray();
                    }
                }
            }

            foreach (var adapter in supported)
            {
                summaries.Add(Summarize(adapter.Name, scenario.Name, samples));
            }
        }

        return (samples, summaries.Where(summary => summary is not null).ToArray());
    }

    private static async Task<PerformanceSample> Measure(
        EngineAdapter adapter,
        Scenario scenario,
        int iteration,
        CancellationToken cancellationToken)
    {
        Collect();
        var peak = new StrongBox<long>(GC.GetTotalMemory(false));
        var running = true;
        var sampler = new Thread(() => SampleHeap(peak, () => Volatile.Read(ref running)))
        {
            IsBackground = true,
            Name = "competitive-heap-sampler",
        };
        sampler.Start();
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();

        try
        {
            var work = await scenario.Invoke(adapter, cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Volatile.Write(ref running, false);
            sampler.Join();
            var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
            ConsiderPeak(peak, GC.GetTotalMemory(false));
            Collect();
            return new PerformanceSample(
                adapter.Name,
                scenario.Name,
                iteration,
                "Completed",
                elapsed,
                allocated,
                peak.Value,
                GC.GetTotalMemory(false),
                work,
                null);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref running, false);
            sampler.Join();
            return new PerformanceSample(
                adapter.Name, scenario.Name, iteration, "Error", 0, 0, 0, 0, string.Empty,
                exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static PerformanceSummary Summarize(
        string engine,
        string scenario,
        IReadOnlyList<PerformanceSample> all)
    {
        var samples = all.Where(sample =>
            sample.Engine == engine && sample.Scenario == scenario && sample.Status == "Completed").ToArray();
        if (samples.Length == 0)
        {
            return ErrorSummary(engine, scenario, "No completed samples.");
        }

        var elapsed = samples.Select(sample => sample.ElapsedMilliseconds).Order().ToArray();
        var work = string.Join(" | ", samples.Select(sample => sample.Work).Distinct(StringComparer.Ordinal));
        return new PerformanceSummary(
            engine,
            scenario,
            samples.Length,
            Median(elapsed),
            Percentile(elapsed, 0.95d),
            elapsed[0],
            elapsed[^1],
            MedianLong(samples.Select(sample => sample.AllocatedBytes)),
            MedianLong(samples.Select(sample => sample.ApproximatePeakManagedBytes)),
            MedianLong(samples.Select(sample => sample.RetainedManagedBytes)),
            work,
            null);
    }

    private static PerformanceSummary ErrorSummary(string engine, string scenario, string? error) =>
        new(engine, scenario, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty, error ?? "Unknown error");

    private static double Median(double[] sorted)
    {
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2d : sorted[middle];
    }

    private static long MedianLong(IEnumerable<long> values)
    {
        var sorted = values.Order().Select(value => (double)value).ToArray();
        return (long)Median(sorted);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var index = Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1);
        return sorted[index];
    }

    private static void SampleHeap(StrongBox<long> peak, Func<bool> running)
    {
        while (running())
        {
            ConsiderPeak(peak, GC.GetTotalMemory(false));
            Thread.Sleep(1);
        }
    }

    private static void ConsiderPeak(StrongBox<long> peak, long current)
    {
        var observed = Interlocked.Read(ref peak.Value);
        while (current > observed)
        {
            var previous = Interlocked.CompareExchange(ref peak.Value, current, observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public static async Task<IReadOnlyList<string>> CaptureOutputs(
        IReadOnlyList<EngineAdapter> adapters,
        string outputFolder,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var paths = new List<string>();
        var fixture = Path.Combine(outputFolder, "fixture-text-200.pdf");
        await File.WriteAllBytesAsync(fixture, ContentFixtures.Text(200), cancellationToken);
        paths.Add(fixture);
        var mergeFixture = Path.Combine(outputFolder, "fixture-merge-source.pdf");
        var mergeSource = ContentFixtures.Text(10);
        await File.WriteAllBytesAsync(mergeFixture, mergeSource, cancellationToken);
        paths.Add(mergeFixture);
        var sources = Enumerable.Range(0, 10).Select(_ => mergeSource).ToArray();

        foreach (var adapter in adapters)
        {
            if (adapter.SupportsCreation)
            {
                var path = Path.Combine(outputFolder, adapter.Name + "-create.pdf");
                await adapter.CreateAsync(10, 20, cancellationToken, path);
                paths.Add(path);
            }

            if (adapter.SupportsMerge)
            {
                var path = Path.Combine(outputFolder, adapter.Name + "-merge.pdf");
                await adapter.MergeAsync(sources, cancellationToken, path);
                paths.Add(path);
            }
        }

        return paths;
    }
}
