using System.Text;
using System.Xml;
using System.Xml.Linq;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The metadata half of <see cref="PdfPageContentReader"/>: the XMP packet a document carries and the
/// files it embeds.
/// </summary>
public sealed partial class PdfPageContentReader
{
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private const string PdfNamespace = "http://ns.adobe.com/pdf/1.3/";
    private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <inheritdoc />
    public async Task<PdfXmpResult> ReadMetadataAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        NoteClosedEncryption(reader, warnings);

        if (reader.Resolve(reader.Catalog?.Get("Metadata")) is not PdfStream stream)
        {
            return new PdfXmpResult { Warnings = [.. reader.Warnings, .. warnings] };
        }

        if (!TryDecodeStream(stream, out var packet))
        {
            warnings.Add(new(
                PdfWarningKind.XmpPacketMalformed,
                $"'{reader.SourceName}': the metadata stream could not be decoded, so its packet cannot be returned."));

            return new PdfXmpResult { Warnings = [.. reader.Warnings, .. warnings] };
        }

        return new PdfXmpResult
        {
            Metadata = new PdfXmpMetadata
            {
                RawPacket = packet,
                Text = Encoding.UTF8.GetString(packet),
                Properties = ParseXmp(packet, reader.SourceName, warnings),
            },
            Warnings = [.. reader.Warnings, .. warnings],
        };
    }

    /// <summary>Parses the values of the three namespaces the read model covers.</summary>
    /// <param name="packet">The bytes of the packet.</param>
    /// <param name="sourceName">The name of the source, used in diagnostics.</param>
    /// <param name="warnings">The list that receives a remark when the packet is not well-formed.</param>
    /// <returns>The values that were parsed, keyed by prefixed name.</returns>
    /// <remarks>
    /// Parsing is deliberately tolerant of a packet that says more than this library knows: every
    /// element in the three namespaces becomes a value, and the rest is left in the raw bytes. A
    /// packet that is not XML at all is reported and yields no values, which is what "the raw packet
    /// survives a broken parse" means in practice.
    /// </remarks>
    private static Dictionary<string, string> ParseXmp(
        byte[] packet,
        string sourceName,
        List<PdfWarning> warnings)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var input = new MemoryStream(packet, writable: false);
            var document = XDocument.Load(input);

            foreach (var property in document
                .Descendants()
                .Where(element => element.Name.NamespaceName is DcNamespace or PdfNamespace or XmpNamespace))
            {
                var prefix = property.Name.NamespaceName switch
                {
                    DcNamespace => "dc",
                    PdfNamespace => "pdf",
                    _ => "xmp",
                };

                var value = ExtractValue(property);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    properties[$"{prefix}:{property.Name.LocalName}"] = value;
                }
            }
        }
        catch (XmlException)
        {
            warnings.Add(new(
                PdfWarningKind.XmpPacketMalformed,
                $"'{sourceName}': the XMP packet is not well-formed XML, so its values could not be parsed."));
        }

        return properties;
    }

    /// <summary>Reads the text of a property, understanding the list form XMP uses.</summary>
    /// <param name="property">The property element.</param>
    /// <returns>The text of the property.</returns>
    private static string ExtractValue(XElement property)
    {
        var items = property.Descendants(XName.Get("li", RdfNamespace)).ToList();

        if (items.Count == 0)
        {
            return property.Value.Trim();
        }

        // A language alternative prefers the default, which is the one a reader shows.
        var preferred = items.FirstOrDefault(item =>
            string.Equals((string?)item.Attribute(XName.Get("lang", "http://www.w3.org/XML/1998/namespace")), "x-default", StringComparison.Ordinal));

        return preferred is not null
            ? preferred.Value.Trim()
            : string.Join(", ", items.Select(item => item.Value.Trim()));
    }

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfEmbeddedFileInfo>> ReadEmbeddedFilesAsync(
        string sourceName,
        Stream content,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        NoteClosedEncryption(reader, warnings);

        var files = new List<PdfEmbeddedFileInfo>();
        var entries = new List<KeyValuePair<string, PdfObject>>();
        var names = reader.ResolveDictionary(reader.Catalog?.Get("Names"));

        CollectNameTree(
            reader,
            reader.ResolveDictionary(names?.Get("EmbeddedFiles")),
            (name, value) => entries.Add(new(name, value)),
            0);

        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            files.Add(BuildEmbeddedFile(reader, entry.Key, entry.Value, used, warnings));
        }

        return Result(reader, files, warnings);
    }

    /// <inheritdoc />
    public async Task CopyEmbeddedFileDataAsync(
        string sourceName,
        Stream content,
        PdfEmbeddedFileInfo file,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var stream = FindEmbeddedStream(reader, file.Id);

        if (stream is null)
        {
            throw new ArgumentException(
                $"'{file.Id}' is not an embedded file of '{sourceName}'.",
                nameof(file));
        }

        await PdfImageFilters
            .CopyAsync(stream, destination, reader.SourceName, cancellationToken)
            .ConfigureAwait(false);

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Walks a name tree, in document order, and reports every leaf it holds.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="node">The root of the tree, or <see langword="null"/>.</param>
    /// <param name="add">Receives each name and the value the tree stores for it.</param>
    /// <param name="depth">The depth of the node.</param>
    private static void CollectNameTree(
        PdfFileReader reader,
        PdfDictionary? node,
        Action<string, PdfObject> add,
        int depth)
    {
        if (node is null || depth > 32)
        {
            return;
        }

        if (reader.ResolveArray(node.Get("Names")) is { } names)
        {
            for (var index = 0; index + 1 < names.Count; index += 2)
            {
                var name = ReadText(reader, names.Items[index]);

                if (name is not null)
                {
                    add(name, names.Items[index + 1]);
                }
            }
        }

        if (reader.ResolveArray(node.Get("Kids")) is not { } kids)
        {
            return;
        }

        foreach (var kid in kids.Items)
        {
            CollectNameTree(reader, reader.ResolveDictionary(kid), add, depth + 1);
        }
    }

    /// <summary>Finds the stream of the embedded file a file specification names.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="specification">The file specification, or <see langword="null"/>.</param>
    /// <returns>The embedded stream, or <see langword="null"/>.</returns>
    private static PdfStream? FindEmbeddedStream(PdfFileReader reader, PdfDictionary? specification)
    {
        var embedded = reader.ResolveDictionary(specification?.Get("EF"));

        return reader.Resolve(embedded?.Get("UF")) as PdfStream
            ?? reader.Resolve(embedded?.Get("F")) as PdfStream;
    }

    /// <summary>Finds the embedded stream whose identifier is the one asked for.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="id">The identifier of the file.</param>
    /// <returns>The stream, or <see langword="null"/> when the document has no such file.</returns>
    private static PdfStream? FindEmbeddedStream(PdfFileReader reader, string id)
    {
        var entries = new List<KeyValuePair<string, PdfObject>>();
        var names = reader.ResolveDictionary(reader.Catalog?.Get("Names"));

        CollectNameTree(
            reader,
            reader.ResolveDictionary(names?.Get("EmbeddedFiles")),
            (name, value) => entries.Add(new(name, value)),
            0);

        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var specification = reader.ResolveDictionary(entry.Value);
            var fileName = ReadFileName(reader, specification, entry.Key);
            var identifier = fileName;

            if (used.TryGetValue(fileName, out var count))
            {
                used[fileName] = count + 1;
                identifier = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{fileName}#{count + 1}");
            }
            else
            {
                used[fileName] = 1;
            }

            if (string.Equals(identifier, id, StringComparison.Ordinal))
            {
                return FindEmbeddedStream(reader, specification);
            }
        }

        return null;
    }

    /// <summary>Reads the name a file specification gives the file.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="specification">The specification, or <see langword="null"/>.</param>
    /// <param name="fallback">The name of the tree entry.</param>
    /// <returns>The name of the file.</returns>
    private static string ReadFileName(PdfFileReader reader, PdfDictionary? specification, string fallback) =>
        (specification is null
            ? null
            : ReadText(reader, specification.Get("UF")) ?? ReadText(reader, specification.Get("F")))
        ?? fallback;

    /// <summary>Builds the description of one embedded file, assigning it a distinct identifier.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="name">The name of the tree entry.</param>
    /// <param name="value">The value the tree stores, a file specification.</param>
    /// <param name="used">The names already given out, so a collision is numbered instead of dropped.</param>
    /// <param name="warnings">The list that receives the remarks made while reading.</param>
    /// <returns>The file.</returns>
    private PdfEmbeddedFileInfo BuildEmbeddedFile(
        PdfFileReader reader,
        string name,
        PdfObject value,
        Dictionary<string, int> used,
        List<PdfWarning> warnings)
    {
        var specification = reader.ResolveDictionary(value);
        var fileName = ReadFileName(reader, specification, name);
        var id = fileName;

        if (used.TryGetValue(fileName, out var count))
        {
            used[fileName] = count + 1;
            id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{fileName}#{count + 1}");

            warnings.Add(new(
                PdfWarningKind.EmbeddedFileNameCollision,
                $"'{reader.SourceName}': two embedded files are named '{fileName}'; the second is identified as '{id}'."));
        }
        else
        {
            used[fileName] = 1;
        }

        var stream = FindEmbeddedStream(reader, specification);
        var payload = stream is null
            ? (PdfPayload?)null
            : PdfImageFilters.Decode(stream, options.MaxInlineContentBytes, reader.SourceName);

        if (stream is null)
        {
            warnings.Add(new(
                PdfWarningKind.EmbeddedFileUnreadable,
                $"'{reader.SourceName}': the embedded file '{fileName}' states no embedded stream."));
        }
        else if (payload is { Failed: true })
        {
            warnings.Add(new(
                PdfWarningKind.EmbeddedFileUnreadable,
                $"'{reader.SourceName}': the payload of the embedded file '{fileName}' could not be decoded, so its raw bytes are returned."));
        }
        else if (payload is { IsDecodable: false })
        {
            warnings.Add(new(
                PdfWarningKind.EmbeddedFileUnreadable,
                $"'{reader.SourceName}': the embedded file '{fileName}' uses /{payload.Value.Filter}, which the library does not decode; its raw bytes are returned."));
        }

        var parameters = stream is null ? null : reader.ResolveDictionary(stream.Dictionary.Get("Params"));

        return new PdfEmbeddedFileInfo
        {
            Id = id,
            Name = fileName,
            MimeType = stream is null ? null : ReadName(reader, stream.Dictionary.Get("Subtype")),
            Relationship = ReadName(reader, specification?.Get("AFRelationship")),
            ModificationDate = parameters is null ? null : ReadDate(reader, parameters.Get("ModDate")),
            Filter = stream is null ? null : PdfImageFilters.DescribeFilter(stream.Dictionary),
            IsDecodable = payload?.IsDecodable ?? true,
            ByteLength = payload?.Length ?? 0,
            IsBuffered = payload?.Data is not null,
            Data = payload?.Data,
        };
    }

}
