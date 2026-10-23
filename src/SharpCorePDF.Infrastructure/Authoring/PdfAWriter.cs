using SharpCorePDF.Application.Ports;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// The engine's implementation of <see cref="IPdfDocumentAWriter"/>: it analyses the source against the
/// profile, and either refuses it or writes a complete archival document through the editor.
/// </summary>
/// <remarks>
/// <para>
/// The analysis runs before a byte is written, which is what makes the refusal path trustworthy: a
/// document with an unembedded font or a transparency PDF/A-1b forbids leaves the caller's destination
/// untouched instead of producing a file that is labelled archival and is not.
/// </para>
/// <para>
/// The result is a complete rewrite, never an incremental update. The profile decides the version of
/// the document — PDF 1.4 for -1b, PDF 1.7 for -2b and -3b — and the editor adds the XMP packet and the
/// output intent the profile requires.
/// </para>
/// </remarks>
public sealed class PdfAWriter : IPdfDocumentAWriter
{
    private readonly PdfEngineOptions options;
    private readonly PdfEditEngine engine;

    /// <summary>Initializes a new instance of the <see cref="PdfAWriter"/> class.</summary>
    /// <param name="options">The engine options; defaults to <see cref="PdfEngineOptions.Default"/>.</param>
    public PdfAWriter(PdfEngineOptions? options = null)
    {
        this.options = options ?? PdfEngineOptions.Default;
        engine = new PdfEditEngine(this.options);
    }

    /// <summary>Gets the options this engine was created with.</summary>
    public PdfEngineOptions Options => options;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// The destination is not writable, the profile is not defined, or the options are not usable.
    /// </exception>
    /// <exception cref="InvalidOperationException">The engine options are not usable.</exception>
    /// <exception cref="PdfEncryptionException">The source is encrypted and cannot be opened.</exception>
    /// <exception cref="PdfSourceFormatException">The source is not a readable PDF document, or has no page.</exception>
    public async Task<PdfAConversionResult> ConvertAsync(
        string sourceName,
        Stream content,
        Stream destination,
        PdfAProfile profile,
        PdfAConversionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                profile,
                "The profile is not one the engine writes.");
        }

        var effective = options ?? PdfAConversionOptions.Default;
        var optionProblems = effective.Validate();

        if (optionProblems.Count > 0)
        {
            throw new ArgumentException(
                $"The PDF/A options are not usable: {string.Join(" ", optionProblems)}",
                nameof(options));
        }

        // The engine implements a public port, so the limits it was created with are checked here as
        // well: a caller that constructed it directly has not been through the application layer.
        var engineProblems = this.options.Validate();

        if (engineProblems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The engine options are not usable: {string.Join(" ", engineProblems)}");
        }

        var bytes = await PdfByteReader
            .ReadAllAsync(content, this.options.MaxSourceBytes, sourceName, cancellationToken)
            .ConfigureAwait(false);

        var reader = new PdfFileReader(bytes, sourceName, this.options, password);

        if (reader.IsEncrypted && reader.Security is null)
        {
            throw new PdfEncryptionException(sourceName, reader.RequiresPassword);
        }

        if (reader.Pages.Count == 0)
        {
            throw new PdfSourceFormatException(
                sourceName,
                $"the document has no page tree, so it cannot be written for {PdfAProfileRules.Name(profile)}.");
        }

        var requirements = PdfARequirements.Analyze(reader, profile);

        if (requirements.Unsatisfied.Count > 0)
        {
            // Nothing is written: a caller that receives IsWritten = false can ship the source, fix it,
            // or choose another profile, and never mistakes this result for an archival master.
            return new PdfAConversionResult
            {
                Profile = profile,
                IsWritten = false,
                PageCount = reader.Pages.Count,
                UnsatisfiedRequirements = requirements.Unsatisfied,
                Warnings = [.. reader.Warnings],
            };
        }

        var plan = PdfEditPlan.Create(new PdfEditOutputOptions
        {
            TargetVersion = PdfAProfileRules.Version(profile),
            PdfAProfile = profile,
            Metadata = effective.Metadata,
            ModificationDate = effective.ModificationDate,
            Producer = effective.Producer,
            DocumentId = effective.DocumentId,
        });

        // The source was consumed by the read above; the edit reads the bytes it was given, so the
        // caller's stream is only ever read once and from its start.
        using var prepared = new MemoryStream(bytes, writable: false);

        var edited = await engine
            .ApplyAsync(sourceName, prepared, plan, password, destination, cancellationToken)
            .ConfigureAwait(false);

        return new PdfAConversionResult
        {
            Profile = profile,
            IsWritten = true,
            PageCount = edited.PageCount,
            BytesWritten = edited.BytesWritten,
            Warnings = edited.Warnings,
        };
    }
}
