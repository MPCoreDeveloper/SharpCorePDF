using System.Runtime.InteropServices;

namespace SharpCorePDF.Competitive;

internal sealed record EngineInfo(string Name, string Version);
internal sealed record EnvironmentInfo(
    string Runtime,
    string OperatingSystem,
    string Architecture,
    int LogicalProcessors,
    bool ServerGarbageCollection,
    int Warmups,
    int Iterations)
{
    public static EnvironmentInfo Capture(PerformanceOptions options) => new(
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        Environment.ProcessorCount,
        System.Runtime.GCSettings.IsServerGC,
        options.Warmups,
        options.Iterations);
}

internal sealed record BenchmarkReport(
    DateTimeOffset GeneratedUtc,
    EnvironmentInfo Environment,
    IReadOnlyList<EngineInfo> Engines,
    IReadOnlyList<CorrectnessResult> Correctness,
    IReadOnlyList<PerformanceSample> PerformanceSamples,
    IReadOnlyList<PerformanceSummary> Performance,
    IReadOnlyList<RobustnessResult> Robustness);
