using System.Diagnostics;
using SharpCorePDF.Examples;
using SharpCorePDF.Domain.Exceptions;

// Runs the examples of SharpCorePDF:
//
//   dotnet run --project .\examples\SharpCorePDF.Examples                  every example
//   dotnet run --project .\examples\SharpCorePDF.Examples -- list          what there is to run
//   dotnet run --project .\examples\SharpCorePDF.Examples -- select-pages  one example by name
//   dotnet run --project .\examples\SharpCorePDF.Examples -- errors options  several, in the order given
//
// Every example writes its documents to a folder under the temporary directory and prints where, so the
// merged files can be opened in a viewer afterwards. The exit code is 1 when an example failed.
using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    // Ctrl+C cancels the merge in progress instead of killing the process, which is what a caller with a
    // CancellationToken does in a service.
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (Scenarios.IsHelp(args))
{
    Scenarios.PrintUsage();
    return 0;
}

if (Scenarios.IsList(args))
{
    Scenarios.PrintList();
    return 0;
}

var selected = Scenarios.Resolve(args, out var unknown);

if (unknown.Count > 0)
{
    Console.WriteLine($"Unknown example: {string.Join(", ", unknown)}");
    Console.WriteLine();
    Scenarios.PrintList();
    return 2;
}

using var context = ExampleContext.Create();

Console.WriteLine($"SharpCorePDF examples");
Console.WriteLine($"  documents  {context.InputFolder}");
Console.WriteLine($"  output     {context.OutputFolder}");

var failures = 0;

foreach (var scenario in selected)
{
    Console.WriteLine();
    Console.WriteLine($"=== {scenario.Id}: {scenario.Title}");

    var started = Stopwatch.GetTimestamp();

    try
    {
        await scenario.Run(context, cancellation.Token);
        Console.WriteLine($"--- ok ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms)");
    }
    catch (PdfException exception)
    {
        // Everything the library throws derives from PdfException, so one catch block can report every
        // refusal with the type and the message that produced it.
        failures++;
        Console.WriteLine($"--- failed: {exception.GetType().Name}: {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? $"All {selected.Count} example(s) ran. Merged documents are in {context.OutputFolder}"
    : $"{failures} of {selected.Count} example(s) failed.");

return failures == 0 ? 0 : 1;
