using System.Diagnostics;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The engine's implementation of <see cref="IPdfEditEngine"/>: it reads one document, copies the
/// objects its remaining pages need into a new document, applies the page and metadata operations of
/// the plan, and writes that document with a classic cross-reference table.
/// </summary>
/// <remarks>
/// <para>
/// Stream bytes are never re-compressed: page content, images and fonts are copied verbatim. The page
/// tree is flattened, exactly as it is for a merge, so inherited <c>MediaBox</c>, <c>CropBox</c>,
/// <c>Resources</c> and <c>Rotate</c> travel with each page.
/// </para>
/// <para>
/// The source is only ever read. The result is a complete rewrite, which keeps earlier revisions and
/// signatures out of a document whose pages have changed; incremental updates belong to the signing
/// work, not to an ordinary edit.
/// </para>
/// </remarks>
public sealed partial class PdfEditEngine : IPdfEditEngine
{
    private readonly PdfEngineOptions options;

    /// <summary>Initializes a new instance of the <see cref="PdfEditEngine"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfEditEngine(PdfEngineOptions? options = null) => this.options = options ?? PdfEngineOptions.Default;

    /// <summary>Gets the options this engine was created with.</summary>
    public PdfEngineOptions Options => options;

    /// <inheritdoc />
    public async Task<PdfEditResult> ApplyAsync(
        string sourceName,
        Stream content,
        PdfEditPlan plan,
        string? password,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        var problems = options.Validate();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The engine options are not usable: {string.Join(" ", problems)}");
        }

        plan.EnsureValid();

        var started = Stopwatch.GetTimestamp();
        var bytes = await PdfByteReader
            .ReadAllAsync(content, options.MaxSourceBytes, sourceName, cancellationToken)
            .ConfigureAwait(false);

        var reader = new PdfFileReader(bytes, sourceName, options, password);

        if (reader.IsEncrypted && reader.Security is null)
        {
            throw new PdfEncryptionException(sourceName, reader.RequiresPassword);
        }

        if (reader.Pages.Count == 0)
        {
            throw new PdfSourceFormatException(
                sourceName,
                "the document has no page tree, so it cannot be edited.");
        }

        var warnings = new List<PdfWarning>(reader.Warnings);
        var layout = BuildLayout(reader, plan, warnings, out var layoutChanged);

        if (layout.Count == 0)
        {
            throw new PdfEditPlanValidationException(
                $"the edit removes every page of '{sourceName}', and a PDF document must keep at least one page.");
        }

        var modificationDate = plan.Output.ModificationDate ?? DateTimeOffset.UtcNow;
        var identifier = BuildFileIdentifier(plan, layout.Count, modificationDate);

        // The capture is disposed with the edit: it owns a pooled buffer, and the destination it
        // forwards to belongs to the caller and is left open.
        using var capture = options.VerifyOutput ? new PdfCaptureStream(destination) : null;
        var writer = new PdfWriter(capture ?? destination, plan.Output.TargetVersion, options);
        var catalogNumber = writer.AllocateObject();
        var pagesNodeNumber = writer.AllocateObject();
        var copier = new PdfObjectCopier(reader, writer);

        RedirectPageTree(reader, copier, pagesNodeNumber);
        AllocatePageTargets(writer, layout);
        RedirectSourcePages(reader, layout, copier);
        WritePages(reader, copier, writer, pagesNodeNumber, layout);

        var catalog = BuildCatalog(reader, copier, pagesNodeNumber, plan, layoutChanged, warnings);
        writer.WriteObject(catalogNumber, catalog);
        WritePagesNode(writer, pagesNodeNumber, layout);

        // Resources, contents and annotations were reserved while the pages were built; this is where
        // their bodies are written.
        copier.Flush();

        var infoNumber = WriteInformation(reader, plan, writer, modificationDate);
        WriteTrailer(writer, catalogNumber, infoNumber, identifier);

        Verify(capture, layout, warnings);

        return new PdfEditResult
        {
            PageCount = layout.Count,
            BytesWritten = writer.Length,
            OutputVersion = plan.Output.TargetVersion,
            Elapsed = Stopwatch.GetElapsedTime(started),
            Operations = [.. plan.Operations.Select(operation => operation.ToString())],
            Warnings = warnings,
        };
    }

    /// <summary>Reads the written document back and confirms its page count and rotations.</summary>
    /// <param name="capture">The capture of the output, when verification is enabled.</param>
    /// <param name="layout">The pages that were written, in order.</param>
    /// <param name="warnings">The list that receives the verification remark.</param>
    /// <exception cref="InvalidOperationException">The document that was written cannot be read back, or does not match the plan.</exception>
    private void Verify(PdfCaptureStream? capture, List<EditedPage> layout, List<PdfWarning> warnings)
    {
        if (capture is not { } recorded)
        {
            return;
        }

        try
        {
            var reader = new PdfFileReader(recorded.CapturedBytes, "edited document", options);

            if (reader.Pages.Count != layout.Count)
            {
                throw new InvalidOperationException(
                    $"The edited document reports {reader.Pages.Count} page(s) but {layout.Count} page(s) were written.");
            }

            for (var index = 0; index < layout.Count; index++)
            {
                var expected = layout[index].EffectiveRotation;
                var actual = ReadRotation(reader.Pages[index]);

                if (actual != expected)
                {
                    throw new InvalidOperationException(
                        $"Page {index + 1} of the edited document is rotated {actual.Degrees} degrees but {expected.Degrees} degrees were written.");
                }
            }

            warnings.Add(new(
                Domain.ValueObjects.PdfWarningKind.OutputVerified,
                $"the edited document was read back and contains {reader.Pages.Count} page(s), as expected."));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The edited document could not be read back: {exception.Message}",
                exception);
        }
    }
}
