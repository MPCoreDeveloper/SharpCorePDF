# 23. Digital Signatures

> **Status: unreleased.** This chapter documents the signing and validation surface queued for the next
> release. It is in the manual source so the API and its boundary are reviewed together; it is not part
> of the published 1.0.0 manual PDF.

Signing a document adds a signature to it. It does not rebuild the document. The signer appends one
revision to the bytes it was given, covering everything that was there before it and its own signature
dictionary, and leaves every earlier byte exactly where it was. That is the only shape in which a second
signature survives: the first one still covers the bytes it signed, and a reader can tell that it does.

Two questions are answered about a signature, and they are answered separately:

- **Integrity** — do the bytes the signature covers still hash and verify to what the signer signed?
- **Trust** — does the signer's certificate lead to an anchor the caller named?

The two are independent. A document can be intact and signed by a certificate nobody trusts, and it can
be signed by a trusted certificate over bytes that were changed afterwards. `IsValid` speaks only about
integrity, because that is the half this library can decide on its own.

## Signing

```csharp
using System.Security.Cryptography.X509Certificates;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Sources;

using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
    "signing.pfx", password: null, X509KeyStorageFlags.EphemeralKeySet);

var options = new PdfSignatureOptions
{
    Certificate = certificate,
    FieldName = "Signature1",
    Reason = "Approved for release",
    Location = "Vienna",
    HashAlgorithm = PdfHashAlgorithm.Sha256,
    Appearance = new PdfSignatureAppearance
    {
        PageNumber = 1,
        Rectangle = new PdfRectangle(340d, 40d, 540d, 100d),
    },
};

await using var output = File.Create("statement-signed.pdf");
var info = await signer.SignAsync(new PdfFileSource("statement.pdf"), output, options);
```

`signer` is an `IPdfSigner`, registered by `AddSharpCorePDF()` and constructible by hand as
`new PdfSignerService(new PdfDocumentSigner())` (Chapter 10). The source is opened, read to its end and
disposed; the destination belongs to the caller and is flushed, not closed.

The certificate's key decides the signature algorithm — an RSA key signs with PKCS#1 v1.5, an ECDSA key
with ECDSA — and `HashAlgorithm` decides the digest. `Chain` embeds the intermediate certificates, so a
reader that trusts the root can build the path from the document alone.

`Appearance` is optional. Without it the widget is invisible and zero-sized, which is what a server-side
signature usually wants. With it, the text and the border are drawn into a form XObject that travels in
the signed revision, so the drawing is covered by the signature rather than added to it later. `Text`
defaults to the signer's common name and the signing date. The rectangle is given as its four edges —
left, bottom, right and top — in PDF points measured from the bottom left of the page.

## The parts of the revision

| Part | What it holds |
| --- | --- |
| Signature dictionary | `/Filter /Adobe.PPKLite`, `/SubFilter` — `adbe.pkcs7.detached`, or `ETSI.CAdES.detached` when `UseCades` is set — `/ByteRange`, `/Contents`, and the entries a reader shows: `/Reason`, `/Location`, `/ContactInfo`, `/M`. |
| `/ByteRange` | Four numbers: where the covered bytes start, how many there are, where they resume after the placeholder, and how many follow. The covered bytes are the whole revision except the `/Contents` value. |
| `/Contents` | The signature value, as a hexadecimal string in a placeholder wide enough for the certificate chain and the token. |
| The CMS | A detached CMS `SignedData`: the signer's certificate, the digest algorithm, the signed attributes — content type, signing time, message digest, and the signing-certificate attribute of ETSI EN 319 122 when CAdES is asked for — and the signature over them. |
| The widget | An annotation of `/Subtype /Widget` with `/FT /Sig`, appended to the page's `/Annots`, and the field that names it in the `/AcroForm` of the document. |

The order matters, and it is why the value cannot be computed in one pass: the writer reserves the
placeholder, hashes the revision with the gap removed, patches `/ByteRange`, and only then writes the
signature into the gap. A count that changed after the hash would invalidate the hash.

## Timestamps

A timestamp is the caller's, never this library's, because obtaining one opens a socket:

```csharp
public sealed class MyAuthority : IPdfTimestampProvider
{
    public async Task<ReadOnlyMemory<byte>> GetTimestampTokenAsync(
        ReadOnlyMemory<byte> digest,
        PdfHashAlgorithm hashAlgorithm,
        CancellationToken cancellationToken = default)
    {
        // Ask your TSA for an RFC 3161 token over `digest`, computed with `hashAlgorithm`.
        return await myTsa.StampAsync(digest, hashAlgorithm, cancellationToken);
    }
}
```

Setting `TimestampProvider` hands the digest of the signature value to the provider and embeds the token
it answers with as the `signatureTimeStampToken` unsigned attribute. A provider that throws stops the
signature: the exception surfaces with the field it was for and no revision is written, so a document
that only looks signed cannot be produced.

The token is embedded, not checked. Verifying a TSA's own signature over it needs trust anchors this
release does not accept, so `PdfTrustOptions.CheckTimestampTrust` has no effect yet and
`PdfSignatureInfo.HasTimestamp` reports only that a token is there — one that cannot be read as a CMS
structure becomes a `TimestampInvalid` problem, which is a statement about the token's shape and not
about its time.

## Validating

```csharp
var validations = await validator.ValidateAsync(
    new PdfFileSource("statement-signed.pdf"),
    new PdfTrustOptions { ExtraRoots = [myRoot] });

foreach (var validation in validations)
{
    Console.WriteLine(validation);

    foreach (var problem in validation.Problems)
    {
        Console.WriteLine($"  {problem.Kind}: {problem.Message}");
    }
}
```

`validator` is an `IPdfSignatureValidator`, registered by `AddSharpCorePDF()`. Every signature the
document carries comes back, in the order the revisions were appended, which is the order a reader lists
them in. A document with no signature yields an empty list; a document that is not a PDF raises
`PdfSourceFormatException`, exactly like every other read.

`PdfSignatureValidation` carries the report — `Signer`, a `PdfSignatureInfo` — and the two verdicts:

| `Integrity` | Meaning |
| --- | --- |
| `Valid` | The covered bytes hash and verify to what the signature states. |
| `Invalid` | The covered bytes do not match, or the signature does not verify against the certificate. |
| `Unknown` | The signature could not be checked at all: unreadable structure, unsupported algorithm, or covered bytes that are not there. Never a success. |

`Trust` is `NotEvaluated` unless `PdfTrustOptions` is passed, `Trusted` when the chain reaches one of
`ExtraRoots`, and `Untrusted` otherwise — including when `ExtraRoots` is empty, which is how a caller
proves a document is signed by a certificate it already knows.

`Problems` explains every verdict. `Kind` is the contract — `DigestMismatch`, `CertificateExpired`,
`ChainUntrusted`, `RevisionNotCovered`, `TimestampInvalid` and the rest — so a caller matches on the kind
and logs the `Message`. A `switch` over it keeps a `default` arm, because a later release may add one.

## What validation does not do

No machine trust store is consulted, no certificate is fetched, and no authority is asked whether a
certificate was revoked. `AllowOnlineRevocationCheck` is a stated boundary rather than a feature: setting
it yields a `PdfSignatureProblemKind.OnlineRevocationUnsupported` problem and makes no network call,
because a library that quietly contacts a responder during validation is a privacy and a latency problem.
Revocation data embedded in the CMS is still reported.

Signing an encrypted document works when the password is passed, and the result stays encrypted.
Validation takes the same password for the same reason. Neither rewrites the document, so neither can
repair one: a signature that does not verify stays that way.

## Reading the result

`SignAsync` returns a `PdfSignatureInfo` read back from the file it wrote, so the report describes the
document the caller now holds rather than the arguments that produced it: `FieldName`, `SubFilter`,
`HashAlgorithm`, `SignerName`, `SignedAt`, `ByteRange`, `SignatureLength`, `CoversWholeDocument` and
`CoveredRevision`. Its `ToString()` is the field and the signer, and `PdfSignatureValidation.ToString()`
is the same field with the two verdicts:

```text
Signature1: Valid, trust Trusted
```

Once a second signature is appended, the first one no longer covers the whole document: its
`CoversWholeDocument` is `false` and its `CoveredRevision` names the revision it signed, while the second
reports `true`. That is the expected outcome and not a defect — a signature protects the bytes that
existed when it was made.

## Evidence

A signature that only its own writer believes is not a signature, so this release is checked by two
implementations that share no code with it:

```powershell
uv run --with pyhanko python docs/tools/validate-signatures.py `
  --trust-signer anchor.pem --require-trusted signed.pdf signed-twice.pdf
uv run --with pikepdf python docs/tools/validate-pdf.py signed.pdf signed-twice.pdf
```

pyHanko reports `integrity: intact` and `trust: trusted`, with coverage `ENTIRE_FILE` for a single
signature and `ENTIRE_REVISION` followed by `ENTIRE_FILE` for two; qpdf reports no problems on either
file. Both scripts live in `docs/tools/`, and `docs/parity.md` records what these runs measured.
