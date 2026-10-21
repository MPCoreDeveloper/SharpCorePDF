# Contract: Signatures and Archival Profiles

**Feature**: `003-pdf-platform-parity` | **Type**: public library API

Signing and PDF/A are opt-in output modes. A caller can create, edit and read a document without ever
touching a certificate, a trust store or an output profile.

## Signing

```csharp
public interface IPdfSigner
{
    Task<PdfSignatureInfo> SignAsync(
        IPdfContentSource source,
        Stream destination,
        PdfSignatureOptions options,
        string? password = null,
        CancellationToken cancellationToken = default);
}

public sealed record PdfSignatureOptions
{
    public X509Certificate2 Certificate { get; init; }
    public IReadOnlyList<X509Certificate2>? Chain { get; init; }
    public PdfHashAlgorithm HashAlgorithm { get; init; } = PdfHashAlgorithm.Sha256;
    public string FieldName { get; init; } = "Signature1";
    public string? Reason { get; init; }
    public string? Location { get; init; }
    public string? ContactInfo { get; init; }
    public PdfSignatureAppearance? Appearance { get; init; }
    public IPdfTimestampProvider? TimestampProvider { get; init; }
    public bool UseCades { get; init; }
}

public interface IPdfTimestampProvider
{
    Task<ReadOnlyMemory<byte>> GetTimestampTokenAsync(
        ReadOnlyMemory<byte> digest,
        PdfHashAlgorithm hashAlgorithm,
        CancellationToken cancellationToken = default);
}
```

**Guarantees**

- The signature is a detached CMS/PKCS#7 over the PDF byte range, written in an incremental revision.
- The original revision’s bytes are not rewritten; a previous signature remains valid after signing a
  later revision.
- A certificate without a private key is refused before any bytes are written.
- A timestamp is embedded only when the caller’s provider returns a token; the library never opens a
  socket.
- The signature dictionary names its `/SubFilter`, `/ByteRange`, `/Contents`, `/M` and the signer’s
  certificate chain as far as the options allow.
- The visible appearance, when supplied, is drawn in the signature widget’s rectangle and is included
  in the signed revision.

## Validation

```csharp
public interface IPdfSignatureValidator
{
    Task<IReadOnlyList<PdfSignatureValidation>> ValidateAsync(
        IPdfContentSource source,
        PdfTrustOptions? trust = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}

public sealed record PdfSignatureValidation(
    PdfSignatureInfo Signer,
    PdfSignatureIntegrity Integrity,
    PdfTrustResult Trust,
    IReadOnlyList<PdfSignatureProblem> Problems)
{
    public bool IsValid => Integrity == PdfSignatureIntegrity.Valid;
}
```

**Guarantees**

- `Integrity` answers “do the covered bytes match the signed digest?”; `Trust` answers “does the
  certificate chain lead to a root the caller trusts?”. They are independent.
- The default `Trust` is `NotEvaluated`; the library does not ship a root store.
- A modified byte inside the covered range yields `Integrity = Invalid` and at least one problem that
  names the byte range.
- A signature that does not cover the whole current revision is reported with
  `CoversWholeDocument = false` and the revision it covers.
- Every signature in a multi-signed document is returned, in revision order.
- A structure the parser cannot read yields `Integrity = Unknown` plus a problem, never a false
  `Valid`.

## Archival Profiles

```csharp
public enum PdfAProfile
{
    PdfA1B,
    PdfA2B,
    PdfA3B,
}

public interface IPdfAWriter
{
    Task<PdfAConversionResult> ConvertAsync(
        IPdfContentSource source,
        Stream destination,
        PdfAProfile profile,
        PdfAConversionOptions? options = null,
        string? password = null,
        CancellationToken cancellationToken = default);
}
```

**Guarantees**

- PDF/A-1b, -2b and -3b are written as complete documents, not incremental updates.
- Every font is embedded and subsetted; a font that cannot be embedded is a hard refusal.
- The output intent, XMP packet, document information and catalog entries required by the profile are
  written.
- Encryption, JavaScript, audio/video and other profile-forbidden features are removed or refused, with
  the reason recorded.
- `PdfAConversionResult.UnsatisfiedRequirements` is empty only when the writer believes the profile is
  satisfied; the library still does not claim conformance without veraPDF evidence.
- A profile that cannot be satisfied returns `IsWritten = false`; it does not produce a file labelled
  PDF/A that is not one.

## Interaction With Editing

- `PdfEditOutputOptions.PdfAProfile` uses the same writer for an edit that targets an archival profile.
- A PDF/A profile cannot be combined with encryption; validation reports the conflict before opening
  the source.
- Signing after PDF/A conversion is allowed only if the chosen signature sub-filter is permitted by the
  profile; otherwise the signer refuses with the conflicting profile named.

## Trust Options

```csharp
public sealed record PdfTrustOptions
{
    public IReadOnlyList<X509Certificate2>? ExtraRoots { get; init; }
    public bool AllowOnlineRevocationCheck { get; init; }
    public bool CheckTimestampTrust { get; init; } = true;
}
```

- `AllowOnlineRevocationCheck` is **not implemented by the library** in 1.1. The property exists only
  to make the boundary explicit; setting it to true yields a validation problem, not a network call.
- `ExtraRoots` augments the caller’s chosen trust anchors. The library never consults the machine store
  implicitly.
- Revocation data embedded in the CMS is reported; fetching revocation is the caller’s job.

## Signature Problems

`PdfSignatureProblemKind` covers at least: `MalformedByteRange`, `DigestMismatch`,
`CertificateMissing`, `CertificateExpired`, `CertificateNotYetValid`, `ChainIncomplete`,
`ChainUntrusted`, `UnsupportedAlgorithm`, `TimestampMissing`, `TimestampInvalid`,
`RevisionNotCovered`, and `StructureUnreadable`.

## PDF/A Conversion Result

```csharp
public sealed record PdfAConversionResult
{
    public PdfAProfile Profile { get; }
    public bool IsWritten { get; }
    public IReadOnlyList<PdfWarning> Warnings { get; }
    public IReadOnlyList<string> UnsatisfiedRequirements { get; }
    public int PageCount { get; }
    public long BytesWritten { get; }
}
```

## Diagnostics

| Condition | Result |
| --- | --- |
| Certificate has no private key | `ArgumentException` before opening the source |
| Unsupported hash algorithm | `PdfUnsupportedFeatureException` naming the algorithm |
| Timestamp provider throws | Signature is not written; the exception is surfaced with context |
| Byte range invalid or overlapping | `PdfSourceFormatException` |
| Signature covers an older revision | Integrity of that revision is reported; current revision is marked not covered |
| Certificate chain untrusted | `Trust = Untrusted`, `Integrity` unchanged |
| PDF/A font cannot be embedded | `IsWritten = false`, requirement named |
| PDF/A profile + encryption | Plan validation refuses before opening the source |
| Profile-forbidden action found | Removed if safe, otherwise conversion refused with the object named |

## Out of Contract

- Public-key encryption (recipient certificates) and certificate-based encryption.
- Online revocation, OCSP/CRL fetching, or a bundled trust store.
- Long-term validation (LTV) and PAdES-LTV.
- A PDF/UA or PDF/X writer.
- A PDF/A *validator*; veraPDF remains the external authority.
- Certifying signatures, document locking and field-lock semantics beyond copying the field dictionary.

