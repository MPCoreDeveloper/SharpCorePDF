using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Extensions;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Examples;

/// <summary>
/// Where the bytes of a source come from: a stream, a byte array, or a plain class instead of a container.
/// </summary>
internal static class SourceExamples
{
    /// <summary>Merges an uploaded stream and a document held in memory, and hands the result back as bytes.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    public static async Task MergeStreamsAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // The shape a web endpoint needs: the upload arrives as a stream, the download leaves as bytes, and
        // no temporary file is involved. A stream source captures its stream once, on first read, and then
        // behaves like any other source, so the same document can be inspected and merged.
        await using var upload = File.OpenRead(context.Appendix);

        var plan = MergePlan.Create()
            .AddStream(upload, "appendix-upload")
            .AddBytes(await File.ReadAllBytesAsync(context.Annex, cancellationToken), "annex-in-memory", pages: "1");

        var bytes = await context.Merger.MergeToBytesAsync(plan, cancellationToken);
        var destination = context.OutputPath("from-streams.pdf");
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);

        Report.Item("wrote", destination);
        Report.Item("bytes", bytes.LongLength);
        Report.Item("fingerprint", Report.Fingerprint(bytes));

        // When every page of every source is wanted, the plan is not needed at all: a sequence of content
        // sources is enough.
        var sources = new List<IPdfContentSource>
        {
            new PdfFileSource(context.Summary),
            new PdfFileSource(context.Appendix, "the appendix"),
        };

        using var buffer = new MemoryStream();
        var result = await context.Merger.MergeAsync(sources, buffer, cancellationToken);

        Report.Item("pages without a plan", result.PageCount);
        Report.Item("sources", string.Join(", ", result.Sources.Select(source => source.SourceName)));
    }

    /// <summary>Does the same merge with plain objects, for a caller who does not use a container.</summary>
    /// <param name="context">The documents to merge and the ports to merge them with.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    public static async Task WithoutContainerAsync(ExampleContext context, CancellationToken cancellationToken)
    {
        // AddSharpCorePDF() only saves typing: the reader, the engine and the merger are ordinary classes
        // with constructor parameters, and the ports are the same ones. Nothing below needs a container.
        var options = PdfEngineOptions.Default with { VerifyOutput = true };
        var merger = new PdfMergerService(new PdfDocumentReader(options), new PdfMergeEngine(options));

        // The modification date is the one detail that would otherwise differ between two runs, so it is
        // fixed here; that is what makes the result reproducible byte for byte.
        var plan = MergePlan.Create()
            .AddFile(context.Summary)
            .AddFile(context.Appendix, pages: "1-2")
            .WithOutput(output => output with { ModificationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) });

        using var buffer = new MemoryStream();
        var result = await merger.MergeAsync(plan, buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var destination = context.OutputPath("no-container.pdf");
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);

        Report.Item("wrote", destination);
        Report.Item("pages", result.PageCount);
        Report.Item("bytes", result.OutputBytes);
        Report.Item("took", $"{result.Duration.TotalMilliseconds:0} ms");
        Report.Item("fingerprint", Report.Fingerprint(bytes));

        // The same plan through the container this app registered from appsettings.json. The two documents
        // must be identical: the container only decides how the objects are built.
        using var throughContainer = new MemoryStream();
        var wired = await context.Merger.MergeAsync(plan, throughContainer, cancellationToken);

        Report.Item("pages via the container", wired.PageCount);
        Report.Item("same bytes as without a container", throughContainer.ToArray().AsSpan().SequenceEqual(bytes));

        var info = await context.Inspector.InspectAsync(
            PdfFileSource.From(destination),
            cancellationToken: cancellationToken);

        Report.Item("read back", info);
    }
}
