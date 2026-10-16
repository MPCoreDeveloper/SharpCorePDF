using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The document information and trailer half of <see cref="PdfMergeEngine"/>.
/// </summary>
public sealed partial class PdfMergeEngine
{
    /// <summary>Writes the document information dictionary and returns its object number.</summary>
    /// <param name="context">The merge context.</param>
    /// <returns>The object number of the document information dictionary.</returns>
    private static int WriteInformation(PdfMergeContext context)
    {
        var output = context.Output;
        var metadata = SelectMetadata(context);
        var number = context.Writer.AllocateObject();
        var information = new PdfDictionary();

        // What the policy decides is where the values come from; an explicit override always wins, so
        // this writes whatever SelectMetadata produced and leaves the rest of the entries out.
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

        // The producer and the modification date always describe the document that was just written,
        // never the document it was made from.
        information.Set("Producer", Text(output.Producer));
        information.Set("ModDate", PdfString.FromText(PdfDate.Format(context.ModificationDate)));

        context.Writer.WriteObject(number, information);
        return number;
    }

    /// <summary>Determines the document information of the result from the configured policy.</summary>
    /// <param name="context">The merge context.</param>
    /// <returns>The document information, before the producer and the modification date are applied.</returns>
    private static PdfMetadata SelectMetadata(PdfMergeContext context)
    {
        var primary = context.PrimarySource.Resolved.Info.Metadata;
        var policy = context.Output.MetadataPolicy;

        var metadata = policy switch
        {
            MetadataPolicy.None => PdfMetadata.Empty,

            // The primary source leads: a later source only fills a field that is still empty.
            MetadataPolicy.Merge => context.Sources.Aggregate(
                primary,
                (accumulated, source) => PdfMetadata.Merge(source.Resolved.Info.Metadata, accumulated)),

            _ => primary,
        };

        if (context.Output.MetadataOverride is { } overlay)
        {
            metadata = PdfMetadata.Merge(metadata, overlay);
        }

        return metadata with
        {
            Producer = context.Output.Producer,
            ModificationDate = context.Output.MetadataOverride?.ModificationDate ?? context.ModificationDate,
        };
    }

    /// <summary>Writes the cross-reference table, the trailer and the closing bytes of the document.</summary>
    /// <param name="context">The merge context.</param>
    /// <param name="infoNumber">The object number of the document information dictionary.</param>
    /// <param name="identifier">The identifier written as both elements of the <c>ID</c> entry.</param>
    /// <param name="security">The encryption of the document, or <see langword="null"/> when it is written in the clear.</param>
    private static void WriteTrailer(
        PdfMergeContext context,
        int infoNumber,
        PdfString identifier,
        PdfSecurityWriter? security)
    {
        var trailer = new PdfDictionary();

        trailer.Set("Root", new PdfReference(context.CatalogNumber, 0));
        trailer.Set("Info", new PdfReference(infoNumber, 0));
        trailer.Set("ID", new PdfArray(new PdfObject[] { identifier, identifier }));

        if (security is not null)
        {
            // The trailer is written in the clear, which is why its strings are never encrypted: the
            // identifier is part of the key derivation of the legacy revisions, so a reader has to be
            // able to read it before it has a key. The encryption dictionary it points at is the other
            // value that is deliberately in the clear, and the writer writes it as such.
            trailer.Set("Encrypt", new PdfReference(context.Writer.WriteEncryptionDictionary(security), 0));
        }

        context.Writer.Complete(trailer);
    }
}
