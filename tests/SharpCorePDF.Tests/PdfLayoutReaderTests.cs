using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Reading;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the reading surface: the letters a page draws, the words and blocks they make, the links the
/// page carries and the exports that describe them.
/// </summary>
/// <remarks>
/// Every fixture is built in memory by <see cref="PdfContentFixtures"/>, so an assertion can name the exact
/// position, font and text it expects instead of describing a binary asset. The reader is wired the way the
/// container wires it: the use case in front of the engine.
/// </remarks>
public sealed partial class PdfLayoutReaderTests
{
    /// <summary>Creates a reader with the default engine options.</summary>
    /// <returns>The use case, wired the way the container wires it.</returns>
    private static PdfLayoutReaderService Reader() => new(new PdfLayoutReader());

    /// <summary>Creates a reader with the given engine options.</summary>
    /// <param name="options">The options to use.</param>
    /// <returns>The use case.</returns>
    private static PdfLayoutReaderService Reader(PdfEngineOptions options) => new(new PdfLayoutReader(options));

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
}
