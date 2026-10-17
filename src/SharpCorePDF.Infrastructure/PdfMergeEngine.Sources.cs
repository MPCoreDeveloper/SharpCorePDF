using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The source reading and page copying half of <see cref="PdfMergeEngine"/>.
/// </summary>
public sealed partial class PdfMergeEngine
{
    /// <summary>Reads every source, reserves the pages it contributes and flattens its page tree.</summary>
    /// <param name="context">The merge context.</param>
    /// <param name="cancellationToken">A token to cancel the merge.</param>
    private async Task ReadSourcesAsync(PdfMergeContext context, CancellationToken cancellationToken)
    {
        foreach (var resolved in context.Request.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var displayName = resolved.Source.DisplayName;

            // A caller may hand over a stream that was already read once — the application layer
            // inspects a source before it merges it — so the position is reset when that is possible.
            if (resolved.Content.CanSeek && resolved.Content.Position != 0)
            {
                resolved.Content.Position = 0;
            }

            var bytes = await PdfByteReader
                .ReadAllAsync(resolved.Content, options.MaxSourceBytes, displayName, cancellationToken)
                .ConfigureAwait(false);

            var reader = new PdfFileReader(bytes, displayName, options, resolved.Source.Password);

            if (reader.IsEncrypted && reader.Security is null)
            {
                // The application layer refuses a source it could not open before the engine runs, but
                // the engine implements a public port and must not be fooled by being called directly:
                // the streams of such a source would be copied in their still-encrypted form. A source
                // whose encryption the engine did open is merged like any other, because every string
                // and stream of it is already in the clear by the time it is read.
                throw new PdfEncryptionException(displayName, reader.RequiresPassword);
            }

            var source = new MergeSource(resolved, reader, new PdfObjectCopier(reader, context.Writer));

            PreparePages(context, source);
            context.Sources.Add(source);

            foreach (var warning in reader.Warnings.Where(warning => !context.Warnings.Contains(warning)))
            {
                context.Warnings.Add(warning);
            }

            // Flushing per source keeps the objects of one document together in the output and keeps
            // the queue of pending work small.
            source.Copier.Flush();
        }
    }
}
