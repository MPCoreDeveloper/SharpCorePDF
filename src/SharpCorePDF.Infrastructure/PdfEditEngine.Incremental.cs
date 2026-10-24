using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Editing;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The incremental half of <see cref="PdfEditEngine"/>: what an edit appends to the bytes of its source
/// instead of rewriting them, and what it has to know about the source before it may.
/// </summary>
/// <remarks>
/// An incremental update is how a document keeps a signature: the contract promises that a signature
/// covering an earlier revision still covers it in the result, which is only true if not one byte of
/// that revision changed. The alternative — a complete rewrite — is still the default, and it reports
/// that a signature of the source cannot survive it.
/// </remarks>
public sealed partial class PdfEditEngine
{
    /// <summary>Refuses a source that cannot carry an appended revision.</summary>
    /// <param name="reader">The document being edited.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <exception cref="PdfEditPlanValidationException">The source is encrypted, or its cross-reference table was rebuilt by scanning.</exception>
    private static void EnsureIncrementalSource(PdfFileReader reader, string sourceName)
    {
        if (reader.IsEncrypted)
        {
            throw new PdfEditPlanValidationException(
                $"{sourceName} is encrypted, and an incremental update of an encrypted document is not supported: the appended objects would need the file key of the revision they follow. Write a complete rewrite instead.");
        }

        // A table that was rebuilt by scanning has no offset to chain to, and the offsets of a
        // revision are relative to the end of every earlier one.
        if (reader.CrossReferenceTable.WasReconstructed || reader.CrossReferenceTable.StartXrefOffset <= 0)
        {
            throw new PdfEditPlanValidationException(
                $"the cross-reference table of {sourceName} could not be read and was rebuilt by scanning the file, so an incremental update has nothing to chain to. Write a complete rewrite instead.");
        }
    }

    /// <summary>Determines whether the document carries a signature that covers its bytes.</summary>
    /// <param name="reader">The document to inspect.</param>
    /// <returns><see langword="true"/> when a signature field with a value, or a document-level signature, is present.</returns>
    /// <remarks>
    /// Only the shape of a signature is looked at, never its contents: the question the edit answers is
    /// whether it is about to invalidate one, not whether the one it finds is any good.
    /// </remarks>
    private static bool CarriesSignature(PdfFileReader reader)
    {
        var catalog = reader.Catalog;

        if (catalog is null)
        {
            return false;
        }

        // A certification signature is reached through /Perms rather than through a field.
        if (reader.ResolveDictionary(catalog.Get("Perms"))?.ContainsKey("DocMDP") == true)
        {
            return true;
        }

        var form = reader.ResolveDictionary(catalog.Get("AcroForm"));

        return form is not null && FieldsCarrySignature(reader, form.Get("Fields"), []);
    }

    /// <summary>Walks the field tree of an interactive form looking for a signed signature field.</summary>
    /// <param name="reader">The document the field belongs to.</param>
    /// <param name="fields">The <c>/Fields</c> or <c>/Kids</c> value of a field or of the form.</param>
    /// <param name="visited">The fields already walked, so a cyclic tree terminates.</param>
    /// <returns><see langword="true"/> when a signature field carries a value.</returns>
    private static bool FieldsCarrySignature(PdfFileReader reader, PdfObject? fields, HashSet<long> visited)
    {
        if (reader.ResolveArray(fields) is not { } array)
        {
            return false;
        }

        foreach (var item in array.Items)
        {
            if (item is PdfReference reference && !visited.Add(reference.ObjectNumber))
            {
                continue;
            }

            var field = reader.ResolveDictionary(item);

            if (field is null)
            {
                continue;
            }

            // A terminal field names its type; a parent field may hold it for its children. A
            // signature field that states a value holds a signature dictionary, and only then is
            // there a signature that a rewrite would invalidate.
            if (field.GetName("FT") is "Sig" && field.ContainsKey("V"))
            {
                return true;
            }

            if (field.ContainsKey("Kids") && FieldsCarrySignature(reader, field.Get("Kids"), visited))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Copies the source into the destination and opens the writer of the appended revision.</summary>
    /// <param name="destination">The stream the result is written to.</param>
    /// <param name="reader">The document being edited.</param>
    /// <param name="warnings">The list that receives the remark about the revision.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <returns>The writer of the revision.</returns>
    /// <remarks>
    /// The bytes that are copied are the ones the reader opened, not a second read of the stream, so a
    /// source that changes underneath the edit cannot make the offsets of the revision disagree with the
    /// bytes they were recorded against: what is appended to is what was read.
    /// </remarks>
    private IncrementalWriter AppendRevision(
        Stream destination,
        PdfFileReader reader,
        List<PdfWarning> warnings,
        string sourceName)
    {
        var revision = IncrementalWriter.Begin(
            destination,
            reader.Data,
            options,
            reader.CrossReferenceTable.StartXrefOffset,
            (int)reader.CrossReferenceTable.HighestObjectNumber);

        warnings.Add(new(
            PdfWarningKind.IncrementalUpdateAppended,
            $"{sourceName} was extended rather than rewritten: its {revision.PriorLength} byte(s) are part of the result unchanged, and the new objects follow them as one more revision."));

        return revision;
    }
}
