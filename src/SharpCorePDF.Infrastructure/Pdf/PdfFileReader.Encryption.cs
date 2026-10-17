using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The encryption half of <see cref="PdfFileReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// A document that declares <c>Encrypt</c> is opened once, while the reader is being constructed, so
/// that the decision to decrypt is made before any object is resolved and cannot be revisited later.
/// The two values that must stay in the clear are out of reach of this class by construction: the
/// file identifier and the <c>Encrypt</c> dictionary live in the trailer, which the cross-reference
/// reader parsed while the document was still closed, and a cross-reference stream is read by the
/// same reader rather than by this one.
/// </para>
/// <para>
/// The <c>Encrypt</c> dictionary is the one object that is resolved while the document is still
/// closed, deliberately, because its entries are the key material. It is therefore cached before
/// <see cref="Security"/> exists, and every later resolution of it returns those untouched bytes.
/// </para>
/// <para>
/// One value of an encrypted document must be read without decrypting it although it is an ordinary
/// object: the metadata stream of a document that clears <c>EncryptMetadata</c>, which the
/// specification keeps in the clear so that a tool can read the metadata of a document it cannot
/// otherwise open. The catalog is the only thing that names that object, and nothing can reach it
/// without passing through the catalog, so resolving the catalog is where it is noticed.
/// </para>
/// </remarks>
internal sealed partial class PdfFileReader
{
    /// <summary>
    /// The object that holds the metadata stream, when the document keeps that one stream in the
    /// clear. Zero — which is never an object number — when there is no such object.
    /// </summary>
    private long clearTextMetadataObjectNumber;

    /// <summary>
    /// The revision of the standard security handler the document declares, or
    /// <see langword="null"/> when it declares no integer <c>R</c> entry.
    /// </summary>
    /// <remarks>
    /// Read from the <c>Encrypt</c> dictionary before the handler decides anything, because the
    /// revision is a fact about the document rather than about the handler's ability to open it:
    /// a document protected by a filter the engine does not implement, or by a revision outside 2 to
    /// 6, still says which revision it uses, and a caller's decision depends on knowing that.
    /// </remarks>
    private int? declaredSecurityHandlerRevision;

    private void OpenEncryption(string? password)
    {
        if (!IsEncrypted)
        {
            return;
        }

        if (ResolveDictionary(CrossReferenceTable.Trailer.Get("Encrypt")) is not { } encryption)
        {
            warnings.Add(new(
                PdfWarningKind.EncryptionDictionaryUnreadable,
                $"'{SourceName}': the trailer declares encryption but its /Encrypt dictionary could not be read, so the document cannot be opened."));

            return;
        }

        // The declared revision survives every later refusal, which is why it is read here rather
        // than inside the handler: a handler is never constructed for an encryption the engine
        // cannot honour, and that is exactly the case a caller needs the revision for.
        declaredSecurityHandlerRevision =
            encryption.GetInteger("R") is { } declaredRevision ? (int)declaredRevision : null;

        var result = PdfSecurityHandler.TryCreate(
            encryption,
            ReadFileIdentifier(),
            SourceName,
            password,
            warnings);

        Security = result.Handler;
        RequiresPassword = result.RequiresPassword;
    }

    /// <summary>
    /// Notes the object that holds the metadata stream of a document that stores that stream in the
    /// clear, so that the reader can leave that one object alone.
    /// </summary>
    /// <param name="catalog">The catalog that was just resolved, or <see langword="null"/>.</param>
    /// <remarks>
    /// Only the metadata stream of a document of revision 4 or later can be stored in the clear; for
    /// every other document, and for a document that keeps its metadata encrypted, nothing is noted
    /// and the object is decrypted like any other. A catalog that names a metadata stream directly
    /// rather than through a reference is left alone as well: a stream is always an indirect object,
    /// so such an entry holds no bytes this class has to keep.
    /// </remarks>
    private void NoteClearTextMetadata(PdfDictionary? catalog)
    {
        if (Security is { EncryptsMetadata: false } && catalog?.Get("Metadata") is PdfReference reference)
        {
            clearTextMetadataObjectNumber = reference.ObjectNumber;
        }
    }

    /// <summary>
    /// Determines whether an object is the one the document stores in the clear although it declares
    /// encryption.
    /// </summary>
    /// <param name="objectNumber">The object number the object carries in the file.</param>
    /// <returns><see langword="true"/> when the object must be read as it stands.</returns>
    private bool IsClearTextMetadata(long objectNumber) => objectNumber == clearTextMetadataObjectNumber;

    /// <summary>
    /// Reads the first element of the trailer <c>ID</c>, the file identifier that the keys of
    /// revisions 2 to 4 are derived from.
    /// </summary>
    /// <returns>The identifier, or an empty array when the document declares none.</returns>
    private byte[] ReadFileIdentifier()
    {
        var identifier = CrossReferenceTable.Trailer.Get("ID");

        if (identifier is PdfReference reference)
        {
            identifier = Resolve(reference);
        }

        // The identifier is not encrypted — it is needed to derive the key in the first place — so it
        // is read before the document is opened, and this resolution happens before Security is set.
        return identifier is PdfArray { Items.Count: > 0 } array && array.Items[0] is PdfString value
            ? value.Bytes
            : [];
    }
}
