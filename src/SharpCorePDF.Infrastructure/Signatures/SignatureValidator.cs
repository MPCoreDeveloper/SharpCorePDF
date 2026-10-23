using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Signatures;

/// <summary>One signature field of a document and the dictionary its value lives in.</summary>
/// <param name="FieldName">The fully qualified name of the field.</param>
/// <param name="Dictionary">The signature dictionary, with its entries already dereferenced.</param>
internal sealed record SignatureField(string FieldName, PdfDictionary Dictionary);

/// <summary>Reads the signatures of a document and checks what they claim.</summary>
/// <remarks>
/// <para>
/// Two questions are answered separately, because they fail differently. Integrity asks whether the bytes a
/// signature covers still hash to what the signature states, which is arithmetic over the file and needs no
/// trust in anybody. Trust asks whether the certificate that made the signature leads to an anchor the caller
/// named, which the caller supplies through <see cref="PdfTrustOptions"/> and which is never decided here on
/// the caller's behalf.
/// </para>
/// <para>
/// A signature that covers an earlier revision is reported as intact but not as covering the whole document.
/// That is the ordinary state of the first of two signatures: the second one appends a revision and covers
/// both, so <see cref="PdfSignatureValidation.IsValid"/> stays true while
/// <see cref="PdfSignatureInfo.CoversWholeDocument"/> turns false.
/// </para>
/// </remarks>
internal static class SignatureValidator
{
    /// <summary>The deepest field tree this release walks before it stops trusting the document's nesting.</summary>
    private const int MaxFieldDepth = 32;

    /// <summary>Finds every signature of a document, in the order the document lists them.</summary>
    /// <param name="reader">The opened document.</param>
    /// <returns>The signature fields, each with its dictionary.</returns>
    public static IReadOnlyList<SignatureField> Collect(PdfFileReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var fields = new List<SignatureField>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        if (reader.Catalog is { } catalog && reader.ResolveDictionary(catalog.Get("AcroForm")) is { } form)
        {
            Walk(reader, reader.ResolveArray(form.Get("Fields")), string.Empty, fields, seen, 0);
        }

        CollectFromPages(reader, fields, seen);

        return fields;
    }

    /// <summary>Walks the interactive form, descending into the kids of non-terminal fields.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="array">The array of fields to walk.</param>
    /// <param name="prefix">The qualified name the parent contributes.</param>
    /// <param name="fields">The list the signature fields are added to.</param>
    /// <param name="seen">The signature dictionaries already added.</param>
    /// <param name="depth">How deep the walk already is.</param>
    private static void Walk(
        PdfFileReader reader,
        PdfArray? array,
        string prefix,
        List<SignatureField> fields,
        HashSet<PdfDictionary> seen,
        int depth)
    {
        if (array is null || depth > MaxFieldDepth)
        {
            return;
        }

        foreach (var item in array.Items)
        {
            if (reader.ResolveDictionary(item) is not { } field)
            {
                continue;
            }

            var name = field.GetText("T");
            string qualified;

            if (string.IsNullOrEmpty(name))
            {
                qualified = prefix;
            }
            else
            {
                qualified = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
            }

            var kids = reader.ResolveArray(field.Get("Kids"));

            if (field.GetName("FT") is null && kids is not null)
            {
                Walk(reader, kids, qualified, fields, seen, depth + 1);

                if (field.Get("V") is null)
                {
                    continue;
                }
            }

            if (!string.Equals(field.GetName("FT"), "Sig", StringComparison.Ordinal))
            {
                continue;
            }

            if (reader.ResolveDictionary(field.Get("V")) is { } signature && seen.Add(signature))
            {
                fields.Add(new SignatureField(qualified, signature));
            }
        }
    }

    /// <summary>Finds signature widgets that no form listing reaches.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="fields">The list the signature fields are added to.</param>
    /// <param name="seen">The signature dictionaries already added.</param>
    /// <remarks>
    /// A document whose catalog has no <c>/AcroForm</c>, or whose form does not list a field that a page still
    /// carries, does exist — usually assembled by a tool that appended a widget without touching the form. The
    /// page annotations are the reliable place to look, so they are walked as well and duplicates are dropped
    /// by the identity of the signature dictionary.
    /// </remarks>
    private static void CollectFromPages(PdfFileReader reader, List<SignatureField> fields, HashSet<PdfDictionary> seen)
    {
        foreach (var page in reader.Pages)
        {
            if (reader.ResolveArray(page.Dictionary.Get("Annots")) is not { } annotations)
            {
                continue;
            }

            foreach (var item in annotations.Items)
            {
                if (reader.ResolveDictionary(item) is not { } widget)
                {
                    continue;
                }

                if (!string.Equals(widget.GetName("FT"), "Sig", StringComparison.Ordinal))
                {
                    continue;
                }

                if (reader.ResolveDictionary(widget.Get("V")) is { } signature && seen.Add(signature))
                {
                    fields.Add(new SignatureField(widget.GetText("T") ?? string.Empty, signature));
                }
            }
        }
    }

    /// <summary>Reads and checks every signature of a document, in the order they were applied.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="trust">What counts as trusted, or <see langword="null"/> to leave trust unevaluated.</param>
    /// <returns>One validation per signature, oldest first.</returns>
    public static IReadOnlyList<PdfSignatureValidation> ValidateAll(PdfFileReader reader, PdfTrustOptions? trust)
    {
        var fields = Collect(reader);
        var validations = new List<PdfSignatureValidation>(fields.Count);

        foreach (var field in fields)
        {
            validations.Add(Validate(reader, field, trust));
        }

        // A later signature covers everything an earlier one covered plus the revision it added, so ordering by
        // the end of the covered range puts the signatures in the order they were applied.
        return
        [
            .. validations.OrderBy(validation => validation.Signer.ByteRange.Offset2 + validation.Signer.ByteRange.Length2),
        ];
    }

    /// <summary>Reads and checks one signature.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="field">The signature field.</param>
    /// <param name="trust">What counts as trusted, or <see langword="null"/> to leave trust unevaluated.</param>
    /// <returns>The report, the integrity and the trust of the signature.</returns>
    public static PdfSignatureValidation Validate(PdfFileReader reader, SignatureField field, PdfTrustOptions? trust)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(field);

        var reading = Read(reader, field.Dictionary);
        var problems = new List<PdfSignatureProblem>();
        var integrity = Check(reader, field.FieldName, reading, problems);

        if (reading.Cms is { Certificate: null })
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.CertificateMissing,
                $"The signature '{field.FieldName}' embeds no certificate, so there is nothing to check it against."));
        }

        CheckPeriod(field.FieldName, reading.Cms?.Certificate, problems);

        if (reading.Cms is { HasTimestamp: true, TimestampReadable: false })
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.TimestampInvalid,
                $"The timestamp of '{field.FieldName}' is present but could not be read, so the moment it claims cannot be relied on."));
        }

        if (reading.Usable && !reading.CoversWhole)
        {
            var revisions = CountRevisions(reader.Data, reader.Data.LongLength);

            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.RevisionNotCovered,
                $"The signature '{field.FieldName}' covers revision {reading.Revision} of {revisions}: the file changed after it was signed."));
        }

        var value = trust is null
            ? PdfTrustResult.NotEvaluated
            : TrustEvaluator.Evaluate(field.FieldName, reading.Cms, trust, problems);

        return new PdfSignatureValidation(ReadSigner(field, reading), integrity, value, problems);
    }

    /// <summary>What one signature dictionary states, once its entries have been read.</summary>
    /// <param name="Range">The covered ranges.</param>
    /// <param name="HasRange">Whether the dictionary carries four usable numbers.</param>
    /// <param name="Usable">Whether the ranges are ordered and inside the document.</param>
    /// <param name="Contents">The DER structure of the signature value, without its padding.</param>
    /// <param name="Cms">The parsed signature, when it parsed.</param>
    /// <param name="Failure">Why the signature value could not be parsed, when it could not.</param>
    /// <param name="CoversWhole">Whether the ranges cover the file as it is now.</param>
    /// <param name="Revision">The one-based number of the revision the ranges cover.</param>
    private sealed record Reading(
        PdfByteRange Range,
        bool HasRange,
        bool Usable,
        byte[]? Contents,
        CmsSignature? Cms,
        Exception? Failure,
        bool CoversWhole,
        int Revision);

    /// <summary>Reads the entries of one signature dictionary.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="dictionary">The signature dictionary.</param>
    /// <returns>What the dictionary states.</returns>
    /// <remarks>
    /// The signature value is parsed even when the byte range is unusable: a caller that is told which bytes a
    /// broken signature claims to cover, and by whom, can diagnose the file; a caller that is told only that it
    /// is broken cannot.
    /// </remarks>
    private static Reading Read(PdfFileReader reader, PdfDictionary dictionary)
    {
        var numbers = ReadByteRange(reader, dictionary);
        var hasRange = numbers is { Length: 4 };
        var range = hasRange ? new PdfByteRange(numbers![0], numbers[1], numbers[2], numbers[3]) : default;

        // A range that starts at zero and covers nothing is what a placeholder signature holds before it is
        // signed; it describes no bytes, so it is malformed rather than merely empty.
        var usable = hasRange
            && range.IsWellFormed
            && range.Length1 > 0
            && range.Offset2 + range.Length2 <= reader.Data.LongLength;
        var contents = ReadContents(reader, dictionary);
        CmsSignature? cms = null;
        Exception? failure = null;

        if (contents is { Length: > 0 })
        {
            try
            {
                cms = CmsReader.Read(contents);
            }
            catch (Exception exception) when (exception is InvalidDataException or CryptographicException or ArgumentException or NotSupportedException)
            {
                failure = exception;
            }
        }

        return new Reading(
            range,
            hasRange,
            usable,
            contents,
            cms,
            failure,
            usable && range.Covers(reader.Data.LongLength),
            usable ? CountRevisions(reader.Data, range.Offset2 + range.Length2) : 0);
    }

    /// <summary>Reads the four numbers of the byte range.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="dictionary">The signature dictionary.</param>
    /// <returns>The four numbers, or <see langword="null"/> when the entry is absent or not four integers.</returns>
    private static long[]? ReadByteRange(PdfFileReader reader, PdfDictionary dictionary)
    {
        if (reader.ResolveArray(dictionary.Get("ByteRange")) is not { } array)
        {
            return null;
        }

        var numbers = new List<long>(array.Count);

        foreach (var item in array.Items)
        {
            if (reader.Resolve(item) is PdfInteger integer)
            {
                numbers.Add(integer.Value);
            }
            else
            {
                return null;
            }
        }

        return numbers.Count == 4 ? [.. numbers] : null;
    }

    /// <summary>Reads the signature value.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="dictionary">The signature dictionary.</param>
    /// <returns>The DER structure of the value, without the padding, or <see langword="null"/> when the entry is not a string.</returns>
    /// <remarks>
    /// The value is written into a gap that was reserved before its length was known, so a reader has to cut it
    /// down to the structure it holds: a CMS ends where its own length says it does, and everything after it is
    /// padding. The cut follows the length in the DER header rather than trailing zeroes, because the last byte
    /// of a signature may legitimately be one.
    /// </remarks>
    private static byte[]? ReadContents(PdfFileReader reader, PdfDictionary dictionary)
    {
        if (reader.Resolve(dictionary.Get("Contents")) is not PdfString value)
        {
            return null;
        }

        return DerValue.Read(value.Bytes, out var consumed) is not null && consumed > 0 && consumed <= value.Bytes.Length
            ? value.Bytes[..consumed]
            : value.Bytes;
    }

    /// <summary>Hashes the covered bytes.</summary>
    /// <param name="data">The document.</param>
    /// <param name="range">The covered ranges.</param>
    /// <param name="algorithm">The digest to hash with.</param>
    /// <returns>The digest of the two ranges, in order.</returns>
    private static byte[] ComputeDigest(byte[] data, PdfByteRange range, PdfHashAlgorithm algorithm)
    {
        using var hash = IncrementalHash.CreateHash(SignatureAlgorithms.Name(algorithm));

        hash.AppendData(data, checked((int)range.Offset1), checked((int)range.Length1));
        hash.AppendData(data, checked((int)range.Offset2), checked((int)range.Length2));

        return hash.GetHashAndReset();
    }

    /// <summary>Counts the revisions of a document up to a point in it.</summary>
    /// <param name="data">The document.</param>
    /// <param name="end">The offset the count stops at.</param>
    /// <returns>The number of <c>%%EOF</c> markers before <paramref name="end"/>, and at least one.</returns>
    private static int CountRevisions(byte[] data, long end)
    {
        var limit = (int)Math.Clamp(end, 0L, data.LongLength);

        if (limit == 0)
        {
            return 0;
        }

        ReadOnlySpan<byte> span = data.AsSpan(0, limit);
        ReadOnlySpan<byte> marker = "%%EOF"u8;
        var count = 0;
        var position = 0;

        while (position < span.Length)
        {
            var index = span[position..].IndexOf(marker);

            if (index < 0)
            {
                break;
            }

            var start = position + index;

            if (start == 0 || span[start - 1] is (byte)'\n' or (byte)'\r')
            {
                count++;
            }

            position = start + marker.Length;
        }

        return Math.Max(count, 1);
    }

    /// <summary>Checks the covered bytes against the signature value.</summary>
    /// <param name="reader">The opened document.</param>
    /// <param name="fieldName">The name of the field, for the report.</param>
    /// <param name="reading">What the signature dictionary states.</param>
    /// <param name="problems">The list problems are added to.</param>
    /// <returns>The integrity of the covered bytes.</returns>
    private static PdfSignatureIntegrity Check(
        PdfFileReader reader,
        string fieldName,
        Reading reading,
        List<PdfSignatureProblem> problems)
    {
        if (!reading.Usable)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.MalformedByteRange,
                reading.HasRange
                    ? $"The signature '{fieldName}' covers {reading.Range}, which is not a pair of ordered ranges inside a document of {reader.Data.LongLength} bytes."
                    : $"The signature '{fieldName}' has no usable /ByteRange entry, so there is no way to tell which bytes it signs."));

            return PdfSignatureIntegrity.Unknown;
        }

        if (reading.Contents is not { Length: > 0 })
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.StructureUnreadable,
                $"The signature '{fieldName}' has no /Contents value, so it carries no signature at all."));

            return PdfSignatureIntegrity.Unknown;
        }

        if (reading.Cms is not { } cms)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.StructureUnreadable,
                $"The signature value of '{fieldName}' is not a CMS structure this engine can read: {reading.Failure?.Message ?? "it holds only padding."}"));

            return PdfSignatureIntegrity.Unknown;
        }

        if (cms.DigestAlgorithm is not { } algorithm)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.UnsupportedAlgorithm,
                $"The signature '{fieldName}' signs with the digest '{cms.DigestAlgorithmOid}', which this release does not implement."));

            return PdfSignatureIntegrity.Unknown;
        }

        var digest = ComputeDigest(reader.Data, reading.Range, algorithm);

        if (!CryptographicOperations.FixedTimeEquals(digest, cms.MessageDigest))
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.DigestMismatch,
                $"The bytes {reading.Range} of '{reader.SourceName}' hash to {Convert.ToHexString(digest)}, but the signature '{fieldName}' states {Convert.ToHexString(cms.MessageDigest)}."));

            return PdfSignatureIntegrity.Invalid;
        }

        return CheckSignature(fieldName, cms, algorithm, problems);
    }

    /// <summary>Checks the signature of the signed attributes against the certificate's public key.</summary>
    /// <param name="fieldName">The name of the field, for the report.</param>
    /// <param name="cms">The parsed signature.</param>
    /// <param name="algorithm">The digest the signature was computed with.</param>
    /// <param name="problems">The list problems are added to.</param>
    /// <returns>The integrity of the signature value.</returns>
    private static PdfSignatureIntegrity CheckSignature(
        string fieldName,
        CmsSignature cms,
        PdfHashAlgorithm algorithm,
        List<PdfSignatureProblem> problems)
    {
        if (cms.Certificate is not { } certificate)
        {
            return PdfSignatureIntegrity.Unknown;
        }

        if (SignatureAlgorithms.FromSignatureOid(cms.SignatureAlgorithm) is null)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.UnsupportedAlgorithm,
                $"The signature '{fieldName}' names the signature algorithm '{cms.SignatureAlgorithm}', which this release does not verify."));

            return PdfSignatureIntegrity.Unknown;
        }

        var name = SignatureAlgorithms.Name(algorithm);
        bool verified;

        if (SignatureAlgorithms.IsEllipticCurve(cms.SignatureAlgorithm))
        {
            using var key = certificate.GetECDsaPublicKey();

            if (key is null)
            {
                problems.Add(new PdfSignatureProblem(
                    PdfSignatureProblemKind.UnsupportedAlgorithm,
                    $"The signature '{fieldName}' is an elliptic-curve signature, but '{certificate.Subject}' carries no elliptic-curve key."));

                return PdfSignatureIntegrity.Unknown;
            }

            verified = key.VerifyData(cms.SignedAttributes, cms.Signature, name);
        }
        else
        {
            using var key = certificate.GetRSAPublicKey();

            if (key is null)
            {
                problems.Add(new PdfSignatureProblem(
                    PdfSignatureProblemKind.UnsupportedAlgorithm,
                    $"The signature '{fieldName}' is an RSA signature, but '{certificate.Subject}' carries no RSA key."));

                return PdfSignatureIntegrity.Unknown;
            }

            // The scheme is the one the document names: PDF signatures are RSA PKCS#1 v1.5 unless they say PSS.
#pragma warning disable S5542 // RSA PKCS#1 v1.5 is what these signatures are; padding is not a choice here.
            verified = key.VerifyData(cms.SignedAttributes, cms.Signature, name, RSASignaturePadding.Pkcs1);
#pragma warning restore S5542
        }

        if (verified)
        {
            return PdfSignatureIntegrity.Valid;
        }

        problems.Add(new PdfSignatureProblem(
            PdfSignatureProblemKind.SignatureMismatch,
            $"The signed attributes of '{fieldName}' do not verify against the public key of '{certificate.Subject}'."));

        return PdfSignatureIntegrity.Invalid;
    }

    /// <summary>Reports a certificate that is outside its validity period.</summary>
    /// <param name="fieldName">The name of the field, for the report.</param>
    /// <param name="certificate">The signer certificate, when the signature embeds one.</param>
    /// <param name="problems">The list problems are added to.</param>
    /// <remarks>
    /// The period is checked against the current moment rather than against the stated signing time, because a
    /// signing time is written by whoever signed and can say anything. A timestamp token would be the honest
    /// answer, and this release reports whether one is present rather than trusting it.
    /// </remarks>
    private static void CheckPeriod(string fieldName, X509Certificate2? certificate, List<PdfSignatureProblem> problems)
    {
        if (certificate is null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (certificate.NotAfter.ToUniversalTime() < now)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.CertificateExpired,
                $"The certificate of '{fieldName}' expired on {certificate.NotAfter:yyyy-MM-dd} and the signature carries no timestamp that would place it earlier."));
        }
        else if (certificate.NotBefore.ToUniversalTime() > now)
        {
            problems.Add(new PdfSignatureProblem(
                PdfSignatureProblemKind.CertificateNotYetValid,
                $"The certificate of '{fieldName}' is not valid before {certificate.NotBefore:yyyy-MM-dd}."));
        }
    }

    /// <summary>Builds the report of what a signature states.</summary>
    /// <param name="field">The signature field.</param>
    /// <param name="reading">What its dictionary states.</param>
    /// <returns>The report.</returns>
    private static PdfSignatureInfo ReadSigner(SignatureField field, Reading reading)
    {
        var dictionary = field.Dictionary;
        var certificate = reading.Cms?.Certificate;

        return new PdfSignatureInfo
        {
            FieldName = field.FieldName,
            Filter = dictionary.GetName("Filter"),
            SubFilter = dictionary.GetName("SubFilter"),
            SignedAt = PdfDate.TryParse(dictionary.GetText("M"), out var stated) ? stated : reading.Cms?.SigningTime,
            Reason = dictionary.GetText("Reason"),
            Location = dictionary.GetText("Location"),
            ContactInfo = dictionary.GetText("ContactInfo"),
            HashAlgorithm = reading.Cms?.DigestAlgorithm,
            SignerSubject = certificate?.Subject,
            SignerName = certificate is null ? null : CertificateNames.Simple(certificate),
            Issuer = certificate?.Issuer,
            SerialNumber = certificate?.SerialNumber,
            NotBefore = certificate?.NotBefore,
            NotAfter = certificate?.NotAfter,
            ByteRange = reading.Range,
            CoversWholeDocument = reading.CoversWhole,
            CoveredRevision = reading.Revision,
            SignatureLength = reading.Contents?.Length ?? 0,
            HasTimestamp = reading.Cms?.HasTimestamp ?? false,
        };
    }
}
