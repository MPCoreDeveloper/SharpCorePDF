using System.Collections.Immutable;
using System.Diagnostics;
using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;
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

        if (plan.Operations.Any(operation => operation is PdfSplitDocumentOperation))
        {
            throw new PdfEditPlanValidationException(
                "the plan asks for a split, which produces several documents; apply it with SplitAsync instead of writing one destination.");
        }

        // An incremental update copies the bytes of the source into the result, so the source has to be
        // a byte sequence that can be read from its start: a forward-only stream cannot be replayed. The
        // destination has to be empty for the same reason in reverse — the result begins with the source,
        // and an offset recorded for the revision counts from the first byte of the file.
        if (plan.Output.UseIncrementalUpdate && !content.CanSeek)
        {
            throw new PdfEditPlanValidationException(
                "an incremental update requires a source that can seek, because the bytes of its earlier revisions are copied into the result unchanged.");
        }

        if (plan.Output.UseIncrementalUpdate && destination.CanSeek && (destination.Length != 0 || destination.Position != 0))
        {
            throw new PdfEditPlanValidationException(
                "an incremental update writes the whole document to the destination, beginning with the bytes of its source, so the destination has to be an empty stream.");
        }

        var started = Stopwatch.GetTimestamp();
        var bytes = await PdfByteReader
            .ReadAllAsync(content, options.MaxSourceBytes, sourceName, cancellationToken)
            .ConfigureAwait(false);

        var reader = new PdfFileReader(bytes, sourceName, options, password);

        if (reader.IsEncrypted && reader.Security is null)
        {
            throw new PdfEncryptionException(sourceName, reader.RequiresPassword);
        }

        var incremental = plan.Output.UseIncrementalUpdate;

        if (incremental)
        {
            EnsureIncrementalSource(reader, sourceName);
        }

        if (reader.Pages.Count == 0)
        {
            throw new PdfSourceFormatException(
                sourceName,
                "the document has no page tree, so it cannot be edited.");
        }

        var warnings = new List<PdfWarning>(reader.Warnings);
        var profile = plan.Output.PdfAProfile;

        // A rewrite of a signed document leaves the signature behind: it names a byte range that no
        // longer exists in the result. Saying so is part of the outcome, because the caller may be
        // about to hand the result on as a signed document.
        if (CarriesSignature(reader))
        {
            warnings.Add(incremental
                ? new(
                    PdfWarningKind.PriorSignaturePreserved,
                    $"{sourceName} is signed, and the signature still covers the revision it was made over because the edit appended a revision instead of rewriting the bytes.")
                : new(
                    PdfWarningKind.PriorSignatureInvalidated,
                    $"{sourceName} is signed, and the signature cannot be valid in the result: a complete rewrite changes the bytes it covers. Write the edit with UseIncrementalUpdate to keep it."));
        }

        if (profile is { } archival)
        {
            // The analysis is what keeps a refusal honest: a document that cannot be made conformant is
            // stopped here, before the writer exists and before a byte reaches the destination.
            var requirements = PdfARequirements.Analyze(reader, archival);

            if (requirements.Unsatisfied.Count > 0)
            {
                throw new PdfEditPlanValidationException(
                    [
                        $"'{sourceName}' cannot be written for {PdfAProfileRules.Name(archival)}:",
                        .. requirements.Unsatisfied,
                    ]);
            }

            foreach (var removal in requirements.Removable)
            {
                warnings.Add(new(
                    PdfWarningKind.PdfAFeatureRemoved,
                    $"'{sourceName}': {removal} was removed because {PdfAProfileRules.Name(archival)} forbids it."));
            }
        }

        var sources = await OpenSourcesAsync(reader, plan, warnings, cancellationToken).ConfigureAwait(false);
        var form = new FormPlan();
        var layout = BuildLayout(sources, plan, form, warnings, out var layoutChanged);

        if (layout.Count == 0)
        {
            throw new PdfEditPlanValidationException(
                $"the edit removes every page of '{sourceName}', and a PDF document must keep at least one page.");
        }

        var modificationDate = plan.Output.ModificationDate ?? DateTimeOffset.UtcNow;

        // A revision appended to a document cannot change the header of the first revision, so an
        // incremental update keeps the version of its source whatever the options ask for.
        var version = plan.Output.TargetVersion;

        if (incremental)
        {
            version = reader.Version;
        }
        else if (profile is { } target)
        {
            version = PdfAProfileRules.Version(target);
        }

        if (incremental && version.CompareTo(plan.Output.TargetVersion) != 0)
        {
            warnings.Add(new(
                PdfWarningKind.IncrementalUpdateVersionKept,
                $"{sourceName} is PDF {reader.Version} and the edit keeps that version: an appended revision cannot change the header of the revision it follows."));
        }
        var identifier = BuildFileIdentifier(plan, version, layout.Count, modificationDate);
        var metadata = ResolveMetadata(reader, plan, modificationDate);

        if (profile is not null && metadata.CustomProperties.Count > 0)
        {
            // A custom information entry has no XMP property of its own without an extension schema,
            // and the profile requires the dictionary and the packet to agree.
            warnings.Add(new(
                PdfWarningKind.PdfAFeatureRemoved,
                $"'{sourceName}': the custom document information entries ({string.Join(", ", metadata.CustomProperties.Keys.Order(StringComparer.Ordinal))}) were removed because {PdfAProfileRules.Name(profile.Value)} needs every entry of the dictionary in its XMP packet."));
            metadata = metadata with { CustomProperties = ImmutableDictionary<string, string>.Empty };
        }

        // The capture is disposed with the edit: it owns a pooled buffer, and the destination it
        // forwards to belongs to the caller and is left open.
        using var capture = options.VerifyOutput ? new PdfCaptureStream(destination) : null;

        // An archival document is never encrypted; the plan validation refuses that combination, so a
        // profile is only ever written in the clear.
        var security = plan.Output.Encryption is { } encryption
            ? PdfSecurityWriter.Create(encryption, identifier.Bytes)
            : null;

        // An incremental update writes the source through first: its bytes are the earlier revisions of
        // the result, and the writer numbers its objects above every object they used.
        var revision = incremental
            ? AppendRevision(capture ?? destination, reader, warnings, sourceName)
            : null;

        var writer = revision?.Writer ?? new PdfWriter(capture ?? destination, version, options, security);
        var catalogNumber = writer.AllocateObject();
        var pagesNodeNumber = writer.AllocateObject();

        // Every document the plan reads has its own copier, because object numbers are only unique
        // inside one document. All copiers write into the same result, which is what makes an
        // imported page indistinguishable from a page that was already there.
        sources.Attach(writer);

        foreach (var (source, copier) in sources.Copiers)
        {
            RedirectPageTree(source, copier, pagesNodeNumber);
        }

        AllocatePageTargets(writer, layout);

        foreach (var (source, copier) in sources.Copiers)
        {
            var contributed = layout.Where(page => page.Reader == source).ToList();

            if (contributed.Count == 0)
            {
                continue;
            }

            RedirectSourcePages(source, contributed, copier);
        }

        // The form is written before the pages: a page names the widgets that stand on it, and a
        // flattened field adds a stream and a resource the page dictionary has to carry.
        var acroForm = WriteForm(reader, sources.CopierOf(reader), writer, layout, form, warnings);

        WritePages(sources, writer, pagesNodeNumber, layout, new AnnotationContext(writer, modificationDate), warnings);

        var catalog = BuildCatalog(
            reader,
            sources.CopierOf(reader),
            writer,
            pagesNodeNumber,
            plan,
            profile,
            metadata,
            layoutChanged,
            [.. layout.Select(page => page.TargetNumber)],
            acroForm,
            form.Touched,
            warnings);

        if (profile is { } written)
        {
            warnings.Add(new(
                PdfWarningKind.PdfAProfileWritten,
                $"'{sourceName}' was written for {PdfAProfileRules.Name(written)}; conformance is established by an external validator, not by this library."));
        }

        writer.WriteObject(catalogNumber, catalog);
        WritePagesNode(writer, pagesNodeNumber, layout);

        // Resources, contents and annotations were reserved while the pages were built; this is where
        // their bodies are written.
        sources.Flush();

        var infoNumber = WriteInformation(writer, metadata);
        WriteTrailer(writer, catalogNumber, infoNumber, identifier, security, revision?.PreviousXref);

        Verify(capture, layout, plan.Output.Encryption?.UserPassword, warnings);

        return new PdfEditResult
        {
            PageCount = layout.Count,
            BytesWritten = writer.Length,
            OutputVersion = version,
            Elapsed = Stopwatch.GetElapsedTime(started),
            Operations = [.. plan.Operations.Select(operation => operation.ToString())],
            Warnings = warnings,
        };
    }

    /// <summary>Reads the written document back and confirms its page count and rotations.</summary>
    /// <param name="capture">The capture of the output, when verification is enabled.</param>
    /// <param name="layout">The pages that were written, in order.</param>
    /// <param name="password">The password the result was encrypted with, or <see langword="null"/>.</param>
    /// <param name="warnings">The list that receives the verification remark.</param>
    /// <exception cref="InvalidOperationException">The document that was written cannot be read back, or does not match the plan.</exception>
    private void Verify(PdfCaptureStream? capture, List<EditedPage> layout, string? password, List<PdfWarning> warnings)
    {
        if (capture is not { } recorded)
        {
            return;
        }

        try
        {
            var reader = new PdfFileReader(recorded.CapturedBytes, "edited document", options, password);

            if (reader.IsEncrypted && reader.Security is null)
            {
                throw new InvalidOperationException(
                    "The edited document was written encrypted but cannot be opened with the password it was written with.");
            }

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
