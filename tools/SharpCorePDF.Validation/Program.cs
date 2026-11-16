using SharpCorePDF.Domain.ValueObjects;
using SharpFuzz;

namespace SharpCorePDF.Validation;

/// <summary>
/// The entry point of the validation harness.
/// </summary>
/// <remarks>
/// <para>
/// <c>dotnet run --project tools/SharpCorePDF.Validation -- corpus &lt;dir&gt;</c> writes the validation corpus,
/// <c>-- probe &lt;file.pdf&gt; [--password p] [--profile 1b|2b|2u|3b|3u|4 [--convert out.pdf] [--preflight-only]]</c>
/// reads one document,
/// <c>-- replay &lt;target&gt; &lt;file-or-dir&gt;...</c> runs a fuzz target over saved inputs, and
/// <c>-- seed &lt;target&gt; &lt;dir&gt; &lt;file&gt;...</c> wraps sample files into seeds for a target. With no
/// argument and <c>SCPDF_FUZZ_TARGET</c> set, the process belongs to libFuzzer: libfuzzer-dotnet starts it as
/// <c>dotnet SharpCorePDF.Validation.dll</c> with nothing else on the command line, so the target can only be
/// named through the environment. With <c>SCPDF_FUZZ_TARGET</c> set and one existing file as the argument,
/// SharpFuzz runs the target once on that file, which is how a crash is reproduced on the instrumented build.
/// </para>
/// <para>
/// <c>replay</c> has to run on a build that was not instrumented: an instrumented assembly records coverage
/// in the trace buffer SharpFuzz only creates inside <see cref="Fuzzer.LibFuzzer.Run(ReadOnlySpanAction)"/>, and
/// outside it the first instrumented branch throws.
/// </para>
/// </remarks>
internal static class Program
{
    private const string FuzzTargetVariable = "SCPDF_FUZZ_TARGET";

    private static async Task<int> Main(string[] args)
    {
        if ((args.Length == 0 || (args.Length == 1 && File.Exists(args[0])))
            && Environment.GetEnvironmentVariable(FuzzTargetVariable) is { Length: > 0 } target)
        {
            if (!FuzzTargets.IsKnown(target))
            {
                await Console.Error.WriteLineAsync($"{FuzzTargetVariable} names no target: {target}")
                    .ConfigureAwait(false);
                return 1;
            }

            Fuzzer.LibFuzzer.Run(input => FuzzTargets.Run(target, input));
            return 0;
        }

        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        return args[0] switch
        {
            "corpus" when args.Length == 2 => await CorpusWriter.WriteAsync(args[1]).ConfigureAwait(false),
            "probe" when args.Length >= 2 => await ProbeAsync(args[1..]).ConfigureAwait(false),
            "replay" when args.Length >= 3 => FuzzTargets.Replay(args[1], args[2..]),
            "seed" when args.Length >= 4 => FuzzTargets.Seed(args[1], args[2], args[3..]),
            _ => Usage(),
        };
    }

    private static async Task<int> ProbeAsync(string[] args)
    {
        string? path = null;
        string? password = null;
        string? convert = null;
        var preflightOnly = false;
        PdfAProfile? profile = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--password" when index + 1 < args.Length:
                    password = args[++index];
                    break;

                case "--convert" when index + 1 < args.Length:
                    convert = args[++index];
                    break;

                case "--preflight-only":
                    preflightOnly = true;
                    break;

                case "--profile" when index + 1 < args.Length:
                    profile = Probe.ParseProfile(args[++index]);

                    if (profile is null)
                    {
                        return Usage();
                    }

                    break;

                default:
                    if (path is not null || args[index].StartsWith('-'))
                    {
                        return Usage();
                    }

                    path = args[index];
                    break;
            }
        }

        if (path is null || (profile is null && (convert is not null || preflightOnly)))
        {
            return Usage();
        }

        return await Probe.RunAsync(path, password, profile, convert, preflightOnly).ConfigureAwait(false);
    }

    private static int Usage()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("usage:");
        Console.WriteLine("  SharpCorePDF.Validation corpus <dir>");
        Console.WriteLine(
            "  SharpCorePDF.Validation probe <file.pdf> [--password <p>] [--profile 1b|2b|2u|3b|3u|4 [--convert <out.pdf>] [--preflight-only]]");
        Console.WriteLine("  SharpCorePDF.Validation replay <target> <file-or-dir>...");
        Console.WriteLine("  SharpCorePDF.Validation seed <target> <dir> <file>...");
        Console.WriteLine(
            $"  {FuzzTargetVariable}=<target> libfuzzer-dotnet --target_path=dotnet --target_arg=SharpCorePDF.Validation.dll <corpus>");
        Console.WriteLine();
        Console.WriteLine($"targets: {string.Join(", ", FuzzTargets.Names)}");
        Console.WriteLine();
        Console.WriteLine(
            "probe exit codes: 0 read, 2 refused with a PdfException, 3 engine fault (any other exception)");
    }
}