using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;

namespace SharpCorePDF.Examples;

/// <summary>
/// The ways a merge can be refused, and how to tell them apart without matching on a message.
/// </summary>
internal static class ErrorHandlingExamples
{
    /// <summary>Walks through every refusal a caller can meet, oldest first.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merges.</param>
    public static async Task HandleRefusalsAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // A plan that cannot work is refused before a single file is opened. Validate() collects the problems
        // instead of throwing, which is what a configuration screen wants; EnsureValid() throws them.
        Report.Heading("a plan without sources");

        var empty = MergePlan.Create();

        foreach (var problem in empty.Validate())
        {
            Report.Line(problem);
        }

        await ExpectAsync(async () => await context.Merger.MergeToBytesAsync(empty, cancellationToken));

        // A selection that is not page notation is a programming error, so it throws where it is written and
        // not as a PdfException: string matching is never needed to tell the two apart.
        Report.Heading("a selection that is not page notation");

        try
        {
            _ = MergePlan.Create().AddFile(context.Summary, pages: "1-3-5");
        }
        catch (FormatException exception)
        {
            Report.Item("FormatException", exception.Message);
        }

        // A page that does not exist can only be detected once the document has been read, so the plan looks
        // valid and the merge is what refuses it.
        Report.Heading("a page that does not exist");

        var beyond = MergePlan.Create().AddFile(context.Summary, pages: "9");
        Report.Item("plan.Validate()", beyond.Validate().Count == 0 ? "no problems yet" : "problems");

        await ExpectAsync(async () => await context.Merger.MergeToBytesAsync(beyond, cancellationToken));

        // The version of the result is checked when the plan is validated, which is where a configuration
        // mistake should surface: before the sources are read.
        Report.Heading("a target version the engine cannot write");

        var tooOld = MergePlan.Create()
            .AddFile(context.Summary)
            .WithOutput(output => output with { TargetVersion = new PdfVersion(1, 3) });

        Report.Item("plan problems", string.Join(" | ", tooOld.Validate()));

        // The input's fault: an encrypted document whose password was not given, and a file that is not a PDF.
        Report.Heading("a source the engine cannot read");

        var encrypted = MergePlan.Create().AddFile(Path.Combine(context.InputFolder, "Encrypted.pdf"));
        var notAPdf = MergePlan.Create().AddFile(Path.Combine(context.InputFolder, "Broken.pdf"));

        await ExpectAsync(async () => await context.Merger.MergeToBytesAsync(encrypted, cancellationToken));
        await ExpectAsync(async () => await context.Merger.MergeToBytesAsync(notAPdf, cancellationToken));

        // With the password the engine opens the document, and the merge it was refused for runs.
        Report.Heading("the same source, merged with its password");

        var unlocked = MergePlan.Create()
            .AddFile(Path.Combine(context.InputFolder, "Encrypted.pdf"), password: SampleDocuments.EncryptedPassword);

        var result = await context.Merger.MergeToFileAsync(
            unlocked,
            context.OutputPath("unlocked.pdf"),
            cancellationToken);

        Report.Item("pages", result.PageCount);
        Report.Item("bytes", result.OutputBytes);
        Report.Item("warnings", string.Join(" | ", result.Warnings));

        // A destination that cannot be written is the caller's fault too, and reported as such.
        Report.Heading("a destination that cannot be written");

        var plan = MergePlan.Create().AddFile(context.Summary);
        using var readOnly = new MemoryStream([], writable: false);

        try
        {
            await context.Merger.MergeAsync(plan, readOnly, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            Report.Item("ArgumentException", exception.Message);
        }

        Report.Heading("the two families of error");
        Report.Line("ArgumentNullException, ArgumentException and FormatException: the caller built it wrong.");
        Report.Line("MergePlanValidationException: the plan does not describe a merge that can be executed.");
        Report.Line("PdfSourceFormatException, PdfEncryptionException: the input cannot be merged. A file that is not a PDF, or an encrypted document whose password was not given.");
        Report.Line("All of the last three derive from PdfException, so one catch block sees them all.");
    }

    /// <summary>Runs an operation that is expected to be refused and prints how the library refuses it.</summary>
    /// <param name="operation">The operation to run.</param>
    private static async Task ExpectAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            Report.Line("no exception was thrown");
        }
        catch (PdfException exception)
        {
            Report.Item(exception.GetType().Name, exception.Message);
        }
    }
}
