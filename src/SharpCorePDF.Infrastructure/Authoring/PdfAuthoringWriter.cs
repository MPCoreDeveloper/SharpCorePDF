using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Assembles the pages of a <see cref="PdfDocumentBuilder"/> into a complete PDF document.
/// </summary>
/// <remarks>
/// The document uses the same writer as a merge: a classic cross-reference table, a document
/// information dictionary and an identifier in the trailer. It is written uncompressed because the
/// content of an authored page is small, and every stream the document carries — an image, a font
/// program — is Flate compressed by itself.
/// </remarks>
internal static class PdfAuthoringWriter
{
    /// <summary>Writes an authored document.</summary>
    /// <param name="pages">The pages, in order.</param>
    /// <param name="metadata">The document information.</param>
    /// <param name="options">The options that control identity and reproducibility.</param>
    /// <returns>The bytes of the document.</returns>
    /// <exception cref="InvalidOperationException">There is no page, or the options are not usable.</exception>
    public static byte[] Write(
        IReadOnlyList<PdfPageCanvas> pages,
        PdfMetadata metadata,
        PdfAuthoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        var problems = options.Validate();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The authoring options are not usable: {string.Join(" ", problems)}");
        }

        if (pages.Count == 0)
        {
            throw new InvalidOperationException(
                "An authored document needs at least one page; call AddPage before Build.");
        }

        var creation = options.CreationDate ?? metadata.CreationDate ?? DateTimeOffset.UtcNow;

        using var buffer = new MemoryStream();
        var writer = new PdfWriter(buffer, PdfVersion.V1_7, PdfEngineOptions.Default);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        hash.AppendData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"SharpCorePDF authored document|{creation:O}|{pages.Count}")));

        var resources = new AuthoringResources(writer);
        var catalogNumber = writer.AllocateObject();
        var pagesNumber = writer.AllocateObject();
        var pageNumbers = new List<int>(pages.Count);
        Span<byte> size = stackalloc byte[16];

        foreach (var page in pages)
        {
            BinaryPrimitives.WriteDoubleBigEndian(size, page.Width);
            BinaryPrimitives.WriteDoubleBigEndian(size[8..], page.Height);
            hash.AppendData(size);

            var pageResources = resources.BeginPage();
            var content = PageContentWriter.Write(page, pageResources, hash);
            var contentNumber = writer.AllocateObject();

            writer.WriteObject(contentNumber, new PdfStream(new PdfDictionary(), content));

            var pageNumber = writer.AllocateObject();
            pageNumbers.Add(pageNumber);

            writer.WriteObject(
                pageNumber,
                new PdfDictionary()
                    .SetName("Type", "Page")
                    .Set("Parent", new PdfReference(pagesNumber, 0))
                    .Set(
                        "MediaBox",
                        new PdfArray(
                        [
                            new PdfInteger(0),
                            new PdfInteger(0),
                            new PdfReal(page.Width),
                            new PdfReal(page.Height),
                        ]))
                    .Set("Resources", pageResources.ToDictionary())
                    .Set("Contents", new PdfReference(contentNumber, 0)));
        }

        resources.WriteObjects(hash);

        hash.AppendData(Encoding.UTF8.GetBytes(
            $"{metadata.Title}|{metadata.Author}|{metadata.Subject}|{metadata.Keywords}|{metadata.Creator}|{options.Producer}"));

        writer.WriteObject(
            pagesNumber,
            new PdfDictionary()
                .SetName("Type", "Pages")
                .Set("Kids", new PdfArray(pageNumbers.Select(number => (PdfObject)new PdfReference(number, 0))))
                .SetInteger("Count", pageNumbers.Count));

        writer.WriteObject(
            catalogNumber,
            new PdfDictionary()
                .SetName("Type", "Catalog")
                .Set("Pages", new PdfReference(pagesNumber, 0)));

        var informationNumber = WriteInformation(writer, metadata, options, creation);
        var identifier = BuildIdentifier(hash, options);

        writer.Complete(
            new PdfDictionary()
                .Set("Root", new PdfReference(catalogNumber, 0))
                .Set("Info", new PdfReference(informationNumber, 0))
                .Set("ID", new PdfArray([identifier, identifier])));

        return buffer.ToArray();
    }

    /// <summary>Writes the document information dictionary.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="metadata">The values the caller supplied.</param>
    /// <param name="options">The authoring options, which state the producer.</param>
    /// <param name="creation">The creation date the document is stamped with.</param>
    /// <returns>The object number of the dictionary.</returns>
    private static int WriteInformation(
        PdfWriter writer,
        PdfMetadata metadata,
        PdfAuthoringOptions options,
        DateTimeOffset creation)
    {
        var number = writer.AllocateObject();
        var information = new PdfDictionary()
            .Set("Title", Text(metadata.Title))
            .Set("Author", Text(metadata.Author))
            .Set("Subject", Text(metadata.Subject))
            .Set("Keywords", Text(metadata.Keywords))
            .Set("Creator", Text(metadata.Creator));

        foreach (var property in metadata.CustomProperties.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            information.Set(property.Key, Text(property.Value));
        }

        // The producer and the dates always describe the document that was just written, so they are
        // written after the custom properties and cannot be replaced by one of them.
        information
            .Set("Producer", Text(options.Producer))
            .Set("CreationDate", PdfString.FromText(PdfDate.Format(creation)))
            .Set("ModDate", PdfString.FromText(PdfDate.Format(metadata.ModificationDate ?? creation)));

        writer.WriteObject(number, information);
        return number;
    }

    /// <summary>Builds the identifier written twice in the trailer.</summary>
    /// <param name="hash">The digest of the document so far.</param>
    /// <param name="options">The options, which may state the identifier explicitly.</param>
    /// <returns>The identifier as a hexadecimal string.</returns>
    private static PdfString BuildIdentifier(IncrementalHash hash, PdfAuthoringOptions options)
    {
        if (options.DocumentId is { } explicitId)
        {
            return new PdfString(Convert.FromHexString(explicitId), isHexadecimal: true);
        }

        return new PdfString(hash.GetHashAndReset()[..16], isHexadecimal: true);
    }

    /// <summary>Creates a PDF string for a text entry, or nothing when the text is empty.</summary>
    /// <param name="value">The text, or <see langword="null"/>.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    private static PdfString? Text(string? value) =>
        string.IsNullOrEmpty(value) ? null : PdfString.FromText(value);
}
