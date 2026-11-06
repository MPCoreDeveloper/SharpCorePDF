using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the text scanner: the strings a content stream shows, decoded exactly as the
/// specification says, and the order of a <c>TJ</c> array preserved.
/// </summary>
/// <remarks>
/// The scanner has two paths for a literal string — a slice of the stream when the string needs no
/// decoding, a pooled buffer when it does — so every shape that decides which path runs is fed here:
/// an escape at the first byte, an escape a hundred bytes in, a nested parenthesis, a bare carriage
/// return, an odd hex string and a string that is never closed.
/// </remarks>
public sealed class PdfTextScannerTests
{
    /// <summary>A string the scanner can take as a slice of the stream.</summary>
    /// <param name="content">The content stream.</param>
    /// <param name="expected">The text the shown string must decode to.</param>
    [Theory]
    [InlineData("(hello) Tj", "hello")]
    [InlineData("() Tj", "")]
    [InlineData(@"(a\(b\)c) Tj", "a(b)c")]
    [InlineData(@"(a\\b) Tj", @"a\b")]
    [InlineData(@"(a\nb) Tj", "a\nb")]
    [InlineData(@"(a\tb) Tj", "a\tb")]
    [InlineData(@"(a(b)c) Tj", "a(b)c")]
    [InlineData(@"(\101\102) Tj", "AB")]
    [InlineData(@"(\1018) Tj", "A8")]
    [InlineData("(a\rb) Tj", "a\nb")]
    [InlineData("<48656C6C6F> Tj", "Hello")]
    [InlineData("<48656C6C6> Tj", "Hell`")]
    public void Reads_the_bytes_of_a_shown_string(string content, string expected)
    {
        var operation = Assert.Single(Snapshot(content));

        Assert.Equal("Tj", operation.Operator);
        Assert.Equal(expected, Assert.Single(operation.Strings));
    }

    /// <summary>An escape far into a long string still decodes, however the kernel splits the scan.</summary>
    [Fact]
    public void Reads_a_long_string_with_an_escape_far_from_its_start()
    {
        var content = "(" + new string('a', 200) + @"\(b) Tj";

        var operation = Assert.Single(Snapshot(content));

        Assert.Equal(new string('a', 200) + "(b", Assert.Single(operation.Strings));
    }

    /// <summary>A long string with nothing to decode stays one unbroken run of bytes.</summary>
    [Fact]
    public void Reads_a_long_string_without_decoding()
    {
        var content = "(" + new string('a', 200) + ") Tj";

        var operation = Assert.Single(Snapshot(content));

        Assert.Equal(new string('a', 200), Assert.Single(operation.Strings));
    }

    /// <summary>A <c>TJ</c> array keeps its numbers and strings in the order the stream states them.</summary>
    [Fact]
    public void Keeps_the_order_of_a_TJ_array()
    {
        var operation = Assert.Single(Snapshot("[ (A) -120 <42> ] TJ"));

        Assert.Equal("TJ", operation.Operator);

        var items = operation.Items;

        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal(TjItemKind.String, item.Kind);
                Assert.Equal("A", item.Text);
            },
            item =>
            {
                Assert.Equal(TjItemKind.Number, item.Kind);
                Assert.Equal(-120d, item.Number);
            },
            item =>
            {
                Assert.Equal(TjItemKind.String, item.Kind);
                Assert.Equal("B", item.Text);
            });
    }

    /// <summary>An unterminated string consumes the rest of the stream, and the scan still ends.</summary>
    /// <remarks>
    /// The string swallows the operator that would have shown it, so the scan yields nothing at all.
    /// What the test pins is that it yields rather than reading past the bytes or looping.
    /// </remarks>
    [Fact]
    public void Ends_the_scan_inside_an_unterminated_string()
    {
        Assert.Empty(Snapshot("(abc"));
        Assert.Empty(Snapshot("<414243"));
    }

    /// <summary>A name's <c>#XX</c> escape is decoded, including one that ends the name.</summary>
    [Fact]
    public void Decodes_a_name_escape_that_ends_the_name()
    {
        var operation = Assert.Single(Snapshot("/F#31 12 Tf"));

        Assert.Equal("Tf", operation.Operator);
        Assert.Equal("F1", Assert.Single(operation.Names));
    }

    /// <summary>An inline image is stepped over, and the text drawn after it is still read.</summary>
    /// <remarks>
    /// The data between <c>ID</c> and <c>EI</c> may contain the letters <c>EI</c>, so the terminator is
    /// only the one a keyword could stand at — whitespace before it, whitespace or a delimiter after.
    /// This stream holds both shapes: a false <c>EI</c> inside the data and the real one after it.
    /// </remarks>
    [Fact]
    public void Reads_the_text_after_an_inline_image()
    {
        var operation = Assert.Single(Snapshot("BI /W 2 /H 1 /BPC 8 /CS /G ID \u0001EI\u0002 EI (after) Tj"));

        Assert.Equal("Tj", operation.Operator);
        Assert.Equal("after", Assert.Single(operation.Strings));
    }

    /// <summary>The literal names are operands, not operators, and do not become one.</summary>
    [Fact]
    public void Skips_the_literal_names()
    {
        var operation = Assert.Single(Snapshot("true false null (x) Tj"));

        Assert.Equal("Tj", operation.Operator);
        Assert.Equal("x", Assert.Single(operation.Strings));
    }

    private static OperationSnapshot[] Snapshot(string content)
    {
        var data = Encoding.Latin1.GetBytes(content);
        var snapshots = new List<OperationSnapshot>();

        foreach (var operation in PdfTextScanner.Scan(data))
        {
            // The scan reuses one instance, so each operation is snapshotted before the next MoveNext.
            snapshots.Add(new OperationSnapshot(
                operation.Operator,
                operation.Strings.Select(value => Encoding.Latin1.GetString(value.Span)).ToArray(),
                operation.Numbers.ToArray(),
                operation.Names.ToArray(),
                operation.Items?
                    .Select(item => (
                        item.Kind,
                        item.Number,
                        item.Kind == TjItemKind.String ? Encoding.Latin1.GetString(item.Bytes.Span) : string.Empty))
                    .ToArray() ?? []));
        }

        return [.. snapshots];
    }

    private sealed record OperationSnapshot(
        string Operator,
        string[] Strings,
        double[] Numbers,
        string[] Names,
        (TjItemKind Kind, double Number, string Text)[] Items);
}
