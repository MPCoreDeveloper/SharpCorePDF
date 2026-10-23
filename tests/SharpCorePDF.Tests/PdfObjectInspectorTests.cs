using System.Text;
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Reading;
using SharpCorePDF.Infrastructure.Sources;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the object inspector: the graph a file lists, the holes in that graph and the operations a page
/// states.
/// </summary>
/// <remarks>
/// The inspector is wired the way the container wires it — the use case in front of the engine — so every
/// assertion also covers the argument checks the surface promises. Each fixture is built in memory by
/// <see cref="PdfContentFixtures"/>, which lets a test name the object number, filter or operator it expects
/// instead of describing a binary asset.
/// </remarks>
public sealed class PdfObjectInspectorTests
{
    /// <summary>Creates an inspector with the default engine options.</summary>
    /// <returns>The use case, wired the way the container wires it.</returns>
    private static PdfObjectInspectorService Inspector() => new(new PdfObjectInspector());

    /// <summary>Creates an inspector with the given engine options.</summary>
    /// <param name="options">The options to use.</param>
    /// <returns>The use case.</returns>
    private static PdfObjectInspectorService Inspector(PdfEngineOptions options) =>
        new(new PdfObjectInspector(options));

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

    /// <summary>Reads a node of the graph by the object number the file gives it.</summary>
    /// <param name="result">The graph.</param>
    /// <param name="objectNumber">The object number to find.</param>
    /// <returns>The node of that object.</returns>
    private static PdfObjectView Object(PdfReadResult<PdfObjectView> result, long objectNumber) =>
        Assert.Single(result.Items, view => view.ObjectNumber == objectNumber);

    /// <summary>Reads a child of a node by the key the document writes it under.</summary>
    /// <param name="view">The node that holds the entry.</param>
    /// <param name="key">The name of the entry.</param>
    /// <returns>The child of that entry.</returns>
    private static PdfObjectView Child(PdfObjectView view, string key) =>
        Assert.Single(view.Children, child => string.Equals(child.Key, key, StringComparison.Ordinal));

    /// <summary>Every object the file lists becomes one node, in the order the numbers run.</summary>
    [Fact]
    public async Task Returns_one_node_per_listed_object_in_number_order()
    {
        var result = await Inspector().ReadObjectsAsync(Source(PdfContentFixtures.ObjectGraph()), cancellationToken: Token);

        long[] expected = [1, 2, 3, 4, 5, 6, 7];

        Assert.Equal(expected, result.Items.Select(view => view.ObjectNumber!.Value));
        Assert.All(result.Items, view => Assert.Equal(0, view.Generation));

        var catalog = result.Items[0];

        Assert.Equal(PdfObjectViewKind.Dictionary, catalog.Kind);
        Assert.Equal("Catalog", Child(catalog, "Type").Scalar as string);
        Assert.Equal(new PdfObjectReference(2, 0), Child(catalog, "Pages").Reference);
    }

    /// <summary>A reference is a node that is named, never one that is walked into.</summary>
    /// <remarks>
    /// The fixture holds two objects that point at each other. A graph that followed references would never
    /// finish; the inspector reports the edge and stops, so the caller decides whether to follow it.
    /// </remarks>
    [Fact]
    public async Task Projects_a_reference_as_a_node_and_never_follows_it()
    {
        var result = await Inspector().ReadObjectsAsync(Source(PdfContentFixtures.ObjectGraph()), cancellationToken: Token);

        var first = Child(Object(result, 5), "Peer");

        Assert.Equal(PdfObjectViewKind.Reference, first.Kind);
        Assert.Equal(new PdfObjectReference(6, 0), first.Reference);
        Assert.True(first.IsResolved);
        Assert.Empty(first.Children);
        Assert.Null(first.Scalar);

        var second = Child(Object(result, 6), "Peer");

        Assert.Equal(new PdfObjectReference(5, 0), second.Reference);
        Assert.Empty(second.Children);
    }

    /// <summary>A reference to an object the file does not hold is a hole, reported once per target.</summary>
    /// <remarks>
    /// The page names object 99 twice and object 5 names it once. One remark for the target is the useful
    /// statement: the file has one hole, not three, and the caller who reads the warnings still sees the
    /// nodes that point at it with <c>IsResolved</c> cleared.
    /// </remarks>
    [Fact]
    public async Task Reports_a_dangling_reference_once_for_the_target_it_names()
    {
        var result = await Inspector().ReadObjectsAsync(Source(PdfContentFixtures.ObjectGraph()), cancellationToken: Token);

        var example = Child(Object(result, 3), "Example");
        var also = Child(Object(result, 3), "Also");
        var missing = Child(Object(result, 5), "Missing");

        Assert.All(
            new[] { example, also, missing },
            node =>
            {
                Assert.Equal(PdfObjectViewKind.Reference, node.Kind);
                Assert.Equal(new PdfObjectReference(99, 0), node.Reference);
                Assert.False(node.IsResolved);
            });

        var remark = Assert.Single(result.Warnings, warning => warning.Kind == PdfWarningKind.DanglingReference);

        Assert.Contains("99", remark.Message, StringComparison.Ordinal);
        Assert.Contains("fixture.pdf", remark.Message, StringComparison.Ordinal);
    }

    /// <summary>A stream is described by the filters it declares and the length it stores.</summary>
    [Fact]
    public async Task Describes_a_stream_by_its_filter_chain_and_its_length()
    {
        var result = await Inspector().ReadObjectsAsync(Source(PdfContentFixtures.ObjectGraph()), cancellationToken: Token);

        var stream = Object(result, 7);

        Assert.Equal(PdfObjectViewKind.Stream, stream.Kind);
        Assert.Equal("FlateDecode ASCIIHexDecode", stream.Filter);
        Assert.Equal((long)PdfContentFixtures.GraphStreamData.Length, stream.StreamLength);
        Assert.Equal("Metadata", Child(stream, "Type").Scalar as string);
    }

    /// <summary>The operations of a page are reported in the order the page states them.</summary>
    /// <remarks>
    /// The samples of the inline image end with a byte that is a delimiter, so a read that ended the image on
    /// a delimiter rather than on <c>EI</c> standing alone would lose the operators that follow it.
    /// </remarks>
    [Fact]
    public async Task Reads_the_operations_of_a_page_in_the_order_it_states_them()
    {
        var result = await Inspector().ReadOperationsAsync(
            Source(PdfContentFixtures.ContentOperations()),
            1,
            cancellationToken: Token);

        string[] expected = ["q", "cm", "BI", "Q", "Do"];

        Assert.Equal(expected, result.Items.Select(operation => operation.Operator));
        Assert.All(result.Items, operation => Assert.Equal(1, operation.PageNumber));
        Assert.True(
            result.Items.Zip(result.Items.Skip(1)).All(pair => pair.First.Offset < pair.Second.Offset),
            "the operations must be reported at increasing offsets");

        var matrix = result.Items[1];

        long[] operands = [1, 0, 0, 1, 10, 20];

        Assert.Equal(operands, matrix.Operands.Select(operand => (long)operand.Scalar!));
        Assert.All(matrix.Operands, operand => Assert.Equal(PdfObjectViewKind.Integer, operand.Kind));

        // The entries of the inline image's header are its operands; its samples are skipped, not reported.
        var image = result.Items[2];

        Assert.Equal(8, image.Operands.Count);
        Assert.Equal("W", image.Operands[0].Scalar as string);
        Assert.Equal(8L, (long)image.Operands[image.Operands.Count - 1].Scalar!);

        var invocation = result.Items[4];

        Assert.Equal(PdfObjectViewKind.Name, Assert.Single(invocation.Operands).Kind);
        Assert.Equal("Fm1", invocation.Operands[0].Scalar as string);
    }

    /// <summary>A page that nests deeper than the limit stops being descended, and the read carries on.</summary>
    /// <remarks>
    /// The limit is four here, the smallest the options accept, and the stream nests eight arrays. The read
    /// must say it stopped descending and still report the operators written after the closers.
    /// </remarks>
    [Fact]
    public async Task Stops_descending_at_the_nesting_limit_and_keeps_reading()
    {
        var result = await Inspector(PdfEngineOptions.Default with { MaxNestingDepth = 4 })
            .ReadOperationsAsync(Source(PdfContentFixtures.NestedContent(8)), 1, cancellationToken: Token);

        string[] expected = ["S", "BT", "ET"];

        Assert.Equal(expected, result.Items.Select(operation => operation.Operator));
        AssertWarning(result.Warnings, PdfWarningKind.ContentNestingTooDeep);
    }

    /// <summary>A stream that nests within the limit is read to its end without a remark.</summary>
    [Fact]
    public async Task Reads_a_stream_that_nests_within_the_limit_without_a_remark()
    {
        var result = await Inspector(PdfEngineOptions.Default with { MaxNestingDepth = 8 })
            .ReadOperationsAsync(Source(PdfContentFixtures.NestedContent(8)), 1, cancellationToken: Token);

        string[] expected = ["S", "BT", "ET"];

        Assert.Equal(expected, result.Items.Select(operation => operation.Operator));
        Assert.Empty(result.Warnings);
    }

    /// <summary>A page number past the last page is an empty read rather than a failure.</summary>
    [Fact]
    public async Task Answers_a_page_past_the_last_with_nothing()
    {
        var result = await Inspector().ReadOperationsAsync(
            Source(PdfContentFixtures.ContentOperations()),
            9,
            cancellationToken: Token);

        Assert.Empty(result.Items);
        Assert.Empty(result.Warnings);
    }

    /// <summary>A page number below one is refused before the source is opened.</summary>
    [Fact]
    public async Task Refuses_a_page_number_below_one()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Inspector().ReadOperationsAsync(
                Source(PdfContentFixtures.ContentOperations()),
                0,
                cancellationToken: Token));
    }

    /// <summary>Bytes that hold no document are refused with the name of the source.</summary>
    [Fact]
    public async Task Refuses_bytes_that_are_not_a_document()
    {
        var source = Source(Encoding.Latin1.GetBytes("this is not a PDF file at all"), "damaged.pdf");

        var exception = await Assert.ThrowsAsync<PdfSourceFormatException>(
            () => Inspector().ReadObjectsAsync(source, cancellationToken: Token));

        Assert.Equal("damaged.pdf", exception.SourceName);
    }

    /// <summary>A document whose encryption cannot be opened lists nothing and says why.</summary>
    /// <remarks>
    /// The graph and the operations both come from the same page walk, so an encryption that keeps the
    /// document closed closes both: the answer is no item plus one remark, never a half-read document.
    /// </remarks>
    [Fact]
    public async Task Lists_nothing_for_a_document_whose_encryption_it_cannot_open()
    {
        var source = Source(PdfContentFixtures.UnopenedEncryption());

        var objects = await Inspector().ReadObjectsAsync(source, cancellationToken: Token);
        var operations = await Inspector().ReadOperationsAsync(source, 1, cancellationToken: Token);

        Assert.Empty(objects.Items);
        Assert.Empty(operations.Items);
        AssertWarning(objects.Warnings, PdfWarningKind.EncryptionNotOpened);
        AssertWarning(operations.Warnings, PdfWarningKind.EncryptionNotOpened);
    }

    /// <summary>A page names its content stream, and the graph holds the node that reference points at.</summary>
    /// <remarks>
    /// The inspector never resolves the reference for the caller, but because it projects every object the
    /// file lists, the target of a reference is a node of the same answer: a caller can follow an edge by
    /// looking it up rather than by asking for the document again.
    /// </remarks>
    [Fact]
    public async Task Holds_the_node_a_page_reference_points_at()
    {
        var result = await Inspector().ReadObjectsAsync(Source(PdfContentFixtures.ObjectGraph()), cancellationToken: Token);

        var page = Object(result, 3);
        var contents = Child(page, "Contents");

        Assert.Equal(new PdfObjectReference(4, 0), contents.Reference);
        Assert.Empty(contents.Children);
        Assert.Equal(PdfObjectViewKind.Dictionary, Child(page, "Resources").Kind);

        var stream = Object(result, 4);

        Assert.Equal(PdfObjectViewKind.Stream, stream.Kind);
        Assert.Null(stream.Filter);
        Assert.True(stream.StreamLength > 0);
    }
}
