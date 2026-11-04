using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Application.Extensions;

/// <summary>
/// Convenience overloads for <see cref="IPdfMerger"/>, so the common shapes — merge to a file, merge
/// to a byte array, merge a list of documents — do not need a plan to be built by hand.
/// </summary>
/// <remarks>
/// These members are declared with C# extension blocks: they read as if they were part of
/// <see cref="IPdfMerger"/> while the interface itself stays at its minimal, mockable surface.
/// </remarks>
public static class PdfMergerExtensions
{
    extension(IPdfMerger merger)
    {
        /// <summary>Merges the plan and writes the result to a file.</summary>
        /// <param name="plan">The plan describing the merge.</param>
        /// <param name="destinationPath">The path of the file to create or overwrite.</param>
        /// <param name="cancellationToken">A token to cancel the merge.</param>
        /// <returns>A summary of what was written.</returns>
        /// <remarks>
        /// The path is used as given: a relative path resolves against the process working directory,
        /// <c>..</c> is followed, the destination directory is created when it does not exist, and an
        /// existing file is truncated. No root is enforced — a caller that takes the path from an
        /// untrusted source must confine it before the call.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="destinationPath"/> is null, empty or whitespace.</exception>
        public async Task<MergeResult> MergeToFileAsync(
            MergePlan plan,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            var fullPath = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var file = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true);

            var result = await merger.MergeAsync(plan, file, cancellationToken).ConfigureAwait(false);
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);

            return result;
        }

        /// <summary>Merges the plan into a byte array.</summary>
        /// <param name="plan">The plan describing the merge.</param>
        /// <param name="cancellationToken">A token to cancel the merge.</param>
        /// <returns>The bytes of the merged document.</returns>
        /// <remarks>
        /// <para>
        /// Use this only when the result fits in memory. For large results, merge to a file or to a
        /// stream instead.
        /// </para>
        /// <para>
        /// Unlike the materialising reads, whose result is a view of the buffer the copy route filled,
        /// this member returns a <see cref="byte"/> array of exactly the merged length: a merge writes an
        /// unknown number of bytes into a stream that grows by doubling, so the array cannot be that
        /// stream's own buffer and <c>ToArray</c> copies it. That copy is the cost of the return type,
        /// not an oversight — <see cref="MergeToFileAsync"/> and <c>MergeAsync</c>, which write where the
        /// bytes are wanted, avoid it.
        /// </para>
        /// </remarks>
        public async Task<byte[]> MergeToBytesAsync(
            MergePlan plan,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);

            using var buffer = new MemoryStream();
            await merger.MergeAsync(plan, buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }

        /// <summary>Merges a list of documents, contributing all of their pages, into a stream.</summary>
        /// <param name="sources">The documents to merge, in merge order.</param>
        /// <param name="destination">A writable stream that receives the merged document.</param>
        /// <param name="cancellationToken">A token to cancel the merge.</param>
        /// <returns>A summary of what was written.</returns>
        public Task<MergeResult> MergeAsync(
            IEnumerable<IPdfContentSource> sources,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(destination);

            var plan = MergePlan.Create().AddSources(sources.Select(source => PdfSource.Create(source)));

            return merger.MergeAsync(plan, destination, cancellationToken);
        }
    }
}
