using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>Writes the revision that turns a document into a signed one.</summary>
/// <remarks>
/// <para>
/// The revision is appended, never merged: nothing the source document contains moves, so every byte range an
/// earlier signature covered still covers the same bytes afterwards. The revision holds the signature
/// dictionary, the widget that shows it on a page, an optional appearance stream, the page with that widget in
/// its <c>/Annots</c>, and the catalog with the field listed through <c>/AcroForm</c>. Its cross-reference
/// section points back at the previous revision through <c>/Prev</c>.
/// </para>
/// <para>
/// The signature covers the whole file as it stands after the revision is written, minus the <c>/Contents</c>
/// value. Both numbers live at offsets this writer knows, so the digest is computed over the finished revision
/// with the <c>/Contents</c> gap left out, and only then is the CMS written into the gap the object reserved.
/// The reservation is generous enough for the certificate chain plus a timestamp token; if it ever is not, the
/// revision is rebuilt with a larger gap, because a signature whose value does not fit cannot be repaired
/// afterwards.
/// </para>
/// <para>
/// A document that is encrypted is refused. Its appended objects would have to be encrypted with the
/// document's key, while <c>/Contents</c> has to stay in the clear, and this release does not implement that
/// combination — writing the signature into an encrypted revision would produce a file no reader accepts.
/// </para>
/// </remarks>
internal static class SignatureFieldWriter
{
    /// <summary>The width of one number of the byte range, digits and separator included.</summary>
    private const int NumberWidth = 11;

    /// <summary>The bytes reserved for the four numbers of the byte range, until their values are known.</summary>
    private static ReadOnlySpan<byte> ByteRangePlaceholder => "0000000000 0000000000 0000000000 0000000000"u8;

    /// <summary>Writes a signed revision of a document into a new buffer.</summary>
    /// <param name="reader">The opened source document.</param>
    /// <param name="options">The signature to produce.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bytes of the source document followed by the signed revision.</returns>
    /// <exception cref="PdfUnsupportedFeatureException">The document is encrypted, or a digest is not implemented.</exception>
    /// <exception cref="PdfSourceFormatException">The document has no catalog or no page to carry the widget.</exception>
    public static async Task<byte[]> WriteAsync(
        PdfFileReader reader,
        PdfSignatureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);

        if (reader.IsEncrypted)
        {
            throw new PdfUnsupportedFeatureException(
                reader.SourceName,
                "signing an encrypted document",
                "the revision would have to be encrypted with the document key while /Contents stays in the clear; decrypt the document first and sign the result.");
        }

        if (reader.Catalog is not { } catalog)
        {
            throw new PdfSourceFormatException(reader.SourceName, "the document has no catalog, so a signature cannot be added to it.");
        }

        if (reader.Pages.Count == 0)
        {
            throw new PdfSourceFormatException(reader.SourceName, "the document has no page tree, so a signature widget cannot be placed on a page.");
        }

        var signingTime = options.SigningTime ?? DateTimeOffset.UtcNow;
        var page = SelectPage(reader, options.Appearance);
        var certificates = CollectCertificates(options);
        var capacity = ReserveCapacity(certificates);

        try
        {
            var revision = await BuildAsync(reader, options, catalog, page, certificates, signingTime, capacity, cancellationToken).ConfigureAwait(false);

            if (revision.SignatureLength <= capacity)
            {
                return revision.Document;
            }

            // The certificate chain and the timestamp token came out larger than the reservation. The gap is part
            // of the revision, so a larger one means rebuilding it — and signing it again.
            capacity = revision.SignatureLength + 256;
            revision = await BuildAsync(reader, options, catalog, page, certificates, signingTime, capacity, cancellationToken).ConfigureAwait(false);

            return revision.SignatureLength <= capacity
                ? revision.Document
                : throw new PdfUnsupportedFeatureException(
                    reader.SourceName,
                    "signing with this certificate",
                    $"the signature value needs {revision.SignatureLength} bytes, more than the {capacity} bytes this engine can reserve for it.");
        }
        catch (NotSupportedException exception)
        {
            throw new PdfUnsupportedFeatureException(
                reader.SourceName,
                $"the digest '{options.HashAlgorithm}'",
                $"this release signs with SHA-256, SHA-384 or SHA-512 only: {exception.Message}");
        }
    }

    /// <summary>Builds one revision and computes the signature value that belongs in it.</summary>
    /// <param name="reader">The opened source document.</param>
    /// <param name="options">The signature to produce.</param>
    /// <param name="catalog">The catalog of the source document.</param>
    /// <param name="page">The page that carries the widget.</param>
    /// <param name="certificates">The signer certificate followed by the rest of its chain.</param>
    /// <param name="signingTime">The time the signature claims to have been made.</param>
    /// <param name="capacity">The number of bytes to reserve for the signature value.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The finished document and the number of bytes the signature value occupies.</returns>
    private static async Task<(byte[] Document, int SignatureLength)> BuildAsync(
        PdfFileReader reader,
        PdfSignatureOptions options,
        PdfDictionary catalog,
        SourcePage page,
        IReadOnlyList<X509Certificate2> certificates,
        DateTimeOffset signingTime,
        int capacity,
        CancellationToken cancellationToken)
    {
        var bytes = reader.Data;
        var next = NextObjectNumber(reader);
        var signatureNumber = next++;
        var widgetNumber = next++;
        var appearanceNumber = options.Appearance is null ? 0 : next;
        var catalogNumber = reader.RootObjectNumber > 0 ? checked((int)reader.RootObjectNumber) : next;
        var pageNumber = checked((int)page.ObjectNumber);
        var hexLength = checked(capacity * 2);
        var signatureReference = new PdfReference(signatureNumber, 0);
        var widgetReference = new PdfReference(widgetNumber, 0);

        using var buffer = new MemoryStream(bytes.Length + hexLength + 8192);
        await buffer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

        var writer = new PdfIncrementalWriter(buffer, bytes.LongLength);

        if (options.Appearance is { } appearance)
        {
            writer.WriteObject(appearanceNumber, 0, AppearanceStream(appearance, options, signingTime));
        }

        writer.BeginObject(signatureNumber, 0);
        WriteSignatureDictionary(writer, options, certificates, signingTime, capacity, out var byteRangeOffset, out var contentsStart);
        writer.EndObject();

        writer.WriteObject(widgetNumber, 0, Widget(options, pageNumber, signatureReference, appearanceNumber));
        writer.WriteObject(pageNumber, 0, PageWithWidget(reader, page, widgetReference));
        writer.WriteObject(catalogNumber, 0, new PdfDictionary(catalog.Entries).Set("AcroForm", AcroForm(reader, catalog, widgetReference)));
        writer.Complete(Trailer(reader, catalogNumber));

        var digest = SealRevision(buffer, byteRangeOffset, contentsStart, hexLength, options.HashAlgorithm);
        var value = await CmsBuilder.BuildAsync(digest, options, certificates, signingTime, cancellationToken).ConfigureAwait(false);

        var document = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));

        WriteSignatureValue(document, contentsStart + 1, value);

        return (document.ToArray(), value.Length);
    }

    /// <summary>Fills in the byte range and hashes the revision the signature will cover.</summary>
    /// <param name="buffer">The revision, as written so far.</param>
    /// <param name="byteRangeOffset">The offset of the first number of the byte range.</param>
    /// <param name="contentsStart">The offset of the <c>&lt;</c> that opens the contents gap.</param>
    /// <param name="hexLength">The number of hexadecimal digits reserved for the signature value.</param>
    /// <param name="algorithm">The digest to hash with.</param>
    /// <returns>The digest the signature is computed over.</returns>
    private static byte[] SealRevision(MemoryStream buffer, long byteRangeOffset, int contentsStart, int hexLength, PdfHashAlgorithm algorithm)
    {
        var document = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
        var contentsEnd = contentsStart + hexLength + 2;

        PatchByteRange(document, byteRangeOffset, contentsStart, contentsEnd);

        return ComputeDigest(document, contentsStart, contentsEnd, algorithm);
    }

    /// <summary>Picks the page the widget is attached to.</summary>
    /// <param name="reader">The opened source document.</param>
    /// <param name="appearance">The appearance that names the page, or <see langword="null"/> for the first page.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The document has no such page.</exception>
    private static SourcePage SelectPage(PdfFileReader reader, PdfSignatureAppearance? appearance)
    {
        var number = appearance?.PageNumber ?? 1;

        if (number > reader.Pages.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appearance),
                number,
                $"The document has {reader.Pages.Count} page(s), so page {number} cannot carry the signature widget.");
        }

        return reader.Pages[number - 1];
    }

    /// <summary>Collects the signer certificate and the rest of its chain, without duplicates.</summary>
    /// <param name="options">The signature to produce.</param>
    /// <returns>The certificates to embed, signer first.</returns>
    private static List<X509Certificate2> CollectCertificates(PdfSignatureOptions options)
    {
        var certificates = new List<X509Certificate2> { options.Certificate };
        var chain = options.Chain ?? Array.Empty<X509Certificate2>();

        foreach (var certificate in chain)
        {
            if (certificate is null || certificates.Exists(existing => existing.Thumbprint == certificate.Thumbprint))
            {
                continue;
            }

            certificates.Add(certificate);
        }

        return certificates;
    }

    /// <summary>Sizes the gap the signature value is written into.</summary>
    /// <param name="certificates">The certificates that will be embedded.</param>
    /// <returns>The number of bytes to reserve.</returns>
    /// <remarks>
    /// Eight kilobytes hold a signature, the signed attributes and a timestamp token for an ordinary chain. A
    /// chain of cross-signed roots, or a token with a long certificate list, can exceed that; the caller retries
    /// with the size the first attempt reported.
    /// </remarks>
    private static int ReserveCapacity(IReadOnlyList<X509Certificate2> certificates)
    {
        var capacity = 8192;

        foreach (var certificate in certificates)
        {
            capacity += certificate.RawData.Length;
        }

        return capacity;
    }

    /// <summary>Finds the first object number that is free in the source document.</summary>
    /// <param name="reader">The opened source document.</param>
    /// <returns>The object number to start from.</returns>
    private static int NextObjectNumber(PdfFileReader reader)
    {
        var table = reader.CrossReferenceTable;
        var declared = table.Trailer.GetInteger("Size") ?? 0L;
        var next = Math.Max(table.HighestObjectNumber + 1, declared);

        return (int)Math.Max(next, 1L);
    }

    /// <summary>Writes the signature dictionary, leaving the byte range and the contents gap to be filled in.</summary>
    /// <param name="writer">The writer of the revision.</param>
    /// <param name="options">The signature to produce.</param>
    /// <param name="certificates">The certificates that will be embedded.</param>
    /// <param name="signingTime">The time the signature claims to have been made.</param>
    /// <param name="capacity">The number of bytes to reserve for the signature value.</param>
    /// <param name="byteRangeOffset">The offset of the first number of the byte range.</param>
    /// <param name="contentsStart">The offset of the <c>&lt;</c> that opens the contents gap.</param>
    private static void WriteSignatureDictionary(
        PdfIncrementalWriter writer,
        PdfSignatureOptions options,
        IReadOnlyList<X509Certificate2> certificates,
        DateTimeOffset signingTime,
        int capacity,
        out long byteRangeOffset,
        out int contentsStart)
    {
        writer.Write("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter "u8);
        writer.Write(options.UseCades ? "/ETSI.CAdES.detached"u8 : "/adbe.pkcs7.detached"u8);
        writer.Write(" /M "u8);
        writer.WriteValue(PdfString.FromText(PdfDate.Format(signingTime)));

        WriteTextEntry(writer, "Name", SignerName(certificates[0]));
        WriteTextEntry(writer, "Reason", options.Reason);
        WriteTextEntry(writer, "Location", options.Location);
        WriteTextEntry(writer, "ContactInfo", options.ContactInfo);

        writer.Write(" /ByteRange ["u8);
        byteRangeOffset = writer.Length;
        writer.Write(ByteRangePlaceholder);

        writer.Write("] /Contents "u8);
        contentsStart = checked((int)writer.Length);
        writer.WriteByte((byte)'<');
        writer.Write(Fill(checked(capacity * 2), (byte)'0'));
        writer.WriteByte((byte)'>');
        writer.Write(" >>"u8);
    }

    /// <summary>Writes one optional text entry of the signature dictionary.</summary>
    /// <param name="writer">The writer of the revision.</param>
    /// <param name="key">The name of the entry, without its slash.</param>
    /// <param name="value">The text, which is skipped when it is empty.</param>
    private static void WriteTextEntry(PdfIncrementalWriter writer, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        writer.WriteByte((byte)' ');
        writer.WriteByte((byte)'/');
        writer.Write(Encoding.ASCII.GetBytes(key));
        writer.WriteByte((byte)' ');
        writer.WriteValue(PdfString.FromText(value));
    }

    /// <summary>Builds the widget annotation that holds the signature field.</summary>
    /// <param name="options">The signature to produce.</param>
    /// <param name="pageNumber">The object number of the page the widget is attached to.</param>
    /// <param name="signature">The reference to the signature dictionary.</param>
    /// <param name="appearanceNumber">The object number of the appearance stream, or zero when there is none.</param>
    /// <returns>The merged field and widget dictionary.</returns>
    private static PdfDictionary Widget(PdfSignatureOptions options, int pageNumber, PdfReference signature, int appearanceNumber)
    {
        var rectangle = options.Appearance?.Rectangle ?? new PdfRectangle(0d, 0d, 0d, 0d);

        var widget = new PdfDictionary()
            .SetName("Type", "Annot")
            .SetName("Subtype", "Widget")
            .SetName("FT", "Sig")

            // Bit 3 (4): the widget is printed with the page. Bit 8 (128): it is locked, so a viewer does not
            // let the reader delete or move the field that holds a signature.
            .SetInteger("F", 132)
            .Set("T", PdfString.FromText(options.FieldName))
            .Set("Rect", new PdfArray(
            [
                new PdfReal(rectangle.Left),
                new PdfReal(rectangle.Bottom),
                new PdfReal(rectangle.Right),
                new PdfReal(rectangle.Top),
            ]))
            .Set("P", new PdfReference(pageNumber, 0))
            .Set("V", signature);

        if (appearanceNumber > 0)
        {
            widget.Set("AP", new PdfDictionary().Set("N", new PdfReference(appearanceNumber, 0)));
        }

        return widget;
    }

    /// <summary>Writes the page with the widget appended to its annotations.</summary>
    /// <param name="reader">The opened source document.</param>
    /// <param name="page">The page that carries the widget.</param>
    /// <param name="widget">The reference to the widget.</param>
    /// <returns>The page dictionary of the new revision.</returns>
    private static PdfDictionary PageWithWidget(PdfFileReader reader, SourcePage page, PdfReference widget)
    {
        var annotations = new PdfArray();

        if (reader.ResolveArray(page.Dictionary.Get("Annots")) is { } existing)
        {
            annotations.Items.AddRange(existing.Items);
        }

        annotations.Add(widget);

        return new PdfDictionary(page.Dictionary.Entries).Set("Annots", annotations);
    }

    /// <summary>Builds the interactive form dictionary that lists the new signature field.</summary>
    /// <param name="reader">The opened source document.</param>
    /// <param name="catalog">The catalog of the source document.</param>
    /// <param name="widget">The reference to the new field.</param>
    /// <returns>The form dictionary of the new revision.</returns>
    private static PdfDictionary AcroForm(PdfFileReader reader, PdfDictionary catalog, PdfReference widget)
    {
        var form = new PdfDictionary();
        var existing = reader.ResolveDictionary(catalog.Get("AcroForm"));

        if (existing is not null)
        {
            foreach (var entry in existing.Entries)
            {
                form.Set(entry.Key, entry.Value);
            }
        }

        var fields = new PdfArray();

        if (reader.ResolveArray(form.Get("Fields")) is { } listed)
        {
            fields.Items.AddRange(listed.Items);
        }

        fields.Add(widget);
        form.Set("Fields", fields);

        var flags = existing?.GetInteger("SigFlags") ?? 0L;

        // Bit 1: the document carries a signature field. Bit 2: it may be appended to without invalidating the
        // signatures already in it, which is true of every document this writer produces.
        return form.SetInteger("SigFlags", flags | 0b11);
    }

    /// <summary>Builds the trailer of the new revision.</summary>
    /// <param name="reader">The opened source document.</param>
    /// <param name="catalogNumber">The object number the catalogue was written with.</param>
    /// <returns>The trailer dictionary.</returns>
    /// <remarks>
    /// The entries of the previous trailer are carried over — <c>/Id</c> and <c>/Info</c> above all — except the
    /// ones that describe the previous revision's cross-reference section, which
    /// <see cref="PdfIncrementalWriter"/> writes again. <c>/Prev</c> points at the section this revision
    /// supersedes.
    /// </remarks>
    private static PdfDictionary Trailer(PdfFileReader reader, int catalogNumber)
    {
        var trailer = new PdfDictionary();

        foreach (var entry in reader.CrossReferenceTable.Trailer.Entries)
        {
            if (entry.Key is "Size" or "Prev" or "XRefStm" or "Type" or "W" or "Index" or "Length" or "Filter" or "DecodeParms")
            {
                continue;
            }

            trailer.Set(entry.Key, entry.Value);
        }

        if (reader.CrossReferenceTable.StartXrefOffset > 0)
        {
            trailer.SetInteger("Prev", reader.CrossReferenceTable.StartXrefOffset);
        }

        trailer.Set("Root", new PdfReference(catalogNumber, 0));

        return trailer;
    }

    /// <summary>Builds the appearance stream that shows who signed and when.</summary>
    /// <param name="appearance">The appearance the caller asked for.</param>
    /// <param name="options">The signature to produce.</param>
    /// <param name="signingTime">The time the signature claims to have been made.</param>
    /// <returns>The form XObject.</returns>
    /// <remarks>
    /// The stream draws a border and up to four lines of text in Helvetica, which every reader has built in, so
    /// the appearance needs no embedded font and adds no bytes worth mentioning to the revision.
    /// </remarks>
    private static PdfStream AppearanceStream(PdfSignatureAppearance appearance, PdfSignatureOptions options, DateTimeOffset signingTime)
    {
        var rectangle = appearance.Rectangle;
        var width = rectangle.Width;
        var height = rectangle.Height;
        var border = appearance.BorderWidth;
        var fontSize = appearance.FontSize;
        var leading = fontSize * 1.25d;

        var content = new StringBuilder();
        content.Append("q\n");

        if (border > 0d)
        {
            var inset = border / 2d;

            content.Append(PdfNumberFormat.Format(border)).Append(" w\n0 0 0 RG\n")
                .Append(PdfNumberFormat.Format(inset)).Append(' ')
                .Append(PdfNumberFormat.Format(inset)).Append(' ')
                .Append(PdfNumberFormat.Format(width - border)).Append(' ')
                .Append(PdfNumberFormat.Format(height - border)).Append(" re\nS\n");
        }

        content.Append("BT\n/Helv ").Append(PdfNumberFormat.Format(fontSize)).Append(" Tf\n0 0 0 rg\n")
            .Append(PdfNumberFormat.Format(border + 2d)).Append(' ')
            .Append(PdfNumberFormat.Format(Math.Max(height - border - 2d - fontSize, 0d)))
            .Append(" Td\n");

        var lines = Lines(appearance.Text ?? DefaultText(options.Certificate, signingTime));

        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                content.Append("0 ").Append(PdfNumberFormat.Format(-leading)).Append(" Td\n");
            }

            content.Append('(').Append(Escape(lines[index])).Append(") Tj\n");
        }

        content.Append("ET\nQ\n");

        var font = new PdfDictionary()
            .SetName("Type", "Font")
            .SetName("Subtype", "Type1")
            .SetName("BaseFont", "Helvetica")
            .SetName("Encoding", "WinAnsiEncoding");

        var dictionary = new PdfDictionary()
            .SetName("Type", "XObject")
            .SetName("Subtype", "Form")
            .SetInteger("FormType", 1)
            .Set("BBox", new PdfArray(
            [
                new PdfReal(0d),
                new PdfReal(0d),
                new PdfReal(width),
                new PdfReal(height),
            ]))
            .Set("Resources", new PdfDictionary().Set("Font", new PdfDictionary().Set("Helv", font)));

        return new PdfStream(dictionary, Encoding.Latin1.GetBytes(content.ToString()));
    }

    /// <summary>Builds the text an appearance shows when the caller did not supply one.</summary>
    /// <param name="certificate">The signer certificate.</param>
    /// <param name="signingTime">The time the signature claims to have been made.</param>
    /// <returns>The text, one line per fact.</returns>
    private static string DefaultText(X509Certificate2 certificate, DateTimeOffset signingTime) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Digitally signed by {SignerName(certificate)}\n{signingTime:yyyy-MM-dd HH:mm:ss zzz}");

    /// <summary>Finds the name a certificate lists for its subject.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The simple name when the certificate has one, its subject otherwise.</returns>
    private static string SignerName(X509Certificate2 certificate) => CertificateNames.Simple(certificate);

    /// <summary>Splits appearance text into the lines the stream draws.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The first four lines, at most, and never an empty list.</returns>
    private static IReadOnlyList<string> Lines(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.Length == 0 ? [string.Empty] : [.. lines.Take(4)];
    }

    /// <summary>Escapes text so it can sit inside a PDF literal string.</summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped text, with characters outside the encoding replaced.</returns>
    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '(':
                    escaped.Append("\\(");
                    break;
                case ')':
                    escaped.Append("\\)");
                    break;
                case '\r':
                case '\n':
                    escaped.Append(' ');
                    break;
                default:
                    escaped.Append(character <= '\u00FF' ? character : '?');
                    break;
            }
        }

        return escaped.ToString();
    }

    /// <summary>Fills a buffer with one repeated byte.</summary>
    /// <param name="count">The length of the buffer.</param>
    /// <param name="value">The byte to repeat.</param>
    /// <returns>The buffer.</returns>
    private static byte[] Fill(int count, byte value)
    {
        var buffer = new byte[count];
        buffer.AsSpan().Fill(value);

        return buffer;
    }

    /// <summary>Replaces the byte range placeholder with the offsets the revision ended up with.</summary>
    /// <param name="document">The finished revision.</param>
    /// <param name="numbersOffset">The offset of the first number of the byte range.</param>
    /// <param name="contentsStart">The offset of the <c>&lt;</c> that opens the contents gap.</param>
    /// <param name="contentsEnd">The offset just past the <c>&gt;</c> that closes the contents gap.</param>
    private static void PatchByteRange(Span<byte> document, long numbersOffset, int contentsStart, int contentsEnd)
    {
        var total = document.Length;

        // The first range runs from the start of the file to the opening delimiter of the value; the second
        // from behind its closing delimiter to the end. What lies between them is not covered, which is what
        // lets the value be written into the file it signs.
        WriteNumber(document, numbersOffset, 0L);
        WriteNumber(document, numbersOffset + NumberWidth, contentsStart);
        WriteNumber(document, numbersOffset + (2 * NumberWidth), contentsEnd);
        WriteNumber(document, numbersOffset + (3 * NumberWidth), total - contentsEnd);
    }

    /// <summary>Writes one right-aligned number of the byte range over its placeholder.</summary>
    /// <param name="document">The finished revision.</param>
    /// <param name="offset">The offset of the number.</param>
    /// <param name="value">The value to write.</param>
    private static void WriteNumber(Span<byte> document, long offset, long value)
    {
        var text = value.ToString("D10", CultureInfo.InvariantCulture);

        for (var index = 0; index < text.Length; index++)
        {
            document[checked((int)offset) + index] = (byte)text[index];
        }
    }

    /// <summary>Hashes the revision without the contents gap.</summary>
    /// <param name="document">The finished revision.</param>
    /// <param name="contentsStart">The offset of the <c>&lt;</c> that opens the contents gap.</param>
    /// <param name="contentsEnd">The offset just past the <c>&gt;</c> that closes the contents gap.</param>
    /// <param name="algorithm">The digest to hash with.</param>
    /// <returns>The digest.</returns>
    private static byte[] ComputeDigest(ReadOnlySpan<byte> document, int contentsStart, int contentsEnd, PdfHashAlgorithm algorithm)
    {
        using var hash = IncrementalHash.CreateHash(SignatureAlgorithms.Name(algorithm));

        hash.AppendData(document[..contentsStart]);
        hash.AppendData(document[contentsEnd..]);

        return hash.GetHashAndReset();
    }

    /// <summary>Writes the signature value into the reserved gap, in hexadecimal.</summary>
    /// <param name="document">The finished revision.</param>
    /// <param name="offset">The offset just past the <c>&lt;</c> that opens the contents gap.</param>
    /// <param name="value">The signature value.</param>
    /// <remarks>
    /// The rest of the gap keeps the zeros it was reserved with. Padding is what the format expects: the value
    /// has to be in place before the digest is computed, so its final length cannot be known when the gap is
    /// written, and a reader stops at the end of the CMS structure anyway.
    /// </remarks>
    private static void WriteSignatureValue(Span<byte> document, int offset, ReadOnlySpan<byte> value)
    {
        ReadOnlySpan<byte> digits = "0123456789ABCDEF"u8;

        for (var index = 0; index < value.Length; index++)
        {
            document[offset + (index * 2)] = digits[value[index] >> 4];
            document[(offset + (index * 2)) + 1] = digits[value[index] & 0x0F];
        }
    }
}
