using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.Entities;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.DependencyInjection;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Validation;

/// <summary>
/// Reads one document through every read use case, then writes it again through a merge and an edit and
/// reads what was written.
/// </summary>
/// <remarks>
/// <para>
/// The answer is the exit code, because a corpus run cares about one thing per file: whether the engine
/// read it, refused it the way the contract says it refuses a document (a <see cref="PdfException"/>), or
/// broke — any other exception, which is a defect in the engine whatever the file looks like. A process
/// that dies instead of answering (a stack overflow, the memory limit of the run) is a fourth outcome the
/// caller sees from the outside.
/// </para>
/// <para>
/// The write half matters as much as the read half: a document the engine reads and then writes into
/// something it cannot read itself is the defect a merge hands to the next reader.
/// </para>
/// </remarks>
internal static class Probe
{
    /// <summary>The document was read and written back.</summary>
    public const int Read = 0;

    /// <summary>The engine refused the document with a <see cref="PdfException"/>.</summary>
    public const int Refused = 2;

    /// <summary>The engine threw something other than a <see cref="PdfException"/>.</summary>
    public const int EngineFault = 3;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static readonly Lazy<ServiceProvider> LazyServices = new(() => new ServiceCollection()
        .AddSharpCorePDF()
        .BuildServiceProvider());

    /// <summary>Gets the composed engine, built once for every document a process reads.</summary>
    public static IServiceProvider Services => LazyServices.Value;

    /// <summary>Reads a profile the way the diagnostics console spells it.</summary>
    /// <param name="text">The value after <c>--profile</c>.</param>
    /// <returns>The profile, or <see langword="null"/> when the text names none.</returns>
    public static PdfAProfile? ParseProfile(string text) => text.ToUpperInvariant() switch
    {
        "1B" => PdfAProfile.PdfA1B,
        "2B" => PdfAProfile.PdfA2B,
        "2U" => PdfAProfile.PdfA2U,
        "3B" => PdfAProfile.PdfA3B,
        "3U" => PdfAProfile.PdfA3U,
        "4" => PdfAProfile.PdfA4,
        _ => null,
    };

    /// <summary>Probes a file and prints the outcome.</summary>
    /// <param name="path">The document.</param>
    /// <param name="password">The password, when the document needs one.</param>
    /// <param name="profile">The archival profile to pre-flight, or <see langword="null"/>.</param>
    /// <param name="convertPath">Where the archival result is written, so a validator can judge it; or <see langword="null"/>.</param>
    /// <param name="preflightOnly">Whether only the archival pre-flight runs, without the read use cases.</param>
    /// <returns>One of <see cref="Read"/>, <see cref="Refused"/> or <see cref="EngineFault"/>.</returns>
    public static async Task<int> RunAsync(string path, string? password, PdfAProfile? profile, string? convertPath, bool preflightOnly)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);

        try
        {
            if (!preflightOnly)
            {
                await ExerciseAsync(bytes, Path.GetFileName(path), password, CancellationToken.None).ConfigureAwait(false);
            }

            if (profile is { } archival)
            {
                await PreflightAsync(bytes, Path.GetFileName(path), password, archival, convertPath).ConfigureAwait(false);
            }

            Console.WriteLine("outcome: read");
            return Read;
        }
        catch (PdfException exception)
        {
            Console.WriteLine($"outcome: refused {exception.GetType().Name}: {exception.Message}");
            return Refused;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.WriteLine($"outcome: fault {exception.GetType().FullName}: {exception.Message}");
            Console.WriteLine(exception.StackTrace);
            return EngineFault;
        }
    }

    /// <summary>Reads a document through every use case and writes it back twice.</summary>
    /// <param name="bytes">The document.</param>
    /// <param name="name">The name the engine reports it under.</param>
    /// <param name="password">The password, when the document needs one.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when every use case has run.</returns>
    /// <exception cref="PdfException">The engine refused the document.</exception>
    /// <exception cref="InvalidOperationException">The engine wrote something it cannot read back.</exception>
    public static async Task ExerciseAsync(byte[] bytes, string name, string? password, CancellationToken cancellationToken)
    {
        var source = new PdfByteArraySource(bytes, name);
        var info = await Services.GetRequiredService<IPdfInspector>()
            .InspectAsync(source, password, cancellationToken).ConfigureAwait(false);

        // A document that stays closed has nothing else to read; the inspection saying so is the answer.
        if (!info.IsReadable || info.PageCount == 0)
        {
            return;
        }

        await ReadContentAsync(source, password, cancellationToken).ConfigureAwait(false);
        await ReadLayoutAsync(source, password, cancellationToken).ConfigureAwait(false);

        await Services.GetRequiredService<IPdfFontInspector>()
            .ReadFontsAsync(source, password, cancellationToken).ConfigureAwait(false);
        await Services.GetRequiredService<IPdfSignatureValidator>()
            .ValidateAsync(source, trust: null, password, cancellationToken).ConfigureAwait(false);

        var objects = Services.GetRequiredService<IPdfObjectInspector>();
        await objects.ReadObjectsAsync(source, password, cancellationToken).ConfigureAwait(false);
        await objects.ReadOperationsAsync(source, 1, password, cancellationToken).ConfigureAwait(false);

        await WriteBackAsync(source, password, info.PageCount, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadContentAsync(PdfByteArraySource source, string? password, CancellationToken cancellationToken)
    {
        var content = Services.GetRequiredService<IPdfContentReader>();

        await content.ReadTextAsync(source, PageRange.All, password, cancellationToken).ConfigureAwait(false);
        await content.ReadAnnotationsAsync(source, PageRange.All, password, cancellationToken).ConfigureAwait(false);
        await content.ReadFormFieldsAsync(source, password, cancellationToken).ConfigureAwait(false);
        await content.ReadMetadataAsync(source, password, cancellationToken).ConfigureAwait(false);
        await content.ReadOutlineAsync(source, password, cancellationToken).ConfigureAwait(false);

        var images = await content.ReadImagesAsync(source, PageRange.All, password, cancellationToken).ConfigureAwait(false);

        foreach (var image in images.Items)
        {
            await content.CopyImageDataAsync(source, image, Stream.Null, password, cancellationToken).ConfigureAwait(false);
        }

        var files = await content.ReadEmbeddedFilesAsync(source, password, cancellationToken).ConfigureAwait(false);

        foreach (var file in files.Items)
        {
            await content.CopyEmbeddedFileDataAsync(source, file, Stream.Null, password, cancellationToken).ConfigureAwait(false);
        }

        await foreach (var _ in content.ReadPageDetailsAsync(source, PageRange.All, password, cancellationToken).ConfigureAwait(false))
        {
            // Every page is enumerated; what a page holds was read by the calls above.
        }
    }

    private static async Task ReadLayoutAsync(PdfByteArraySource source, string? password, CancellationToken cancellationToken)
    {
        var layout = Services.GetRequiredService<IPdfLayoutReader>();

        await layout.ReadBlocksAsync(source, PageRange.All, options: null, password, cancellationToken).ConfigureAwait(false);
        await layout.ReadHyperlinksAsync(source, PageRange.All, password, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Merges and edits the document, then reads both results with the engine that wrote them.</summary>
    private static async Task WriteBackAsync(PdfByteArraySource source, string? password, int pageCount, CancellationToken cancellationToken)
    {
        var merged = await Services.GetRequiredService<IPdfMerger>().MergeToBytesAsync(
            MergePlan.Create().AddSource(PdfSource.Create(source, source.DisplayName, password: password)),
            cancellationToken).ConfigureAwait(false);

        await ExpectReadableAsync(merged, "merged", pageCount, cancellationToken).ConfigureAwait(false);

        var edited = await Services.GetRequiredService<IPdfDocumentEditor>().ApplyToArrayAsync(
            source,
            PdfEditPlan.Create().RotatePage(1, PageRotation.Clockwise90),
            password,
            cancellationToken).ConfigureAwait(false);

        await ExpectReadableAsync(edited, "edited", pageCount, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExpectReadableAsync(byte[] written, string what, int pageCount, CancellationToken cancellationToken)
    {
        var info = await Services.GetRequiredService<IPdfInspector>()
            .InspectAsync(new PdfByteArraySource(written, what + ".pdf"), password: null, cancellationToken)
            .ConfigureAwait(false);

        if (!info.IsReadable || info.PageCount != pageCount)
        {
            throw new InvalidOperationException(
                $"the {what} result of a {pageCount} page document reads back as {info.PageCount} page(s), readable {info.IsReadable}.");
        }
    }

    private static async Task PreflightAsync(byte[] bytes, string name, string? password, PdfAProfile profile, string? convertPath)
    {
        var source = new PdfByteArraySource(bytes, name);
        PdfAConversionResult? archival;

        if (convertPath is null)
        {
            var report = await Services.GetRequiredService<IPdfDiagnoser>()
                .DiagnoseAsync(source, password, profile, CancellationToken.None)
                .ConfigureAwait(false);

            archival = report.Archival;
        }
        else
        {
            // The result is kept so a validator can judge what the writer claims it wrote; a refusal leaves no file.
            var destination = File.Create(convertPath);

            await using (destination.ConfigureAwait(false))
            {
                archival = await Services.GetRequiredService<IPdfAWriter>()
                    .ConvertAsync(source, destination, profile, options: null, password, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (!archival.IsWritten)
            {
                File.Delete(convertPath);
            }
        }

        // One line a script can parse: whether the pre-flight would write the profile, and what it named.
        Console.WriteLine("archival: " + JsonSerializer.Serialize(
            new
            {
                profile = profile.ToString(),
                written = archival?.IsWritten,
                unsatisfied = archival?.UnsatisfiedRequirements ?? [],
                warnings = archival?.Warnings.Select(warning => warning.Kind.ToString()).Distinct().ToArray() ?? [],
            },
            Json));
    }
}
