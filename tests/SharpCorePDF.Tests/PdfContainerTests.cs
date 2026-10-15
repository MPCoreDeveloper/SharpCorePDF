using System.Text;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the containers of the object model and of the writer: how entries are read back out of a
/// dictionary, what an array, a reference and a stream report, and what the writer produces for them.
/// </summary>
/// <remarks>
/// A dictionary keeps the order its entries were set in, because that order is what makes a merged document
/// readable by hand and stable between two runs of the same merge.
/// </remarks>
public sealed class PdfContainerTests
{
    /// <summary>A dictionary keeps the order of its entries.</summary>
    /// <remarks>
    /// The text of a dictionary is a diagnostic and not syntax: a name is shown as it is stored, so the slash
    /// appears once, on the key. The writer is what produces syntax, and it is asserted further down.
    /// </remarks>
    [Fact]
    public void Keeps_the_order_of_its_entries()
    {
        var dictionary = new PdfDictionary()
            .Set("Type", new PdfName("Catalog"))
            .SetInteger("Count", 2)
            .Set("Pages", new PdfReference(3, 0));

        Assert.Equal(["Type", "Count", "Pages"], dictionary.Keys.ToArray());
        Assert.Equal(["Type", "Count", "Pages"], dictionary.Entries.Select(entry => entry.Key).ToArray());
        Assert.Equal(3, dictionary.Count);
        Assert.Equal(PdfObjectKind.Dictionary, dictionary.Kind);
        Assert.True(dictionary.ContainsKey("Type"));
        Assert.False(dictionary.ContainsKey("Absent"));
        Assert.Equal("<< /Type Catalog /Count 2 /Pages 3 0 R >>", dictionary.ToString());
    }

    /// <summary>Setting an entry again keeps its place, and a null value removes the entry.</summary>
    [Fact]
    public void Replaces_and_removes_entries()
    {
        var dictionary = new PdfDictionary().Set("A", new PdfInteger(1)).Set("B", new PdfInteger(2));

        dictionary.Set("A", new PdfInteger(3));

        Assert.Equal(["A", "B"], dictionary.Keys.ToArray());
        Assert.Equal(3L, dictionary.GetInteger("A"));

        Assert.True(dictionary.Remove("A"));
        Assert.False(dictionary.Remove("A"));
        Assert.Equal(["B"], dictionary.Keys.ToArray());

        dictionary.Set("B", null);

        Assert.Equal(0, dictionary.Count);
        Assert.Null(dictionary.Get("B"));
    }

    /// <summary>A dictionary can be filled from a set of entries, in the order they are handed in.</summary>
    [Fact]
    public void Copies_a_set_of_entries()
    {
        var entries = new List<KeyValuePair<string, PdfObject>>
        {
            new("A", new PdfInteger(1)),
            new("B", PdfNull.Instance),
        };

        var dictionary = new PdfDictionary(entries);

        Assert.Equal(["A", "B"], dictionary.Keys.ToArray());
        Assert.Same(PdfNull.Instance, dictionary.Get("B"));
    }

    /// <summary>An entry without a key is a programmer error.</summary>
    [Fact]
    public void Refuses_an_entry_without_a_key() =>
        Assert.Throws<ArgumentException>(() => new PdfDictionary().Set(string.Empty, PdfNull.Instance));

    /// <summary>The readers of a dictionary answer with the type they were asked for, or with nothing.</summary>
    [Fact]
    public void Reads_entries_by_type()
    {
        var dictionary = new PdfDictionary()
            .SetInteger("Count", 2)
            .SetInteger("Width", 612)
            .Set("Measure", new PdfReal(2.5d))
            .Set("Integral", new PdfReal(3d))
            .Set("Type", new PdfName("Catalog"))
            .Set("Title", PdfString.FromText("Report"))
            .Set("Pages", new PdfReference(3, 0))
            .Set("Kids", new PdfArray([new PdfInteger(1)]))
            .Set("Info", new PdfDictionary());

        Assert.Equal(2L, dictionary.GetInteger("Count"));
        Assert.Equal(3L, dictionary.GetInteger("Integral"));
        Assert.Null(dictionary.GetInteger("Measure"));
        Assert.Null(dictionary.GetInteger("Type"));

        Assert.Equal(612d, dictionary.GetNumber("Width"));
        Assert.Equal(2.5d, dictionary.GetNumber("Measure"));
        Assert.Null(dictionary.GetNumber("Type"));

        Assert.Equal("Catalog", dictionary.GetName("Type"));
        Assert.Null(dictionary.GetName("Count"));

        Assert.Equal("Report", dictionary.GetText("Title"));
        Assert.Null(dictionary.GetText("Type"));

        Assert.Equal(3L, dictionary.GetReference("Pages")!.ObjectNumber);
        Assert.Null(dictionary.GetReference("Type"));

        Assert.Single(dictionary.GetArray("Kids")!.Items);
        Assert.Null(dictionary.GetArray("Type"));

        Assert.Empty(dictionary.GetDictionary("Info")!.Entries);
        Assert.Null(dictionary.GetDictionary("Type"));
    }

    /// <summary>An array appends the items it is built from and reports them in order.</summary>
    /// <remarks>
    /// Like the dictionary, the text of an array is a diagnostic: it shows the values as they are stored
    /// rather than in syntax, which is what the writer produces instead.
    /// </remarks>
    [Fact]
    public void Builds_an_array()
    {
        var array = new PdfArray([new PdfInteger(1), new PdfName("Two")]).Add(PdfString.FromText("three"));
        var empty = new PdfArray();

        Assert.Equal(3, array.Count);
        Assert.Equal(PdfObjectKind.Array, array.Kind);
        Assert.Equal("[1 Two three]", array.ToString());
        Assert.Equal(0, empty.Count);
        Assert.Equal("[]", empty.ToString());
    }

    /// <summary>Two references to the same object are the same reference.</summary>
    [Fact]
    public void Compares_references()
    {
        var reference = new PdfReference(12, 1);

        Assert.Equal(12L, reference.ObjectNumber);
        Assert.Equal(1, reference.Generation);
        Assert.Equal(PdfObjectKind.Reference, reference.Kind);
        Assert.Equal("12 1 R", reference.ToString());
        Assert.True(reference.Equals(new PdfReference(12, 1)));
        Assert.True(reference.Equals((object)new PdfReference(12, 1)));
        Assert.False(reference.Equals(new PdfReference(12, 0)));
        Assert.False(reference.Equals(new PdfReference(13, 1)));
        Assert.False(reference.Equals(null));
        Assert.Equal(reference.GetHashCode(), new PdfReference(12, 1).GetHashCode());
    }

    /// <summary>A stream reports its body and its dictionary.</summary>
    [Fact]
    public void Reports_a_stream()
    {
        var body = new byte[] { 1, 2, 3 };
        var stream = new PdfStream(new PdfDictionary().SetInteger("Length", 3), body);

        Assert.Equal(PdfObjectKind.Stream, stream.Kind);
        Assert.Equal(3, stream.Raw.Length);
        Assert.Equal(3L, stream.Dictionary.GetInteger("Length"));
        Assert.Equal("stream(3 bytes, 1 entries)", stream.ToString());
    }

    /// <summary>The writer writes the scalars in their syntax, and a missing object as null.</summary>
    [Fact]
    public void Writes_the_scalars()
    {
        Assert.Equal("null", Write(null));
        Assert.Equal("null", Write(PdfNull.Instance));
        Assert.Equal("true", Write(PdfBoolean.True));
        Assert.Equal("false", Write(PdfBoolean.False));
        Assert.Equal("42", Write(new PdfInteger(42)));
        Assert.Equal("3.25", Write(new PdfReal(3.25d)));
        Assert.Equal("/Catalog", Write(new PdfName("Catalog")));
        Assert.Equal("12 0 R", Write(new PdfReference(12, 0)));
    }

    /// <summary>A name is escaped only where the syntax requires it.</summary>
    [Fact]
    public void Writes_a_name_with_the_escapes_it_needs()
    {
        Assert.Equal("/A#20B", Write(new PdfName("A B")));
        Assert.Equal("/A#23B", Write(new PdfName("A#B")));
        Assert.Equal("/A#2FB", Write(new PdfName("A/B")));
        Assert.Equal("/", Write(new PdfName(string.Empty)));

        // A character outside Latin-1 has no representation in a name, so a question mark is written rather
        // than failing in the middle of a document.
        Assert.Equal("/A?", Write(new PdfName("A\u20ac")));
    }

    /// <summary>A string is written with the escapes and the octal codes the syntax requires.</summary>
    [Fact]
    public void Writes_a_string_with_the_escapes_it_needs()
    {
        var bytes = new byte[] { 0x00, 0x08, 0x0C, 0x7F, 0x80 };

        Assert.Equal("(\\000\\b\\f\\177\\200)", Write(new PdfString(bytes, isHexadecimal: false)));
        Assert.Equal("<00080C7F80>", Write(new PdfString(bytes, isHexadecimal: true)));
    }

    /// <summary>An array and a dictionary are written with their contents, in order.</summary>
    /// <remarks>
    /// An empty container is written with the spaces of the syntax around nothing, which is what makes the
    /// written form valid for a reader that splits on tokens instead of characters.
    /// </remarks>
    [Fact]
    public void Writes_containers()
    {
        var dictionary = new PdfDictionary().Set("Type", new PdfName("Catalog")).SetInteger("Count", 2);
        var array = new PdfArray([new PdfInteger(1), new PdfName("Two"), PdfString.FromText("three")]);

        Assert.Equal("<< /Type /Catalog /Count 2 >>", Write(dictionary));
        Assert.Equal("[1 /Two (three)]", Write(array));
        Assert.Equal("<< >>", Write(new PdfDictionary()));
        Assert.Equal("[]", Write(new PdfArray()));
    }

    /// <summary>A stream is written with its exact length, whatever the stored length claims.</summary>
    /// <remarks>
    /// The stored length may be wrong or indirect, so the written form always states the size of the body; that
    /// is what makes the output unambiguous for the next reader.
    /// </remarks>
    [Fact]
    public void Writes_a_stream_with_its_exact_length()
    {
        var body = new byte[] { 1, 2, 3 };
        var stored = new PdfStream(new PdfDictionary().SetInteger("Length", 99).SetName("Subtype", "XML"), body);
        var indirect = new PdfStream(new PdfDictionary().Set("Length", new PdfReference(7, 0)), body);
        var absent = new PdfStream(new PdfDictionary(), body);

        Assert.Equal("<< /Length 3 /Subtype /XML >>\nstream\n\u0001\u0002\u0003\nendstream", Write(stored));
        Assert.Equal("<< /Length 3 >>\nstream\n\u0001\u0002\u0003\nendstream", Write(indirect));
        Assert.Equal("<< /Length 3 >>\nstream\n\u0001\u0002\u0003\nendstream", Write(absent));
    }

    /// <summary>What the writer produces is what the lexer reads back.</summary>
    [Fact]
    public void Writes_what_the_lexer_reads_back()
    {
        var text = Write(PdfString.FromText("a(b)c\\d\ne"));
        var token = new PdfLexer(Encoding.Latin1.GetBytes(text)).ReadToken();

        Assert.Equal("(a\\(b\\)c\\\\d\\ne)", text);
        Assert.Equal(PdfTokenType.LiteralString, token.Type);
        Assert.Equal(Encoding.Latin1.GetBytes("a(b)c\\d\ne"), token.Bytes);
    }

    /// <summary>The writer needs something to write to.</summary>
    [Fact]
    public void Refuses_a_missing_destination()
    {
        using var buffer = new MemoryStream();
        var output = new PdfOutputBuffer(buffer);

        Assert.Throws<ArgumentNullException>(() => PdfSerializer.Write(PdfNull.Instance, null!));
        Assert.Throws<ArgumentNullException>(() => PdfSerializer.WriteDictionary(null!, output));
        Assert.Throws<ArgumentNullException>(() => PdfSerializer.WriteDictionary(new PdfDictionary(), null!));
        Assert.Throws<ArgumentNullException>(() => PdfSerializer.WriteStream(null!, output));
    }

    /// <summary>Writes a value the way the writer does.</summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The text that was written.</returns>
    private static string Write(PdfObject? value)
    {
        using var buffer = new MemoryStream();
        var output = new PdfOutputBuffer(buffer);
        PdfSerializer.Write(value, output);
        output.Flush();

        return Encoding.Latin1.GetString(buffer.ToArray());
    }
}
