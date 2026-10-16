using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The engine's implementation of <see cref="IPdfMergeEngine"/>: it reads every source, copies the
/// objects the selected pages need into a new document, and writes that document with a classic
/// cross-reference table.
/// </summary>
/// <remarks>
/// <para>
/// Stream bytes are never re-compressed: page content, images and fonts are copied verbatim, and only
/// object streams and cross-reference streams are decoded, because their contents have to be
/// understood to be copied. Object-stream-stored objects are therefore written as ordinary objects in
/// the result.
/// </para>
/// <para>
/// Each merge runs on its own context and writes into the destination the caller supplies, so an
/// engine instance can serve concurrent merges without sharing state.
/// </para>
/// </remarks>
public sealed partial class PdfMergeEngine : IPdfMergeEngine
{
    private readonly PdfEngineOptions options;

    /// <summary>Initializes a new instance of the <see cref="PdfMergeEngine"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfMergeEngine(PdfEngineOptions? options = null) => this.options = options ?? PdfEngineOptions.Default;

    /// <summary>Gets the options this engine was created with.</summary>
    public PdfEngineOptions Options => options;

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="InvalidOperationException">The engine options are not usable.</exception>
    public async Task<MergeResult> MergeAsync(
        MergeEngineRequest request,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        // The engine implements a public port, so what a caller asks of the result is checked here as
        // well: the application layer has already refused an unusable plan, but a direct caller has not
        // been through that.
        var outputProblems = request.Output.Validate();

        if (outputProblems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The output options are not usable: {string.Join(" ", outputProblems)}");
        }

        var started = Stopwatch.GetTimestamp();

        // The moment the result is stamped with, and the identifier that is written twice in its
        // trailer, are decided before the first source is read: the identifier is part of the key
        // derivation of every revision, so it must exist before the first encrypted byte is written.
        var modificationDate = request.Output.ModificationDate ?? DateTimeOffset.UtcNow;
        var identifier = BuildFileIdentifier(request, modificationDate);

        // The capture is disposed with the merge: it owns a pooled buffer, and the destination it
        // forwards to belongs to the caller and is left open.
        using var capture = options.VerifyOutput ? new PdfCaptureStream(destination) : null;

        var security = request.Output.Encryption is { } encryption
            ? PdfSecurityWriter.Create(encryption, identifier.Bytes)
            : null;

        var writer = new PdfWriter(capture ?? destination, request.Output.TargetVersion, options, security);
        var context = new PdfMergeContext(request, writer, options, modificationDate)
        {
            CatalogNumber = writer.AllocateObject(),
            PagesNodeNumber = writer.AllocateObject(),
            Capture = capture,
        };

        await ReadSourcesAsync(context, cancellationToken).ConfigureAwait(false);

        var outlines = MergeOutlines(context);
        var catalog = BuildCatalog(context, outlines);

        writer.WriteObject(context.CatalogNumber, catalog);
        WritePagesNode(context);
        Flush(context);

        var infoNumber = WriteInformation(context);
        WriteTrailer(context, infoNumber, identifier, security);

        Verify(context);

        return new MergeResult
        {
            PageCount = context.Sources.Sum(source => source.PageTargets.Count),
            SourceCount = context.Sources.Count,
            OutputBytes = writer.Length,
            OutputVersion = request.Output.TargetVersion,
            Duration = Stopwatch.GetElapsedTime(started),
            PrimarySourceId = request.PrimarySourceId,
            Sources = [.. context.Sources.Select(source => source.ToSummary())],
            Warnings = [.. context.Warnings],
        };
    }

    /// <summary>Writes the reserved bodies of every source.</summary>
    /// <param name="context">The merge context.</param>
    private static void Flush(PdfMergeContext context)
    {
        foreach (var source in context.Sources)
        {
            source.Copier.Flush();
        }
    }

    /// <summary>
    /// Reads the merged document back and confirms that it can be opened and has the pages that were
    /// written. Only runs when <see cref="PdfEngineOptions.VerifyOutput"/> is enabled.
    /// </summary>
    /// <param name="context">The merge context.</param>
    /// <exception cref="InvalidOperationException">The document that was written cannot be read back.</exception>
    private void Verify(PdfMergeContext context)
    {
        if (context.WrittenBytes is not { } data)
        {
            return;
        }

        var expected = context.Sources.Sum(source => source.PageTargets.Count);

        try
        {
            // An encrypted document is read back with the password it was just written with, which is
            // the only way to prove that the encryption is one the engine can undo again.
            var reader = new PdfFileReader(
                data,
                "merged document",
                options,
                context.Output.Encryption?.UserPassword);

            if (reader.IsEncrypted && reader.Security is null)
            {
                throw new InvalidOperationException(
                    "The merged document was written encrypted but cannot be opened with the password it was written with.");
            }

            if (reader.Pages.Count != expected)
            {
                throw new InvalidOperationException(
                    $"The merged document reports {reader.Pages.Count} page(s) but {expected} page(s) were written.");
            }

            context.Warnings.Add(
                $"the merged document was read back and contains {reader.Pages.Count} page(s), as expected.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The merged document could not be read back: {exception.Message}",
                exception);
        }
    }

    /// <summary>Builds the identifier written twice in the trailer of the result.</summary>
    /// <param name="request">The request being executed.</param>
    /// <param name="modificationDate">The moment written as the modification date of the result.</param>
    /// <returns>The identifier as a hexadecimal string.</returns>
    /// <remarks>
    /// The seed describes the document that is being written rather than the documents it is made from:
    /// where the content came from leaves no trace in the result, but the version, the moment and the
    /// size of the merge do. Two merges of the same content at the same moment therefore produce the
    /// same file, which is what makes the output reproducible. The values are taken from the request
    /// because the identifier is needed before a single source has been read.
    /// </remarks>
    private static PdfString BuildFileIdentifier(MergeEngineRequest request, DateTimeOffset modificationDate)
    {
        var seed = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{request.Output.TargetVersion}|{modificationDate:O}|{request.Sources.Count}|{request.TotalPageCount}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        return new PdfString(hash.AsSpan(0, 16).ToArray(), isHexadecimal: true);
    }

    /// <summary>Creates a PDF string for a text entry, or nothing when the text is empty.</summary>
    /// <param name="value">The text, or <see langword="null"/>.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    private static PdfString? Text(string? value) =>
        string.IsNullOrEmpty(value) ? null : PdfString.FromText(value);
}
