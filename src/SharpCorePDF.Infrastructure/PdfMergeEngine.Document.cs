using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The document structure half of <see cref="PdfMergeEngine"/>: the catalog, the features that are
/// carried over from the primary source, the document information and the trailer.
/// </summary>
public sealed partial class PdfMergeEngine
{
    /// <summary>The name of the tree of named destinations, which is carried over as a whole.</summary>
    private const string DestsEntry = "Dests";

    /// <summary>Builds the catalog of the merged document.</summary>
    /// <param name="context">The merge context.</param>
    /// <param name="outlines">The root of the merged outline, when there is one.</param>
    /// <returns>The catalog.</returns>
    /// <remarks>
    /// Features that can only exist once in a document — viewer preferences, the interactive form, the
    /// name trees of named destinations and embedded files, the page labels — are taken from the
    /// primary source. Everything that can exist per page (content, annotations) has already travelled
    /// with the pages themselves.
    /// </remarks>
    private static PdfDictionary BuildCatalog(PdfMergeContext context, PdfReference? outlines)
    {
        var primary = context.PrimarySource;
        var source = primary.Reader.Catalog;

        var catalog = new PdfDictionary();
        catalog.Set("Type", new PdfName("Catalog"));
        catalog.Set("Pages", new PdfReference(context.PagesNodeNumber, 0));

        CopyFromPrimary(primary, source, catalog, "ViewerPreferences");
        CopyFromPrimary(primary, source, catalog, "PageLayout");
        CopyFromPrimary(primary, source, catalog, "PageMode");
        CopyFromPrimary(primary, source, catalog, "Lang");
        CopyFromPrimary(primary, source, catalog, "MarkInfo");
        CopyFromPrimary(primary, source, catalog, "OutputIntents");
        CopyFromPrimary(primary, source, catalog, DestsEntry);

        if (context.Output.MetadataPolicy != MetadataPolicy.None)
        {
            CopyFromPrimary(primary, source, catalog, "Metadata");
        }

        if (context.Output.PreserveAcroForm)
        {
            CopyFromPrimary(primary, source, catalog, "AcroForm");
        }

        if (context.Output.PageLabelPolicy == PageLabelPolicy.FromPrimarySource)
        {
            CopyFromPrimary(primary, source, catalog, "PageLabels");
        }

        CopyNameTrees(context, primary, catalog);

        if (outlines is not null)
        {
            catalog.Set("Outlines", outlines);
        }

        return catalog;
    }

    /// <summary>
    /// Copies named destinations and embedded files from the primary source, and notes that the named
    /// destinations of the other sources were left behind.
    /// </summary>
    /// <param name="context">The merge context.</param>
    /// <param name="primary">The primary source.</param>
    /// <param name="catalog">The catalog being built.</param>
    private static void CopyNameTrees(PdfMergeContext context, MergeSource primary, PdfDictionary catalog)
    {
        var names = new PdfDictionary();
        var sourceNames = primary.Reader.ResolveDictionary(primary.Reader.Catalog?.Get("Names"));

        if (sourceNames?.Get(DestsEntry) is { } dests)
        {
            names.Set(DestsEntry, primary.Copier.Clone(dests));
        }

        if (context.Output.PreserveEmbeddedFiles && sourceNames?.Get("EmbeddedFiles") is { } embedded)
        {
            names.Set("EmbeddedFiles", primary.Copier.Clone(embedded));
        }

        if (names.Count > 0)
        {
            catalog.Set("Names", names);
        }

        foreach (var other in context.Sources.Where(candidate => !ReferenceEquals(candidate, primary)))
        {
            var otherNames = other.Reader.ResolveDictionary(other.Reader.Catalog?.Get("Names"));

            if (otherNames?.Get(DestsEntry) is not null || other.Reader.Catalog?.Get(DestsEntry) is not null)
            {
                context.Warnings.Add(new(
                    PdfWarningKind.NamedDestinationsDropped,
                    $"'{other.DisplayName}': its named destinations were not carried over, because named destinations are merged from the primary source only."));
            }
        }
    }

    /// <summary>Copies one catalog entry of the primary source into the catalog of the result.</summary>
    /// <param name="primary">The primary source.</param>
    /// <param name="source">The catalog of the primary source.</param>
    /// <param name="target">The catalog being built.</param>
    /// <param name="key">The entry to copy.</param>
    private static void CopyFromPrimary(
        MergeSource primary,
        PdfDictionary? source,
        PdfDictionary target,
        string key)
    {
        if (source?.Get(key) is not { } value)
        {
            return;
        }

        target.Set(key, primary.Copier.Clone(value));
    }
}
