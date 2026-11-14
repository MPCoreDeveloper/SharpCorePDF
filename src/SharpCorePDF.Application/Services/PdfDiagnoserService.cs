using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Application.Services;

/// <summary>
/// The use case behind <see cref="IPdfDiagnoser"/>: it asks each existing use case in turn, merges their
/// warnings and returns one report.
/// </summary>
/// <remarks>
/// <para>
/// The report is composed rather than computed: inspection answers the structure, the font inspector the
/// fonts, the signature validator the signatures, and the archival writer — when a profile is named — the
/// requirements the writer would refuse. Because each of those is the one the rest of the library uses,
/// the report cannot drift from the behaviour it describes.
/// </para>
/// <para>
/// A document that stays closed is reported with <see cref="PdfDocumentInfo.IsReadable"/> and
/// <see cref="PdfDocumentInfo.RequiresPassword"/> instead of an exception; the other reads are skipped,
/// because they would fail on the same document. The class is stateless and thread safe.
/// </para>
/// </remarks>
public sealed class PdfDiagnoserService : IPdfDiagnoser
{
    private readonly IPdfInspector inspector;
    private readonly IPdfFontInspector fonts;
    private readonly IPdfSignatureValidator signatures;
    private readonly IPdfAWriter archival;

    /// <summary>Initializes a new instance of the <see cref="PdfDiagnoserService"/> class.</summary>
    /// <param name="inspector">The use case that describes the document.</param>
    /// <param name="fonts">The use case that reads the fonts.</param>
    /// <param name="signatures">The use case that checks the signatures.</param>
    /// <param name="archival">The use case whose pre-flight answers the archival question.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PdfDiagnoserService(
        IPdfInspector inspector,
        IPdfFontInspector fonts,
        IPdfSignatureValidator signatures,
        IPdfAWriter archival)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        this.signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        this.archival = archival ?? throw new ArgumentNullException(nameof(archival));
    }

    /// <inheritdoc />
    public async Task<PdfDiagnosticsReport> DiagnoseAsync(
        IPdfContentSource source,
        string? password = null,
        PdfAProfile? archivalProfile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (archivalProfile is { } profile && !Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(
                nameof(archivalProfile),
                archivalProfile,
                "The PDF/A profile is not one the engine writes.");
        }

        var document = await inspector
            .InspectAsync(source, password, cancellationToken)
            .ConfigureAwait(false);

        // The other reads would throw on a document that stays closed. The inspector has already said
        // what stands in the way, so the answer is returned instead of an exception.
        if (!document.IsReadable)
        {
            return new PdfDiagnosticsReport
            {
                Document = document,
                Warnings = [.. document.Warnings],
            };
        }

        var fontResult = await fonts
            .ReadFontsAsync(source, password, cancellationToken)
            .ConfigureAwait(false);

        var signatureResult = await signatures
            .ValidateAsync(source, trust: null, password, cancellationToken)
            .ConfigureAwait(false);

        PdfAConversionResult? archivalResult = null;

        if (archivalProfile is { } archivalProfileValue)
        {
            // The writer's own pre-flight, with a destination that discards: a report is read-only, and
            // the result says what would be refused without a document being kept.
            archivalResult = await archival
                .ConvertAsync(source, Stream.Null, archivalProfileValue, options: null, password, cancellationToken)
                .ConfigureAwait(false);
        }

        var warnings = new List<PdfWarning>();
        Add(warnings, document.Warnings);
        Add(warnings, fontResult.Warnings);

        if (archivalResult is { } archivalWarnings)
        {
            Add(warnings, archivalWarnings.Warnings);
        }

        return new PdfDiagnosticsReport
        {
            Document = document,
            Fonts = fontResult.Items,
            Signatures = signatureResult,
            Archival = archivalResult,
            Warnings = warnings,
        };
    }

    /// <summary>Adds the warnings that are not already in the report, in the order they arrive.</summary>
    /// <param name="destination">The merged list.</param>
    /// <param name="warnings">The warnings of one sub-read.</param>
    private static void Add(List<PdfWarning> destination, IEnumerable<PdfWarning> warnings) =>
        destination.AddRange(warnings.Where(warning => !destination.Contains(warning)));
}
