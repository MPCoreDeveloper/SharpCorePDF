using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;

namespace SharpCorePDF.Tests;

/// <summary>
/// The plumbing the merge tests share: merging a plan and reading the result back.
/// </summary>
public sealed partial class PdfMergeEngineTests
{
    /// <summary>Merges a plan and returns everything the assertions need.</summary>
    /// <param name="plan">The plan to execute.</param>
    /// <param name="output">The output options to use instead of the ones of the plan.</param>
    /// <returns>The bytes, the description and the result of the merge.</returns>
    private static async Task<Merged> MergeAsync(MergePlan plan, MergeOutputOptions? output = null)
    {
        var effective = output is null ? plan : plan.WithOutput(output);

        using var buffer = new MemoryStream();
        var result = await Merger.MergeAsync(effective, buffer, TestContext.Current.CancellationToken);
        var raw = buffer.ToArray();

        var document = await Reader.ReadAsync(
            "merged.pdf",
            new MemoryStream(raw),
            password: null,
            TestContext.Current.CancellationToken);

        return new Merged(raw, document, result);
    }

    /// <summary>The three views of a merge that the tests assert on.</summary>
    /// <param name="Raw">The bytes of the merged document.</param>
    /// <param name="Document">The merged document as described by the reader.</param>
    /// <param name="Result">What the merge reported.</param>
    private sealed record Merged(byte[] Raw, PdfDocumentInfo Document, MergeResult Result);
}
