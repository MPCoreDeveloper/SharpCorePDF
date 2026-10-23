using System.Diagnostics;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The editing use case: validate the plan, open and inspect the source, hand the work to the engine,
/// and make sure the source stream is released no matter how the edit ends.
/// </summary>
/// <remarks>
/// The class is stateless and thread safe, which is why a single instance can be registered as a
/// singleton in a dependency injection container.
/// </remarks>
public sealed class PdfDocumentEditorService : IPdfDocumentEditor
{
    private readonly IPdfDocumentReader reader;
    private readonly IPdfEditEngine engine;

    /// <summary>Initializes a new instance of the <see cref="PdfDocumentEditorService"/> class.</summary>
    /// <param name="reader">The reader used to inspect the source.</param>
    /// <param name="engine">The engine that writes the edited document.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PdfDocumentEditorService(IPdfDocumentReader reader, IPdfEditEngine engine)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc />
    public async Task<PdfEditResult> ApplyAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        plan.EnsureValid();

        var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var info = await reader
                .ReadAsync(source.DisplayName, content, password, cancellationToken)
                .ConfigureAwait(false);

            if (!info.IsReadable)
            {
                // An encrypted document the reader could not open is described rather than thrown
                // from, so the decision to stop is made here: either a password is what stands in
                // the way and the caller can ask for one, or nothing would help.
                throw new PdfEncryptionException(source.DisplayName, info.RequiresPassword);
            }

            // The engine reads the same stream from the start, so the position the reader left
            // behind must not leak into the edit.
            if (content.CanSeek && content.Position != 0)
            {
                content.Position = 0;
            }

            return await engine
                .ApplyAsync(source.DisplayName, content, plan, password, destination, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await content.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ApplyToArrayAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        using var buffer = new MemoryStream();
        await ApplyAsync(source, plan, buffer, password, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <inheritdoc />
    public async Task<PdfSplitResult> SplitAsync(
        IPdfContentSource source,
        PdfEditPlan plan,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        plan.EnsureValid();

        var splits = plan.Operations.OfType<PdfSplitDocumentOperation>().ToArray();

        if (splits.Length != 1)
        {
            throw new PdfEditPlanValidationException(
                splits.Length == 0
                    ? "the plan does not contain a SplitDocument operation, so there is nothing to split; add one with SplitDocument(pagesPerDocument)."
                    : $"the plan contains {splits.Length} SplitDocument operations, but one document can only be split once per plan.");
        }

        var started = Stopwatch.GetTimestamp();
        var pagesPerDocument = splits[0].PagesPerDocument;

        // Every other operation runs first, on the whole document. The part plans then only cut the
        // result, so the work of the edit is not repeated for every part.
        var editedPlan = plan.Operations
            .Where(operation => operation is not PdfSplitDocumentOperation)
            .Aggregate(PdfEditPlan.For(plan.Output), (current, operation) => current.AddOperation(operation));

        IPdfContentSource splitSource = source;
        var warnings = new List<PdfWarning>();
        var editedOperations = Array.Empty<string>();

        if (!editedPlan.IsEmpty)
        {
            using var editedBuffer = new MemoryStream();
            var editedResult = await ApplyAsync(source, editedPlan, editedBuffer, password, cancellationToken)
                .ConfigureAwait(false);

            splitSource = new MemorySource(editedBuffer.ToArray(), source.DisplayName);
            warnings.AddRange(editedResult.Warnings);
            editedOperations = [.. editedResult.Operations];
        }

        var info = await InspectAsync(splitSource, password, cancellationToken).ConfigureAwait(false);
        warnings.AddRange(info.Warnings);

        var parts = new List<PdfSplitPart>();

        for (var start = 1; start <= info.PageCount; start += pagesPerDocument)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var end = Math.Min(start + pagesPerDocument - 1, info.PageCount);
            var pages = PageRange.FromTo(start, end);
            var partPlan = PdfEditPlan.For(plan.Output).ExtractPages(pages);

            using var partBuffer = new MemoryStream();
            var partResult = await ApplyAsync(splitSource, partPlan, partBuffer, password, cancellationToken)
                .ConfigureAwait(false);

            if (editedOperations.Length > 0)
            {
                partResult = partResult with { Operations = [.. editedOperations, .. partResult.Operations] };
            }

            parts.Add(new PdfSplitPart
            {
                PartNumber = parts.Count + 1,
                Pages = pages,
                Content = partBuffer.ToArray(),
                Result = partResult,
            });

            warnings.AddRange(partResult.Warnings);
        }

        return new PdfSplitResult
        {
            Parts = parts,
            Elapsed = Stopwatch.GetElapsedTime(started),
            Warnings = [.. warnings.Distinct()],
        };
    }

    /// <summary>Inspects a source without applying any operation to it.</summary>
    /// <param name="source">The document to inspect.</param>
    /// <param name="password">The user or owner password, when the source is encrypted.</param>
    /// <param name="cancellationToken">A token to cancel the inspection.</param>
    /// <returns>The description of the document.</returns>
    /// <exception cref="PdfEncryptionException">The source is encrypted and could not be opened.</exception>
    private async Task<PdfDocumentInfo> InspectAsync(
        IPdfContentSource source,
        string? password,
        CancellationToken cancellationToken)
    {
        await using var content = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        var info = await reader
            .ReadAsync(source.DisplayName, content, password, cancellationToken)
            .ConfigureAwait(false);

        if (!info.IsReadable)
        {
            throw new PdfEncryptionException(source.DisplayName, info.RequiresPassword);
        }

        return info;
    }

    /// <summary>A source over bytes that the split produced itself.</summary>
    private sealed class MemorySource(byte[] content, string displayName) : IPdfContentSource
    {
        /// <inheritdoc />
        public string DisplayName => displayName;

        /// <inheritdoc />
        public long? Length => content.Length;

        /// <inheritdoc />
        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false));
    }
}
