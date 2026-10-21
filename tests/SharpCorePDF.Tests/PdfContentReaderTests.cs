using System.Security.Cryptography;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the read side of the library: what a document already contains, read through the same
/// pieces of the engine the merger uses, and never written back.
/// </summary>
/// <remarks>
/// Every fixture is built in memory by <see cref="PdfContentFixtures"/>, so an assertion can name the
/// exact bytes, filter or destination it expects instead of describing a binary asset.
/// </remarks>
public sealed partial class PdfContentReaderTests
{
    /// <summary>Creates a reader with the default engine options.</summary>
    /// <returns>The use case, wired the way the container wires it.</returns>
    private static PdfContentReaderService Reader() => new(new PdfPageContentReader());

    /// <summary>Creates a reader with the given engine options.</summary>
    /// <param name="options">The options to use.</param>
    /// <returns>The use case.</returns>
    private static PdfContentReaderService Reader(PdfEngineOptions options) =>
        new(new PdfPageContentReader(options));

    /// <summary>Wraps document bytes in a content source.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <param name="name">The name used in diagnostics.</param>
    /// <returns>The source.</returns>
    private static PdfByteArraySource Source(byte[] bytes, string name = "fixture.pdf") => new(bytes, name);

    /// <summary>Gets the cancellation token of the running test.</summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Asserts that a result carries a remark of a kind.</summary>
    /// <param name="warnings">The remarks of the result.</param>
    /// <param name="kind">The kind that must be present.</param>
    private static void AssertWarning(IReadOnlyList<PdfWarning> warnings, PdfWarningKind kind) =>
        Assert.Contains(warnings, warning => warning.Kind == kind);

    /// <summary>Computes the digest a test uses to prove a document was not modified.</summary>
    /// <param name="bytes">The bytes of the document.</param>
    /// <returns>The SHA-256 digest.</returns>
    private static byte[] Digest(byte[] bytes) => SHA256.HashData(bytes);
}
