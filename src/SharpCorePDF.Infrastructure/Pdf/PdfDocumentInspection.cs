using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Turns a read document into the read model the application layer works with.
/// </summary>
/// <remarks>
/// Every fact is derived from the document itself; nothing is guessed, and anything that could not be
/// read is reported as a warning instead of being silently invented.
/// </remarks>
internal static partial class PdfDocumentInspection
{
    /// <summary>The keys of the document information dictionary that are part of the standard set.</summary>
    private static readonly string[] StandardInformationKeys =
        ["Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate"];

    /// <summary>Describes a document that has already been read.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="lengthBytes">The number of bytes the document occupies.</param>
    /// <returns>The description.</returns>
    public static PdfDocumentInfo Describe(PdfFileReader reader, long lengthBytes)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var warnings = new List<string>(reader.Warnings);
        var encrypted = reader.IsEncrypted;

        if (encrypted)
        {
            warnings.Add(
                $"'{reader.SourceName}': the document is encrypted; its page tree cannot be read without the password and it cannot be merged.");
        }

        List<PdfPageInfo> pages = encrypted ? [] : ReadPages(reader, warnings);

        return new PdfDocumentInfo
        {
            SourceName = reader.SourceName,
            Version = reader.Version,
            PageCount = pages.Count,
            IsEncrypted = encrypted,
            RequiresPassword = false,
            Metadata = encrypted ? PdfMetadata.Empty : ReadMetadata(reader),
            Pages = pages,
            HasOutlines = !encrypted && reader.Catalog?.Get("Outlines") is not null,
            OutlineItemCount = encrypted ? 0 : CountOutlineItems(reader),
            EmbeddedFileCount = encrypted ? 0 : CountEmbeddedFiles(reader),
            HasAcroForm = !encrypted && reader.Catalog?.Get("AcroForm") is not null,
            SourceLengthBytes = lengthBytes,
            Warnings = warnings,
        };
    }

    private static PdfMetadata ReadMetadata(PdfFileReader reader)
    {
        if (reader.Information is not { } information)
        {
            return PdfMetadata.Empty;
        }

        var custom = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in information.Entries)
        {
            if (StandardInformationKeys.Contains(entry.Key, StringComparer.Ordinal) || entry.Value is not PdfString)
            {
                continue;
            }

            var text = information.GetText(entry.Key);

            if (!string.IsNullOrWhiteSpace(text))
            {
                custom[entry.Key] = text;
            }
        }

        return new PdfMetadata
        {
            Title = information.GetText("Title"),
            Author = information.GetText("Author"),
            Subject = information.GetText("Subject"),
            Keywords = information.GetText("Keywords"),
            Creator = information.GetText("Creator"),
            Producer = information.GetText("Producer"),
            CreationDate = TryReadDate(information, "CreationDate"),
            ModificationDate = TryReadDate(information, "ModDate"),
            CustomProperties = custom,
        };
    }

    private static DateTimeOffset? TryReadDate(PdfDictionary information, string key) =>
        PdfDate.TryParse(information.GetText(key), out var value) ? value : null;
}
