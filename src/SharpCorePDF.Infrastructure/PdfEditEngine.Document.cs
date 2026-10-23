using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Domain.Aggregates;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The document structure half of <see cref="PdfEditEngine"/>: the catalog, the features carried over
/// from the source, the document information and the trailer.
/// </summary>
public sealed partial class PdfEditEngine
{
    /// <summary>The name of the tree of named destinations, which is carried over as a whole.</summary>
    private const string DestsEntry = "Dests";

    /// <summary>The registry the sRGB output condition of an archival document is registered with.</summary>
    private const string ColorRegistryName = "https://www.color.org";

    /// <summary>Builds the catalog of the edited document.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="writer">The writer of the result, which receives the archival entries.</param>
    /// <param name="pagesNodeNumber">The object number of the page tree node of the result.</param>
    /// <param name="plan">The plan being applied.</param>
    /// <param name="profile">The archival profile the result is written for, or <see langword="null"/>.</param>
    /// <param name="metadata">The document information the XMP packet mirrors.</param>
    /// <param name="layoutChanged">Whether an operation changed the order or number of pages.</param>
    /// <param name="warnings">The list that receives remarks about features an edit could not keep.</param>
    /// <returns>The catalog.</returns>
    /// <remarks>
    /// Page-independent features are carried over as they are. Features that name pages — bookmarks,
    /// named destinations, a form — are carried over too, with every reference to a source page
    /// redirected to the first page that survived. Page labels and XMP are the two features that
    /// cannot be carried over truthfully after an edit, so they are dropped and reported. A profile
    /// replaces the XMP packet and the output intent with the ones it requires.
    /// </remarks>
    private static PdfDictionary BuildCatalog(
        PdfFileReader reader,
        PdfObjectCopier copier,
        PdfWriter writer,
        int pagesNodeNumber,
        PdfEditPlan plan,
        PdfAProfile? profile,
        PdfMetadata metadata,
        bool layoutChanged,
        List<PdfWarning> warnings)
    {
        var source = reader.Catalog;
        var catalog = new PdfDictionary();
        catalog.Set("Type", new PdfName("Catalog"));
        catalog.Set("Pages", new PdfReference(pagesNodeNumber, 0));

        CopyFromSource(copier, source, catalog, "ViewerPreferences");
        CopyFromSource(copier, source, catalog, "PageLayout");
        CopyFromSource(copier, source, catalog, "PageMode");
        CopyFromSource(copier, source, catalog, "Lang");
        CopyFromSource(copier, source, catalog, "MarkInfo");
        CopyFromSource(copier, source, catalog, "Outlines");
        CopyNameTrees(reader, copier, source, catalog);

        if (profile is null)
        {
            CopyFromSource(copier, source, catalog, "OutputIntents");
            CopyFromSource(copier, source, catalog, "AcroForm");
        }
        else if (source is not null && reader.ResolveDictionary(source.Get("AcroForm")) is { } sourceForm)
        {
            // PDF/A requires a reader to be able to paint the form without running anything, so the
            // flag that asks it to build appearances itself is cleared rather than copied.
            var form = (PdfDictionary)copier.Clone(sourceForm);
            form.Set("NeedAppearances", PdfBoolean.False);
            catalog.Set("AcroForm", form);
        }

        if (source?.Get("PageLabels") is not null)
        {
            if (layoutChanged)
            {
                warnings.Add(new(
                    PdfWarningKind.PageLabelsDropped,
                    $"'{reader.SourceName}': the page labels were dropped because the edit changed the order or number of pages."));
            }
            else
            {
                CopyFromSource(copier, source, catalog, "PageLabels");
            }
        }

        if (profile is null && source?.Get("Metadata") is not null)
        {
            var informationOverridden = plan.Output.Metadata is not null
                || plan.Operations.Any(operation => operation is PdfSetMetadataOperation);

            if (!informationOverridden)
            {
                CopyFromSource(copier, source, catalog, "Metadata");
            }
            else
            {
                warnings.Add(new(
                    PdfWarningKind.XmpPacketDropped,
                    $"'{reader.SourceName}': the XMP packet was dropped because the edit sets document information; the information dictionary holds the new values."));
            }
        }

        if (profile is { } archival)
        {
            WriteArchivalEntries(writer, catalog, archival, metadata);
        }

        return catalog;
    }

    /// <summary>
    /// Writes the XMP packet and the output intent a PDF/A profile requires, and points the catalog at
    /// them.
    /// </summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="catalog">The catalog being built.</param>
    /// <param name="profile">The profile the document is written for.</param>
    /// <param name="metadata">The document information the packet mirrors.</param>
    /// <remarks>
    /// The packet is never compressed, which PDF/A-1 requires and the later parts allow, and the output
    /// intent names the sRGB colour space the profile was built for. Both replace whatever the source
    /// carried: a packet that describes a different document would contradict the information
    /// dictionary, and a copied intent would describe a colour space the pages may not use.
    /// </remarks>
    private static void WriteArchivalEntries(
        PdfWriter writer,
        PdfDictionary catalog,
        PdfAProfile profile,
        PdfMetadata metadata)
    {
        var profileData = SRgbColorProfile.Bytes;
        var iccNumber = writer.AllocateObject();

        writer.WriteObject(
            iccNumber,
            new PdfStream(new PdfDictionary().SetInteger("N", 3), profileData));

        var intent = new PdfDictionary()
            .SetName("Type", "OutputIntent")
            .SetName("S", "GTS_PDFA1")
            .Set("OutputCondition", PdfString.FromText("sRGB IEC61966-2.1"))
            .Set("OutputConditionIdentifier", PdfString.FromText("sRGB IEC61966-2.1"))
            .Set("RegistryName", PdfString.FromText(ColorRegistryName)) // NOSONAR S5332: an ICC registry identifier, not a request
            .Set("Info", PdfString.FromText("sRGB IEC61966-2.1"))
            .Set("DestOutputProfile", new PdfReference(iccNumber, 0));

        var intentNumber = writer.AllocateObject();
        writer.WriteObject(intentNumber, intent);

        var packet = XmpPacket.Build(profile, metadata);
        var metadataNumber = writer.AllocateObject();

        writer.WriteObject(
            metadataNumber,
            new PdfStream(
                new PdfDictionary().SetName("Type", "Metadata").SetName("Subtype", "XML"),
                packet));

        catalog.Set("OutputIntents", new PdfArray([new PdfReference(intentNumber, 0)]));
        catalog.Set("Metadata", new PdfReference(metadataNumber, 0));
    }

    /// <summary>Copies named destinations and embedded files from the source.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="source">The catalog of the source.</param>
    /// <param name="catalog">The catalog being built.</param>
    private static void CopyNameTrees(
        PdfFileReader reader,
        PdfObjectCopier copier,
        PdfDictionary? source,
        PdfDictionary catalog)
    {
        var sourceNames = reader.ResolveDictionary(source?.Get("Names"));
        var names = new PdfDictionary();

        if (sourceNames?.Get(DestsEntry) is { } dests)
        {
            names.Set(DestsEntry, copier.Clone(dests));
        }

        if (sourceNames?.Get("EmbeddedFiles") is { } embedded)
        {
            names.Set("EmbeddedFiles", copier.Clone(embedded));
        }

        if (names.Count > 0)
        {
            catalog.Set("Names", names);
        }

        // The older catalog-level /Dests entry is still written by some producers.
        CopyFromSource(copier, source, catalog, DestsEntry);
    }

    /// <summary>Copies one catalog entry of the source into the catalog of the result.</summary>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="source">The catalog of the source.</param>
    /// <param name="target">The catalog being built.</param>
    /// <param name="key">The entry to copy.</param>
    private static void CopyFromSource(
        PdfObjectCopier copier,
        PdfDictionary? source,
        PdfDictionary target,
        string key)
    {
        if (source?.Get(key) is { } value)
        {
            target.Set(key, copier.Clone(value));
        }
    }

    /// <summary>Resolves the document information the result is written with.</summary>
    /// <param name="reader">The source document.</param>
    /// <param name="plan">The plan being applied.</param>
    /// <param name="modificationDate">The moment written as the modification date.</param>
    /// <returns>The document information of the result.</returns>
    private static PdfMetadata ResolveMetadata(
        PdfFileReader reader,
        PdfEditPlan plan,
        DateTimeOffset modificationDate)
    {
        var metadata = PdfDocumentInspection.ReadMetadata(reader);

        foreach (var operation in plan.Operations.OfType<PdfSetMetadataOperation>())
        {
            metadata = PdfMetadata.Merge(metadata, operation.Metadata);
        }

        if (plan.Output.Metadata is { } outputMetadata)
        {
            metadata = PdfMetadata.Merge(metadata, outputMetadata);
        }

        // The producer and the modification date always describe the document that was just written,
        // never the document it was made from.
        return metadata with
        {
            Producer = plan.Output.Producer,
            ModificationDate = modificationDate,
        };
    }

    /// <summary>Writes the document information dictionary and returns its object number.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="metadata">The document information of the result.</param>
    /// <returns>The object number of the document information dictionary.</returns>
    private static int WriteInformation(PdfWriter writer, PdfMetadata metadata)
    {
        var number = writer.AllocateObject();
        var information = new PdfDictionary();

        information.Set("Title", Text(metadata.Title));
        information.Set("Author", Text(metadata.Author));
        information.Set("Subject", Text(metadata.Subject));
        information.Set("Keywords", Text(metadata.Keywords));
        information.Set("Creator", Text(metadata.Creator));

        if (metadata.CreationDate is { } created)
        {
            information.Set("CreationDate", PdfString.FromText(PdfDate.Format(created)));
        }

        foreach (var property in metadata.CustomProperties.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            information.Set(property.Key, Text(property.Value));
        }

        information.Set("Producer", Text(metadata.Producer));
        information.Set(
            "ModDate",
            PdfString.FromText(PdfDate.Format(metadata.ModificationDate ?? DateTimeOffset.UtcNow)));

        writer.WriteObject(number, information);
        return number;
    }

    /// <summary>Writes the cross-reference table, the trailer and the closing bytes of the document.</summary>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="catalogNumber">The object number of the catalog.</param>
    /// <param name="infoNumber">The object number of the document information dictionary.</param>
    /// <param name="identifier">The identifier written as both elements of the <c>ID</c> entry.</param>
    /// <param name="security">The encryption of the result, or <see langword="null"/> when it is written in the clear.</param>
    private static void WriteTrailer(
        PdfWriter writer,
        int catalogNumber,
        int infoNumber,
        PdfString identifier,
        PdfSecurityWriter? security)
    {
        var trailer = new PdfDictionary();
        trailer.Set("Root", new PdfReference(catalogNumber, 0));
        trailer.Set("Info", new PdfReference(infoNumber, 0));
        trailer.Set("ID", new PdfArray(new PdfObject[] { identifier, identifier }));

        if (security is not null)
        {
            // The trailer is written in the clear, so its strings are never encrypted: a reader has to
            // reach the encryption dictionary before it has a key.
            trailer.Set("Encrypt", new PdfReference(writer.WriteEncryptionDictionary(security), 0));
        }

        writer.Complete(trailer);
    }

    /// <summary>Builds the identifier written twice in the trailer of the result.</summary>
    /// <param name="plan">The plan being applied.</param>
    /// <param name="version">The PDF version of the result.</param>
    /// <param name="pageCount">The number of pages the result contains.</param>
    /// <param name="modificationDate">The moment written as the modification date of the result.</param>
    /// <returns>The identifier as a hexadecimal string.</returns>
    /// <remarks>
    /// The seed describes the document that is being written rather than the document it came from:
    /// the operations it applies, its page count, its version and its moment are what make two edits
    /// of the same source reproducible. A fixed <see cref="PdfEditOutputOptions.ModificationDate"/>
    /// and <see cref="PdfEditOutputOptions.DocumentId"/> therefore produce the same bytes.
    /// </remarks>
    private static PdfString BuildFileIdentifier(
        PdfEditPlan plan,
        PdfVersion version,
        int pageCount,
        DateTimeOffset modificationDate)
    {
        var seed = string.Create(
            CultureInfo.InvariantCulture,
            $"{version}|{plan.Output.PdfAProfile}|{modificationDate:O}|{pageCount}|{plan.Output.DocumentId}|{string.Join(';', plan.Operations)}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        return new PdfString(hash.AsSpan(0, 16).ToArray(), isHexadecimal: true);
    }

    /// <summary>Creates a PDF string for a text entry, or nothing when the text is empty.</summary>
    /// <param name="value">The text, or <see langword="null"/>.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    private static PdfString? Text(string? value) =>
        string.IsNullOrEmpty(value) ? null : PdfString.FromText(value);
}
