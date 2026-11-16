using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of which version of an object the reader resolves when a document can answer in more than one
/// way: an object that lives in an object stream and also in the file body, and a hybrid-reference file whose
/// classic table lists as free the objects its cross-reference stream describes.
/// </summary>
/// <remarks>
/// ISO 32000-1 makes the cross-reference data the authority on where an object is. A reader that lets an
/// object stream, or an older section, answer for a number the newest section places elsewhere shows a
/// different document than every conforming viewer does, and the inspection, the text, a signature check and a
/// merge would all describe or copy content nobody sees.
/// </remarks>
public sealed class PdfCrossReferencePrecedenceTests
{
    private static readonly PdfEngineOptions Options = PdfEngineOptions.Default with
    {
        ReconstructCrossReferenceOnFailure = false
    };

    /// <summary>
    /// An object stream that still holds an old copy of a number the table places in the file body does not
    /// answer for it, whichever object is resolved first: resolving a neighbour loads the stream, and loading
    /// it must not leave its stale copy where the next lookup finds it before the table is consulted.
    /// </summary>
    [Fact]
    public void Resolves_the_object_the_table_names_not_the_copy_an_object_stream_still_holds()
    {
        using var document = new RawDocument();

        document.Add(1, "<< /Type /Catalog /Pages 2 0 R >>");
        document.Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        document.Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        document.Add(10, "<< /Version /Current >>");

        // Object stream 6 holds object 10 as it stood in an earlier revision, and object 11, which only it has.
        document.AddObjectStream(6, ("10", "<< /Version /Stale >>"), ("11", "<< /Neighbour true >>"));

        var reader = new PdfFileReader(
            document.BuildWithXrefStream(root: 1,
                compressed: new Dictionary<int, (int Container, int Index)> { [11] = (6, 1) }),
            "stale.pdf",
            Options);

        // 11 is resolved first, which loads stream 6 and everything in it.
        Assert.True(Assert.IsType<PdfDictionary>(reader.Resolve(11)).ContainsKey("Neighbour"));

        var resolved = Assert.IsType<PdfDictionary>(reader.Resolve(10));

        Assert.Equal("Current", resolved.GetName("Version"));
    }

    /// <summary>
    /// A number the table lists as free is not resolved from an object stream that happens to hold it: the
    /// object was deleted, and a deleted object that reappears is one no other reader shows.
    /// </summary>
    [Fact]
    public void Does_not_bring_back_an_object_the_table_lists_as_free()
    {
        using var document = new RawDocument();

        document.Add(1, "<< /Type /Catalog /Pages 2 0 R >>");
        document.Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        document.Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        document.AddObjectStream(6, ("10", "<< /Deleted true >>"), ("11", "<< /Neighbour true >>"));

        var reader = new PdfFileReader(
            document.BuildWithXrefStream(root: 1,
                compressed: new Dictionary<int, (int Container, int Index)> { [11] = (6, 1) }, free: [10]),
            "deleted.pdf",
            Options);

        Assert.NotNull(reader.Resolve(11));
        Assert.Null(reader.Resolve(10));
    }

    /// <summary>
    /// A hybrid-reference file lists the objects it keeps in object streams as free in its classic table, so
    /// a reader that does not know cross-reference streams skips them, and describes them in the stream the
    /// trailer's <c>/XRefStm</c> names. A reader that does know the stream has to let it answer for those
    /// numbers: keeping the table's free entry hides the page tree from the one reader the file was written for.
    /// </summary>
    [Fact]
    public void Reads_an_object_a_hybrid_file_lists_as_free_in_its_table_and_describes_in_its_stream()
    {
        using var document = new RawDocument();

        document.Add(1, "<< /Type /Catalog /Pages 2 0 R >>");
        document.Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        document.AddObjectStream(5, ("3", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>"));

        var reader = new PdfFileReader(
            document.BuildHybrid(root: 1, hidden: new Dictionary<int, (int Container, int Index)> { [3] = (5, 0) }),
            "hybrid.pdf",
            Options);

        Assert.Single(reader.Pages);
        Assert.Equal("Page", Assert.IsType<PdfDictionary>(reader.Resolve(3)).GetName("Type"));
    }

    /// <summary>
    /// What the stream a hybrid file names says about a number is read before what an older revision says,
    /// because the stream belongs to the newer section: the table, then its stream, then <c>/Prev</c>.
    /// </summary>
    [Fact]
    public void Reads_the_stream_of_a_hybrid_section_before_the_revision_it_follows()
    {
        using var older = new RawDocument();

        older.Add(1, "<< /Type /Catalog /Pages 2 0 R >>");
        older.Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        older.Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>");

        using var newer = older.Append();

        newer.AddObjectStream(5, ("3", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] >>"));

        var reader = new PdfFileReader(
            newer.BuildHybrid(root: 1, hidden: new Dictionary<int, (int Container, int Index)> { [3] = (5, 0) }),
            "hybrid-update.pdf",
            Options);

        var page = Assert.Single(reader.Pages);
        var box = Assert.IsType<PdfArray>(page.Dictionary.Get("MediaBox"));

        Assert.Equal(300, Assert.IsType<PdfInteger>(box.Items[2]).Value);
    }

    /// <summary>Builds the bytes of a document object by object, with the cross-reference data written by hand.</summary>
    private sealed class RawDocument : IDisposable
    {
        private readonly MemoryStream output = new();
        private readonly SortedDictionary<int, long> offsets = [];
        private readonly byte[] previous;
        private readonly long previousXref;

        /// <summary>Releases the buffer the document is built in.</summary>
        public void Dispose() => output.Dispose();

        public RawDocument()
        {
            previous = [];
            Write("%PDF-1.7\n");
        }

        private RawDocument(byte[] earlier, long earlierXref)
        {
            previous = earlier;
            previousXref = earlierXref;
            output.Write(earlier);
        }

        /// <summary>Starts a revision appended to this document, which must have been built first.</summary>
        public RawDocument Append()
        {
            var bytes = BuildClassic(root: 1);

            return new RawDocument(bytes, LastStartXref(bytes));
        }

        public void Add(int number, string body)
        {
            offsets[number] = output.Position;
            Write($"{number} 0 obj\n{body}\nendobj\n");
        }

        /// <summary>Adds an uncompressed object stream whose first object is listed first in its header.</summary>
        public void AddObjectStream(int number, params (string Number, string Body)[] objects)
        {
            var header = new StringBuilder();
            var body = new StringBuilder();

            foreach (var (objectNumber, text) in objects)
            {
                header.Append(System.Globalization.CultureInfo.InvariantCulture, $"{objectNumber} {body.Length} ");
                body.Append(text).Append('\n');
            }

            var content = header.ToString() + body;

            Add(number,
                $"<< /Type /ObjStm /N {objects.Length} /First {header.Length} /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        /// <summary>Writes a classic table and trailer for every object added.</summary>
        public byte[] BuildClassic(int root)
        {
            var start = output.Position;
            var size = (offsets.Keys.Max()) + 1;

            Write($"xref\n0 {size}\n0000000000 65535 f \n");

            for (var number = 1; number < size; number++)
            {
                Write(offsets.TryGetValue(number, out var offset)
                    ? $"{offset:D10} 00000 n \n"
                    : "0000000000 00000 f \n");
            }

            Write($"trailer\n<< /Size {size} /Root {root} 0 R >>\nstartxref\n{start}\n%%EOF\n");

            return output.ToArray();
        }

        /// <summary>Writes one cross-reference stream: uncompressed entries for the objects added, compressed ones as given.</summary>
        public byte[] BuildWithXrefStream(
            int root,
            Dictionary<int, (int Container, int Index)> compressed,
            int[]? free = null)
        {
            var streamNumber = Math.Max(offsets.Keys.Max(), compressed.Keys.Max()) + 1;
            var size = streamNumber + 1;
            var start = output.Position;

            offsets[streamNumber] = start;

            var table = new MemoryStream();

            for (var number = 0; number < size; number++)
            {
                if (number == 0 || (free?.Contains(number) ?? false))
                {
                    Entry(table, 0, 0, 65535);
                }
                else if (compressed.TryGetValue(number, out var place))
                {
                    Entry(table, 2, place.Container, place.Index);
                }
                else if (offsets.TryGetValue(number, out var offset))
                {
                    Entry(table, 1, offset, 0);
                }
                else
                {
                    Entry(table, 0, 0, 0);
                }
            }

            Write(
                $"{streamNumber} 0 obj\n<< /Type /XRef /Size {size} /W [1 4 2] /Root {root} 0 R /Length {table.Length} >>\nstream\n");
            output.Write(table.ToArray());
            Write($"\nendstream\nendobj\nstartxref\n{start}\n%%EOF\n");

            return output.ToArray();
        }

        /// <summary>
        /// Writes a hybrid section: a classic table that lists every hidden object as free, and a
        /// cross-reference stream, named by the trailer's <c>/XRefStm</c>, that describes them.
        /// </summary>
        public byte[] BuildHybrid(int root, Dictionary<int, (int Container, int Index)> hidden)
        {
            var streamNumber = Math.Max(offsets.Keys.Max(), hidden.Keys.Max()) + 1;
            var size = streamNumber + 1;
            var streamStart = output.Position;

            offsets[streamNumber] = streamStart;

            var table = new MemoryStream();

            // The stream lists only what the table hides, as a hybrid file does.
            foreach (var (number, place) in hidden.OrderBy(pair => pair.Key))
            {
                Entry(table, 2, place.Container, place.Index);
            }

            var index = string.Join(' ', hidden.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} 1"));

            Write(
                $"{streamNumber} 0 obj\n<< /Type /XRef /Size {size} /W [1 4 2] /Index [{index}] /Length {table.Length} >>\nstream\n");
            output.Write(table.ToArray());
            Write("\nendstream\nendobj\n");

            var start = output.Position;

            // One subsection per number this section states, as an incremental update writes it: a number it
            // does not mention is not freed, it keeps what the revision it follows says.
            Write("xref\n");

            if (previousXref == 0)
            {
                Write("0 1\n0000000000 65535 f \n");
            }

            foreach (var number in offsets.Keys.Union(hidden.Keys).Order())
            {
                Write(hidden.ContainsKey(number)
                    ? $"{number} 1\n0000000000 00000 f \n"
                    : $"{number} 1\n{offsets[number]:D10} 00000 n \n");
            }

            var prev = previousXref > 0 ? $" /Prev {previousXref}" : string.Empty;

            Write(
                $"trailer\n<< /Size {size} /Root {root} 0 R /XRefStm {streamStart}{prev} >>\nstartxref\n{start}\n%%EOF\n");

            return output.ToArray();
        }

        private static long LastStartXref(byte[] bytes)
        {
            var text = Encoding.Latin1.GetString(bytes);
            var at = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;

            return long.Parse(text[at..].Trim().Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Entry(MemoryStream table, int kind, long field, int index)
        {
            table.WriteByte((byte)kind);
            table.WriteByte((byte)(field >> 24));
            table.WriteByte((byte)(field >> 16));
            table.WriteByte((byte)(field >> 8));
            table.WriteByte((byte)field);
            table.WriteByte((byte)(index >> 8));
            table.WriteByte((byte)index);
        }

        private void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
    }
}
