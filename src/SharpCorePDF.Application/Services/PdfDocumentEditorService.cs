using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;

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
}
