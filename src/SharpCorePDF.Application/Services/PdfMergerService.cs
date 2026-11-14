using System.Diagnostics;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The merging use case: validate the plan, open and inspect every source, resolve the page
/// selections against the real documents, hand the work to the engine, and make sure every stream is
/// released no matter how the merge ends.
/// </summary>
/// <remarks>
/// The class is stateless and thread safe, which is why a single instance can be registered as a
/// singleton in a dependency injection container.
/// </remarks>
public sealed class PdfMergerService : IPdfMerger
{
    private readonly IPdfDocumentReader reader;
    private readonly IPdfDocumentBytesReader? bytesReader;
    private readonly IPdfMergeEngine engine;

    /// <summary>Initializes a new instance of the <see cref="PdfMergerService"/> class.</summary>
    /// <param name="reader">The reader used to inspect the sources.</param>
    /// <param name="engine">The engine that writes the merged document.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// A reader that also implements <see cref="IPdfDocumentBytesReader"/> is preferred, because the merge
    /// then hands the engine the bytes the inspection produced instead of reading each source twice
    /// ([ADR-004](../../../docs/adr/0004-buffer-model.md)). A reader that does not is still accepted, and the
    /// engine reads the source itself, exactly as before.
    /// </remarks>
    public PdfMergerService(IPdfDocumentReader reader, IPdfMergeEngine engine)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        bytesReader = reader as IPdfDocumentBytesReader;
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc />
    public async Task<MergeResult> MergeAsync(
        MergePlan plan,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        plan.EnsureValid();

        var started = Stopwatch.GetTimestamp();
        var openStreams = new List<Stream>(plan.SourceCount);

        try
        {
            var resolved = new List<ResolvedPdfSource>(plan.SourceCount);

            foreach (var source in plan.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var content = await source.Content.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                openStreams.Add(content);

                // What the engine reads. When the reader can hand its bytes back, the document is read once
                // and the engine reads it out of that array; otherwise the stream is rewound and read again.
                var engineContent = content;
                PdfDocumentInfo info;

                if (bytesReader is not null)
                {
                    var read = await bytesReader
                        .ReadWithBytesAsync(source.DisplayName, content, source.Password, cancellationToken)
                        .ConfigureAwait(false);

                    info = read.Info;
                    engineContent = read.OpenRead();
                    openStreams.Add(engineContent);
                }
                else
                {
                    info = await reader
                        .ReadAsync(source.DisplayName, content, source.Password, cancellationToken)
                        .ConfigureAwait(false);

                    // The engine reads the same stream from the start, so the position the reader left
                    // behind must not leak into the merge. Our own engine rewinds as well, but the port a
                    // third-party engine implements does not promise it, so the stream is put back here.
                    if (content.CanSeek && content.Position != 0)
                    {
                        content.Position = 0;
                    }
                }

                if (!info.IsReadable)
                {
                    // An encrypted document the reader could not open is described rather than thrown
                    // from, so the decision to stop is made here: either a password is what stands in
                    // the way and the caller can ask for one, or nothing would help.
                    throw new PdfEncryptionException(source.DisplayName, info.RequiresPassword);
                }

                var pages = source.Pages.Resolve(info.PageCount, source.DisplayName);

                resolved.Add(new ResolvedPdfSource
                {
                    Source = source,
                    Content = engineContent,
                    Info = info,
                    Pages = pages,
                });
            }

            var request = new MergeEngineRequest
            {
                Sources = resolved,
                Output = plan.Output,
                PrimarySourceId = plan.PrimarySource.Id,
            };

            var result = await engine
                .MergeAsync(request, destination, cancellationToken)
                .ConfigureAwait(false);

            return result with { Duration = Stopwatch.GetElapsedTime(started) };
        }
        finally
        {
            foreach (var stream in openStreams)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
