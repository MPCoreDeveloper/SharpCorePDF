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

        var warnings = new List<PdfWarning>(reader.Warnings);
        var encrypted = reader.IsEncrypted;

        // A document is readable when it is not encrypted, or when the engine opened its encryption
        // with the password the caller supplied. Everything else about an encrypted document that
        // stayed closed is left empty, because reporting a page count of zero would be a claim about
        // the document rather than about the reader.
        var readable = !encrypted || reader.Security is not null;

        if (!readable)
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionNotOpened,
                $"'{reader.SourceName}': the document is encrypted and could not be opened, so its page tree cannot be read and it cannot be merged."));
        }

        List<PdfPageInfo> pages = readable ? ReadPages(reader, warnings) : [];

        return new PdfDocumentInfo
        {
            SourceName = reader.SourceName,
            Version = reader.Version,
            PageCount = pages.Count,
            IsEncrypted = encrypted,
            RequiresPassword = reader.RequiresPassword,
            SecurityHandlerRevision = reader.DeclaredSecurityHandlerRevision,
            IsReadable = readable,
            Metadata = readable ? ReadMetadata(reader) : PdfMetadata.Empty,
            Pages = pages,
            HasOutlines = readable && reader.Catalog?.Get("Outlines") is not null,
            OutlineItemCount = readable ? CountOutlineItems(reader) : 0,
            EmbeddedFileCount = readable ? CountEmbeddedFiles(reader) : 0,
            HasAcroForm = readable && reader.Catalog?.Get("AcroForm") is not null,
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
