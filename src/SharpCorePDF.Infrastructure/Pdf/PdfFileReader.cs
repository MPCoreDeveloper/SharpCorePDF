using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Reads one PDF document: parses its header and cross-reference table, resolves indirect objects on
/// demand, and exposes its page tree.
/// </summary>
/// <remarks>
/// Every resolved object is cached, so resolving an object twice costs nothing. Objects stored inside
/// object streams are extracted the first time one of them is asked for.
/// </remarks>
internal sealed partial class PdfFileReader
{
    private readonly byte[] data;
    private readonly PdfEngineOptions options;
    private readonly List<PdfWarning> warnings = [];
    private readonly Dictionary<long, PdfObject?> resolved = [];
    private readonly Dictionary<long, PdfObject> fromObjectStreams = [];
    private readonly HashSet<long> loadedObjectStreams = [];
    private readonly HashSet<long> resolvingObjectStreams = [];
    private readonly HashSet<long> resolvingLengths = [];
    private PageTreeWalk? pageTree;
    private PdfDictionary? catalog;

    /// <summary>Initializes a new instance of the <see cref="PdfFileReader"/> class.</summary>
    /// <param name="data">The bytes of the document.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="options">The engine options.</param>
    /// <param name="password">The user or owner password, when the caller knows one.</param>
    /// <exception cref="SharpCorePDF.Domain.Exceptions.PdfSourceFormatException">The bytes are not a readable PDF document.</exception>
    /// <remarks>
    /// The cross-reference table is read without any decryption, because nothing it holds is
    /// encrypted, and the encryption is opened afterwards, so that every object resolved from then on
    /// is decrypted on the way in. That order is what keeps the <c>Encrypt</c> dictionary itself in
    /// the clear: it is read while the document is still closed.
    /// </remarks>
    public PdfFileReader(
        byte[] data,
        string sourceName,
        PdfEngineOptions options,
        string? password = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        SourceName = sourceName;

        Version = ReadVersion();
        CrossReferenceTable = new PdfCrossReferenceReader(data, sourceName, options, warnings).Read();
        OpenEncryption(password);
    }

    /// <summary>Gets the name of the source.</summary>
    public string SourceName { get; }

    /// <summary>Gets the raw bytes of the document.</summary>
    public byte[] Data => data;

    /// <summary>Gets the version from the file header.</summary>
    public PdfVersion Version { get; }

    /// <summary>Gets the merged cross-reference table.</summary>
    public PdfCrossReferenceTable CrossReferenceTable { get; }

    /// <summary>Gets non-fatal remarks collected while reading.</summary>
    public IReadOnlyList<PdfWarning> Warnings => warnings;

    /// <summary>Gets a value indicating whether the document declares encryption.</summary>
    public bool IsEncrypted => CrossReferenceTable.Trailer.ContainsKey("Encrypt");

    /// <summary>
    /// Gets a value indicating whether a password is needed to read the document: it is encrypted and
    /// the password that was supplied — possibly none at all — did not open it.
    /// </summary>
    public bool RequiresPassword { get; private set; }

    /// <summary>
    /// Gets the revision of the standard security handler the document declares, or
    /// <see langword="null"/> when the document declares no encryption or no integer <c>R</c> entry.
    /// </summary>
    /// <remarks>
    /// It is a property of the document rather than of the handler, so it is available for an
    /// encrypted document whose encryption stayed closed — which is the case a caller has to decide
    /// about before asking for a password.
    /// </remarks>
    public int? DeclaredSecurityHandlerRevision => declaredSecurityHandlerRevision;

    /// <summary>
    /// Gets the handler that decrypts the strings and streams of the document, or
    /// <see langword="null"/> when the document is not encrypted or its encryption stayed closed.
    /// </summary>
    /// <remarks>
    /// The handler is what proves the document can be read: a reader that has one exposes every
    /// string and every stream in the clear, and a reader without one that still reports
    /// <see cref="IsEncrypted"/> must not have its streams copied anywhere.
    /// </remarks>
    public PdfSecurityHandler? Security { get; private set; }

    /// <summary>Gets the reference to the document catalog.</summary>
    public PdfReference? RootReference => CrossReferenceTable.Trailer.GetReference("Root");

    /// <summary>Gets the object number of the document catalog, or zero.</summary>
    public long RootObjectNumber => RootReference?.ObjectNumber ?? 0;

    /// <summary>Gets the document catalog.</summary>
    /// <remarks>
    /// Resolving the catalog is also what identifies the object that holds the metadata stream of a
    /// document that stores that one stream in the clear, so this is a method rather than a one-line
    /// property.
    /// </remarks>
    public PdfDictionary? Catalog
    {
        get
        {
            if (catalog is null)
            {
                catalog = ResolveDictionary(RootReference);
                NoteClearTextMetadata(catalog);
            }

            return catalog;
        }
    }

    /// <summary>Gets the walked page tree.</summary>
    public PageTreeWalk PageTree => pageTree ??= PdfPageTree.Walk(this, warnings, options.MaxNestingDepth);

    /// <summary>Gets the pages of the document in document order.</summary>
    public IReadOnlyList<SourcePage> Pages => PageTree.Pages;

    /// <summary>Gets the document information dictionary.</summary>
    public PdfDictionary? Information => ResolveDictionary(CrossReferenceTable.Trailer.Get("Info"));

    /// <summary>Resolves an indirect object.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <returns>The object, or <see langword="null"/> when it does not exist.</returns>
    public PdfObject? Resolve(long objectNumber)
    {
        if (objectNumber < 0)
        {
            return null;
        }

        if (resolved.TryGetValue(objectNumber, out var cached))
        {
            return cached;
        }

        if (fromObjectStreams.TryGetValue(objectNumber, out var streamed))
        {
            return streamed;
        }

        var entry = CrossReferenceTable.Get(objectNumber);

        if (entry is not { } xref || !xref.IsInUse)
        {
            resolved[objectNumber] = null;
            return null;
        }

        var value = xref.Type == XrefEntryType.Compressed
            ? ResolveFromObjectStream(xref)
            : ParseObjectAt((int)xref.Offset, objectNumber);

        resolved[objectNumber] = value;
        return value;
    }

    /// <summary>Resolves an indirect reference.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The object, or <see langword="null"/>.</returns>
    public PdfObject? Resolve(PdfReference? reference) => reference is null ? null : Resolve(reference.ObjectNumber);

    /// <summary>Resolves a value that may be a reference, leaving anything else as it is.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The object the value refers to, or the value itself when it is not a reference.</returns>
    public PdfObject? Resolve(PdfObject? value) => value is PdfReference reference ? Resolve(reference) : value;

    /// <summary>Resolves a value that may be a dictionary or a reference to one.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public PdfDictionary? ResolveDictionary(PdfObject? value) => value switch
    {
        PdfDictionary dictionary => dictionary,
        PdfReference reference => Resolve(reference) as PdfDictionary,
        _ => null,
    };

    /// <summary>Resolves a value that may be an array or a reference to one.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The array, or <see langword="null"/>.</returns>
    public PdfArray? ResolveArray(PdfObject? value) => value switch
    {
        PdfArray array => array,
        PdfReference reference => Resolve(reference) as PdfArray,
        _ => null,
    };
}
