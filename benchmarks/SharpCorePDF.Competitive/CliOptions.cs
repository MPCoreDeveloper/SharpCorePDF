using System.Globalization;

namespace SharpCorePDF.Competitive;

internal sealed record CliOptions(
    string? Output,
    int Warmups,
    int Iterations,
    bool SkipCorrectness,
    bool SkipPerformance,
    bool Capture,
    string? RobustnessFolder,
    string? RobustnessLock,
    bool WriteRobustnessLock,
    int RobustnessLimit,
    long RobustnessMaxBytes,
    double RobustnessTimeoutSeconds)
{
    public static CliOptions Parse(string[] args)
    {
        string? output = null;
        var warmups = 2;
        var iterations = 5;
        var skipCorrectness = false;
        var skipPerformance = false;
        var capture = false;
        string? robustness = null;
        string? robustnessLock = null;
        var writeRobustnessLock = false;
        var limit = 50;
        var maxBytes = 5L * 1024 * 1024;
        var timeoutSeconds = 5d;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--output":
                    output = Require(args, ref index, argument);
                    break;
                case "--warmups":
                    warmups = int.Parse(Require(args, ref index, argument), CultureInfo.InvariantCulture);
                    break;
                case "--iterations":
                    iterations = int.Parse(Require(args, ref index, argument), CultureInfo.InvariantCulture);
                    break;
                case "--skip-correctness":
                    skipCorrectness = true;
                    break;
                case "--skip-performance":
                    skipPerformance = true;
                    break;
                case "--capture":
                    capture = true;
                    break;
                case "--robustness":
                    robustness = Require(args, ref index, argument);
                    break;
                case "--robustness-lock":
                    robustnessLock = Require(args, ref index, argument);
                    break;
                case "--write-robustness-lock":
                    writeRobustnessLock = true;
                    break;
                case "--robustness-limit":
                    limit = int.Parse(Require(args, ref index, argument), CultureInfo.InvariantCulture);
                    break;
                case "--robustness-max-bytes":
                    maxBytes = long.Parse(Require(args, ref index, argument), CultureInfo.InvariantCulture);
                    break;
                case "--robustness-timeout-seconds":
                    timeoutSeconds = double.Parse(Require(args, ref index, argument), CultureInfo.InvariantCulture);
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'. Pass --help for usage.");
            }
        }

        if (warmups < 0 || iterations < 1)
        {
            throw new ArgumentException("Warm-ups must be non-negative and iterations must be at least one.");
        }

        return new(
            output,
            warmups,
            iterations,
            skipCorrectness,
            skipPerformance,
            capture,
            robustness,
            robustnessLock,
            writeRobustnessLock,
            limit,
            maxBytes,
            timeoutSeconds);
    }

    private static string Require(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Argument {name} requires a value.");
        }

        index++;
        return args[index];
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            SharpCorePDF competitive harness — isolated comparison project, not part of the product packages.

              dotnet run -c Release --project .\benchmarks\SharpCorePDF.Competitive -- [options]

            Options:
              --output <dir>                     Report folder (default: artifacts/competitive)
              --warmups <n>                      Warm-ups per engine/scenario (default: 2)
              --iterations <n>                   Measured iterations (default: 5)
              --skip-correctness                 Skip labelled extraction scoring
              --skip-performance                 Skip timed scenarios
              --capture                          Write one unmeasured create/merge PDF per writer
              --robustness <folder>              Optional local PDF corpus for open/extract completion
              --robustness-lock <file>           SHA-256 manifest the corpus must match (hash-locked run)
              --write-robustness-lock            Write the manifest named by --robustness-lock from the folder first
              --robustness-limit <n>             Maximum files from the folder (default: 50)
              --robustness-max-bytes <n>         Skip larger files (default: 5242880)
              --robustness-timeout-seconds <n>   Per-file timeout; a timeout stops the robustness leg
              --help                             Show this text

            Read benchmarks/SharpCorePDF.Competitive/THIRD-PARTY.md before redistributing this harness.
            """);
    }
}
