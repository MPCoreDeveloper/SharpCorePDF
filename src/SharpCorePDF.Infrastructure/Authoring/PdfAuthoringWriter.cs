using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharpCorePDF.Domain.ReadModels;
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
    /// <param name="pages">The pages, in order: the ones the caller drew and the ones it imported.</param>
    /// <param name="metadata">The document information.</param>
    /// <param name="options">The options that control identity and reproducibility.</param>
    /// <param name="xmp">The XMP packet the catalog carries, or <see langword="null"/> for none.</param>
    /// <param name="bookmarks">The outline of the document, empty when it has none.</param>
    /// <returns>The bytes of the document.</returns>
    /// <exception cref="InvalidOperationException">There is no page, or the options are not usable.</exception>
    public static byte[] Write(
        IReadOnlyList<IAuthoredPage> pages,
        PdfMetadata metadata,
        PdfAuthoringOptions options,
        ReadOnlyMemory<byte>? xmp,
        IReadOnlyList<PdfOutlineItem> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(bookmarks);

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
            switch (page)
            {
                case DrawnPage drawn:
                    pageNumbers.Add(WriteDrawnPage(writer, resources, drawn.Canvas, pagesNumber, hash, size));
                    break;

                case ImportedPage imported:
                    pageNumbers.Add(WriteImportedPage(writer, imported, pagesNumber, hash));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"The page type '{page.GetType().Name}' is not one the authoring writer knows.");
            }
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

        var catalog = new PdfDictionary()
            .SetName("Type", "Catalog")
            .Set("Pages", new PdfReference(pagesNumber, 0));

        if (xmp is { Length: > 0 } packet)
        {
            // The packet is written as the caller stated it, uncompressed: a validator compares the
            // bytes it reads with the ones a producer signed, and PDF/A-1 forbids compressing them.
            var metadataNumber = writer.AllocateObject();

            writer.WriteObject(
                metadataNumber,
                new PdfStream(
                    new PdfDictionary().SetName("Type", "Metadata").SetName("Subtype", "XML"),
                    packet.ToArray()));

            catalog.Set("Metadata", new PdfReference(metadataNumber, 0));
            hash.AppendData(packet.Span);
        }

        if (bookmarks.Count > 0)
        {
            catalog.Set("Outlines", new PdfReference(
                PdfOutlineWriter.Write(writer, bookmarks, pageNumbers, hash),
                0));
        }

        writer.WriteObject(catalogNumber, catalog);

        var informationNumber = WriteInformation(writer, metadata, options, creation);
        var identifier = BuildIdentifier(hash, options);

        writer.Complete(
            new PdfDictionary()
                .Set("Root", new PdfReference(catalogNumber, 0))
                .Set("Info", new PdfReference(informationNumber, 0))
                .Set("ID", new PdfArray([identifier, identifier])));

        return buffer.ToArray();
    }

    /// <summary>Writes a page the caller drew, and returns the object number it was given.</summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="resources">The fonts and images of the document.</param>
    /// <param name="canvas">The canvas the page was drawn with.</param>
    /// <param name="pagesNumber">The object number of the page tree node.</param>
    /// <param name="hash">The digest that identifies the document.</param>
    /// <param name="size">Scratch space for the page size, which the digest receives.</param>
    /// <returns>The object number of the page.</returns>
    private static int WriteDrawnPage(
        PdfWriter writer,
        AuthoringResources resources,
        PdfPageCanvas canvas,
        int pagesNumber,
        IncrementalHash hash,
        Span<byte> size)
    {
        BinaryPrimitives.WriteDoubleBigEndian(size, canvas.Width);
        BinaryPrimitives.WriteDoubleBigEndian(size[8..], canvas.Height);
        hash.AppendData(size);

        var pageResources = resources.BeginPage();
        var content = PageContentWriter.Write(canvas, pageResources, hash);
        var contentNumber = writer.AllocateObject();

        writer.WriteObject(contentNumber, new PdfStream(new PdfDictionary(), content));

        var pageNumber = writer.AllocateObject();

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
                        new PdfReal(canvas.Width),
                        new PdfReal(canvas.Height),
                    ]))
                .Set("Resources", pageResources.ToDictionary())
                .Set("Contents", new PdfReference(contentNumber, 0)));

        return pageNumber;
    }

    /// <summary>
    /// Copies one page of another document into this one and returns the object number it was given.
    /// </summary>
    /// <param name="writer">The writer of the document.</param>
    /// <param name="page">The page to copy, with the reader of its document.</param>
    /// <param name="pagesNumber">The object number of the page tree node of this document.</param>
    /// <param name="hash">The digest that identifies the document.</param>
    /// <returns>The object number of the copied page.</returns>
    /// <remarks>
    /// The page, its content streams, its resources, its annotations, its rotation and everything else
    /// it refers to are copied as they are; the copier in the merge engine does the work, so an imported
    /// page keeps exactly what a merged page keeps. Attributes the page inherited from its own page tree
    /// are stated on the copy, because this document has a single flat page tree.
    /// </remarks>
    private static int WriteImportedPage(
        PdfWriter writer,
        ImportedPage page,
        int pagesNumber,
        IncrementalHash hash)
    {
        var reader = page.Reader;
        var sourcePages = reader.Pages;
        var source = sourcePages[page.PageNumber - 1];
        var copier = new PdfObjectCopier(reader, writer);
        var target = copier.Reserve(source.ObjectNumber);

        copier.Override(
            source.ObjectNumber,
            body => MaterialiseImportedPage(copier, body, source, pagesNumber));

        // A link or a bookmark that points at a page this document does not have would dangle, so every
        // other page of the source is folded onto the one that was imported, as a merge does.
        foreach (var other in sourcePages
            .Select(other => other.ObjectNumber)
            .Where(number => number != source.ObjectNumber))
        {
            copier.Redirect(other, target);
        }

        foreach (var node in reader.PageTree.NodeObjectNumbers)
        {
            copier.Redirect(node, pagesNumber);
        }

        hash.AppendData(reader.Data);
        copier.Flush();

        return target;
    }

    /// <summary>Turns the copied body of a source page into a page of this document.</summary>
    /// <param name="copier">The copier that maps the source onto this document.</param>
    /// <param name="body">The copied page dictionary.</param>
    /// <param name="page">The page as the source stated it, with what it inherited.</param>
    /// <param name="pagesNumber">The object number of the page tree node of this document.</param>
    /// <returns>The page dictionary of this document.</returns>
    private static PdfDictionary MaterialiseImportedPage(
        PdfObjectCopier copier,
        PdfObject body,
        SourcePage page,
        int pagesNumber)
    {
        var copy = body as PdfDictionary ?? new PdfDictionary();
        var imported = new PdfDictionary();

        foreach (var entry in page.Inherited.Entries.Where(entry => !copy.ContainsKey(entry.Key)))
        {
            imported.Set(entry.Key, copier.Clone(entry.Value));
        }

        foreach (var entry in copy.Entries)
        {
            imported.Set(entry.Key, entry.Value);
        }

        imported
            .Set("Type", new PdfName("Page"))
            .Set("Parent", new PdfReference(pagesNumber, 0));

        return imported;
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
