using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Domain.ReadModels;

/// <summary>
/// The outcome of checking one signature: the report, the integrity of the covered bytes and — when it was
/// asked for — the trust of the certificate chain.
/// </summary>
/// <remarks>
/// <see cref="Integrity"/> and <see cref="Trust"/> answer different questions and are reported
/// independently: a document can be intact and signed by a certificate nobody trusts, and it can be signed
/// by a certificate the caller trusts over bytes that were changed afterwards. <see cref="IsValid"/> speaks
/// only about integrity, because that is the only half this library can decide on its own.
/// </remarks>
/// <param name="Signer">What the signature states.</param>
/// <param name="Integrity">Whether the covered bytes still match what was signed.</param>
/// <param name="Trust">Whether the chain leads to an anchor the caller named.</param>
/// <param name="Problems">Everything that keeps the signature from being reported as valid.</param>
public sealed record PdfSignatureValidation(
    PdfSignatureInfo Signer,
    PdfSignatureIntegrity Integrity,
    PdfTrustResult Trust,
    IReadOnlyList<PdfSignatureProblem> Problems)
{
    /// <summary>Gets a value indicating whether the covered bytes still verify.</summary>
    public bool IsValid => Integrity == PdfSignatureIntegrity.Valid;

    /// <summary>Formats the validation for diagnostics.</summary>
    /// <returns>The field, the integrity and the trust, on one line.</returns>
    public override string ToString() => $"{Signer.FieldName}: {Integrity}, trust {Trust}";
}
