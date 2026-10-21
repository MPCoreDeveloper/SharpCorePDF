namespace SharpCorePDF.Domain.ValueObjects;

/// <summary>
/// Identifies the kind of a <see cref="PdfWarning"/>: the condition that produced it, as opposed to
/// the wording of the remark.
/// </summary>
/// <remarks>
/// <para>
/// This is the contract a caller matches on. Every member documents the condition that produces it,
/// while the wording of <see cref="PdfWarning.Message"/> may be rewritten in any release without that
/// being a breaking change — which is what makes "no limitation is silent" testable against a kind
/// instead of against a sentence.
/// </para>
/// <para>
/// The set is open. A later release may add a kind, which is additive rather than breaking, so a
/// <c>switch</c> over this enum must keep a <c>default</c> arm.
/// </para>
/// </remarks>
public enum PdfWarningKind
{
    /// <summary>
    /// The cross-reference table could not be read and was rebuilt by scanning the file for object
    /// headers. The document may be damaged.
    /// </summary>
    CrossReferenceRebuilt,

    /// <summary>
    /// A cross-reference entry pointed at the header of a different object than the one it named.
    /// The object found at that offset was used.
    /// </summary>
    CrossReferenceEntryMismatch,

    /// <summary>
    /// An entry claimed its object lives inside an object stream, but the object holding it was not
    /// a stream, so the objects of that container are treated as missing.
    /// </summary>
    ObjectStreamUnreadable,

    /// <summary>
    /// An object stream's header ended before the number of objects it declared, so the objects whose
    /// entries could not be read are missing from the document.
    /// </summary>
    ObjectStreamTruncated,

    /// <summary>The document catalog names no page tree, so the document contributes no pages.</summary>
    PageTreeMissing,

    /// <summary>
    /// The page tree is deeper than <c>PdfEngineOptions.MaxNestingDepth</c> and was cut off at that
    /// depth.
    /// </summary>
    PageTreeTooDeep,

    /// <summary>The page tree contains a cycle; the branch that repeated an object was skipped.</summary>
    PageTreeCycle,

    /// <summary>A page tree node could not be resolved and was skipped with everything below it.</summary>
    PageTreeBranchUnreadable,

    /// <summary>
    /// An object with children is not typed as a page tree node. Its children were still visited,
    /// because producers are known to omit the type.
    /// </summary>
    PageTreeChildNotANode,

    /// <summary>
    /// A page stored inline in the page tree cannot be referenced and was skipped.
    /// </summary>
    InlinePageSkipped,

    /// <summary>A page has no readable <c>/MediaBox</c>, so it is described with the A4 size.</summary>
    PageSizeAssumed,

    /// <summary>
    /// The trailer declares encryption but its <c>/Encrypt</c> dictionary could not be read, so the
    /// document cannot be opened.
    /// </summary>
    EncryptionDictionaryUnreadable,

    /// <summary>
    /// The document declares encryption that stayed closed, so its page tree cannot be read and it
    /// cannot be merged. The <see cref="ReadModels.PdfDocumentInfo.RequiresPassword"/> flag says
    /// whether a password is what stood in the way.
    /// </summary>
    EncryptionNotOpened,

    /// <summary>
    /// The document names a security handler, or a security handler sub-filter, that the engine does
    /// not implement.
    /// </summary>
    EncryptionHandlerUnsupported,

    /// <summary>
    /// The document declares a security handler <c>/V</c> and <c>/R</c> combination that the engine
    /// does not implement.
    /// </summary>
    EncryptionRevisionUnsupported,

    /// <summary>
    /// The document declares a crypt filter and a security handler <c>/V</c> that the engine cannot
    /// combine, for example AES-256 under a revision that does not define it.
    /// </summary>
    EncryptionCombinationUnsupported,

    /// <summary>
    /// The encryption dictionary lacks an entry its revision requires, such as <c>/O</c>, <c>/U</c>
    /// or the entries a <c>/V 5</c> document needs.
    /// </summary>
    EncryptionDictionaryIncomplete,

    /// <summary>
    /// The encryption dictionary names a crypt filter in <c>/StrF</c> or <c>/StmF</c> without
    /// defining it.
    /// </summary>
    CryptFilterUndefined,

    /// <summary>A crypt filter names a <c>/CFM</c> that the engine does not implement.</summary>
    CryptFilterMethodUnsupported,

    /// <summary>
    /// The document is encrypted and was opened with the password that was supplied; its strings and
    /// streams are read decrypted. This is the success case, reported so that a caller knows the
    /// document was not simply unprotected.
    /// </summary>
    EncryptionOpened,

    /// <summary>
    /// A secondary source's named destinations were not carried over, because named destinations are
    /// merged from the primary source only.
    /// </summary>
    NamedDestinationsDropped,

    /// <summary>The configured <c>OutlinePolicy</c> dropped the bookmarks of a source.</summary>
    OutlinesDroppedByPolicy,

    /// <summary>A bookmark that is not stored as an indirect object cannot be referenced and was skipped.</summary>
    BookmarkSkipped,

    /// <summary>A source carried more bookmarks than the engine copies, and the rest was skipped.</summary>
    BookmarksTruncated,

    /// <summary>
    /// A bookmark that pointed at a named destination was kept, but without its destination, because
    /// named destinations are merged from the primary source only.
    /// </summary>
    BookmarkDestinationDropped,

    /// <summary>
    /// The merged document was read back after it was written and matched what was written. Reported
    /// only when <c>PdfEngineOptions.VerifyOutput</c> is enabled.
    /// </summary>
    OutputVerified,

    /// <summary>
    /// A content stream could not be decoded, so the page it belongs to reports no images from it. The
    /// stream is named in the message; the rest of the page and the rest of the document are readable.
    /// </summary>
    ContentStreamUnreadable,

    /// <summary>
    /// An image declares a filter this library does not decode, such as <c>JPXDecode</c> or
    /// <c>CCITTFaxDecode</c>. The image is still returned with its raw bytes and its filter named, and
    /// <c>IsDecodable</c> is cleared.
    /// </summary>
    ImageFilterUnsupported,

    /// <summary>
    /// An image's geometry or payload could not be read — a missing <c>/Width</c>, a truncated stream,
    /// a colour space that cannot be described. The image is still returned with what is known.
    /// </summary>
    ImageDataUnreadable,

    /// <summary>
    /// A content stream paints a named XObject that the page's resources do not define, so the object
    /// it names could not be read.
    /// </summary>
    XObjectResourceMissing,

    /// <summary>
    /// A link or a bookmark names a destination that does not resolve in this document. The entry is
    /// returned with the name that failed and no page, and the call does not throw.
    /// </summary>
    NamedDestinationUnresolved,

    /// <summary>
    /// An embedded file's payload could not be read, or the file specification that names it could not
    /// be resolved. The entry is returned with what is known.
    /// </summary>
    EmbeddedFileUnreadable,

    /// <summary>
    /// Two entries of the filename tree share a name. Both are returned with distinct identifiers and
    /// the collision is reported, so neither is silently dropped.
    /// </summary>
    EmbeddedFileNameCollision,

    /// <summary>
    /// A document carries an XMP packet that is not well-formed XML. The raw packet is still returned
    /// byte-exact and the parsed values are empty.
    /// </summary>
    XmpPacketMalformed,

    /// <summary>
    /// A form field could not be read — a field dictionary that does not resolve, or a name that
    /// cannot be built. The field is skipped and the rest of the form is still returned.
    /// </summary>
    FormFieldUnreadable,

    /// <summary>
    /// An annotation could not be read — its dictionary does not resolve, so its subtype and
    /// rectangle are unknown. The annotation is skipped and the rest of the page is still returned.
    /// </summary>
    AnnotationUnreadable,

    /// <summary>
    /// A glyph of a page could not be turned into text: the font resource the content stream selects
    /// is missing, the font states no encoding this library understands, or a composite font carries no
    /// <c>/ToUnicode</c> map. The glyph is skipped and a character is never guessed, so the block that
    /// loses it still carries the text around it and the warning says which font was in the way. A
    /// document whose text uses an encoding this library cannot map therefore reads as no text plus
    /// this warning, not as silence.
    /// </summary>
    TextUnreadable,
}
