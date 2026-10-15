using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// The failure and reproducibility half of the merge tests.
/// </summary>
public sealed partial class PdfMergeEngineTests
{
    /// <summary>A fixed modification date makes the output reproducible, byte for byte.</summary>
    [Fact]
    public async Task Writes_the_same_bytes_for_the_same_input()
    {
        var output = MergeOutputOptions.Default with
        {
            ModificationDate = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
        };

        var plan = MergePlan.Create().AddFile(TestAssets.Outlined);

        var first = await MergeAsync(plan, output);
        var second = await MergeAsync(plan, output);

        Assert.Equal(first.Raw, second.Raw);
    }

    /// <summary>An encrypted source is refused with a domain exception.</summary>
    [Fact]
    public async Task Refuses_an_encrypted_source()
    {
        var plan = MergePlan.Create().AddFile(TestAssets.Encrypted);

        var exception = await Assert.ThrowsAsync<PdfEncryptionException>(
            () => Merger.MergeToBytesAsync(plan, TestContext.Current.CancellationToken));

        Assert.Equal("Encrypted.pdf", exception.SourceName);
    }

    /// <summary>A selection that reaches past the last page is refused before anything is written.</summary>
    [Fact]
    public async Task Refuses_a_selection_that_reaches_beyond_the_document()
    {
        var plan = MergePlan.Create().AddFile(TestAssets.Modern, pages: "5");

        await Assert.ThrowsAsync<MergePlanValidationException>(
            () => Merger.MergeToBytesAsync(plan, TestContext.Current.CancellationToken));
    }

    /// <summary>The warnings of a merge reach the caller through the result.</summary>
    [Fact]
    public async Task Reports_what_it_had_to_change()
    {
        var merged = await MergeAsync(
            MergePlan.Create().AddFile(TestAssets.Outlined),
            MergeOutputOptions.Default with { OutlinePolicy = OutlinePolicy.Drop });

        Assert.Contains(
            merged.Result.Warnings,
            warning => warning.Contains("bookmark", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            merged.Result.Warnings,
            warning => warning.Contains("read back", StringComparison.OrdinalIgnoreCase));
        Assert.False(merged.Document.HasOutlines);
    }

    /// <summary>Reads the page numbers a destination array of a bookmark points at.</summary>
    /// <param name="document">The merged document.</param>
    /// <param name="bookmark">The bookmark to read.</param>
    /// <returns>The one-based page numbers of the destination, in the order they appear.</returns>
    private static int[] ReadDestinationPages(PdfFileReader document, PdfDictionary bookmark)
    {
        var destination = document.ResolveArray(bookmark.Get("Dest"));
        var pages = document.Pages.Select(page => page.ObjectNumber).ToList();
        var numbers = new List<int>();

        foreach (var value in destination?.Items ?? [])
        {
            if (value is not PdfReference reference)
            {
                continue;
            }

            var index = pages.IndexOf(reference.ObjectNumber);

            if (index >= 0)
            {
                numbers.Add(index + 1);
            }
        }

        return [.. numbers];
    }
}
