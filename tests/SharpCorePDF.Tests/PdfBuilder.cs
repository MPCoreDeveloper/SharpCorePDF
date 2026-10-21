using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SharpCorePDF.Tests;

/// <summary>
/// Builds a small, valid PDF from object bodies, so the read tests can state exactly what a document
/// contains without shipping a binary fixture for every shape they need.
/// </summary>
/// <remarks>
/// The builder writes a classic cross-reference table with correct offsets, which is what lets the
/// engine read the result with no tolerance at all. Object numbers are handed out in insertion order,
/// so a fixture can reserve a number for the catalog before it knows what the pages will be.
/// </remarks>
internal sealed class PdfBuilder
{
    private readonly List<byte[]?> objects = [];

    /// <summary>Reserves an object number for a body that is written later.</summary>
    /// <returns>The object number.</returns>
    public int Reserve()
    {
        objects.Add(null);
        return objects.Count;
    }

    /// <summary>Adds an object whose body is the given text.</summary>
    /// <param name="body">The body of the object.</param>
    /// <returns>The object number.</returns>
    public int Add(string body) => Add(Encoding.Latin1.GetBytes(body));

    /// <summary>Adds an object whose body is the given bytes.</summary>
    /// <param name="body">The body of the object.</param>
    /// <returns>The object number.</returns>
    public int Add(byte[] body)
    {
        objects.Add(body);
        return objects.Count;
    }

    /// <summary>Adds a stream object.</summary>
    /// <param name="entries">The entries of the stream dictionary, without the <c>/Length</c>.</param>
    /// <param name="data">The bytes of the stream, written as they are.</param>
    /// <returns>The object number.</returns>
    public int AddStream(string entries, byte[] data) => Add(StreamBody(entries, data));

    /// <summary>Builds the body of a stream object.</summary>
    /// <param name="entries">The entries of the stream dictionary, without the <c>/Length</c>.</param>
    /// <param name="data">The bytes of the stream, written as they are.</param>
    /// <returns>The body of the object, without its <c>obj</c> and <c>endobj</c> markers.</returns>
    public static byte[] StreamBody(string entries, byte[] data)
    {
        var header = Encoding.Latin1.GetBytes($"<< {entries} /Length {data.Length} >>\nstream\n");
        var footer = Encoding.Latin1.GetBytes("\nendstream");
        var body = new byte[header.Length + data.Length + footer.Length];

        header.CopyTo(body, 0);
        data.CopyTo(body, header.Length);
        footer.CopyTo(body, header.Length + data.Length);

        return body;
    }

    /// <summary>Sets the body of a reserved object.</summary>
    /// <param name="number">The object number.</param>
    /// <param name="body">The body of the object.</param>
    public void Set(int number, string body) => objects[number - 1] = Encoding.Latin1.GetBytes(body);

    /// <summary>Sets the body of a reserved object to the given bytes.</summary>
    /// <param name="number">The object number.</param>
    /// <param name="body">The body of the object.</param>
    public void Set(int number, byte[] body) => objects[number - 1] = body;

    /// <summary>Writes the document.</summary>
    /// <param name="root">The object number of the catalog.</param>
    /// <returns>The bytes of the document.</returns>
    public byte[] Build(int root)
    {
        using var output = new MemoryStream();
        var offsets = new long[objects.Count];

        Write(output, "%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");

        for (var index = 0; index < objects.Count; index++)
        {
            offsets[index] = output.Position;

            Write(output, string.Create(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n"));
            output.Write(objects[index] ?? Encoding.Latin1.GetBytes("null"));
            Write(output, "\nendobj\n");
        }

        var startXref = output.Position;

        Write(output, string.Create(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));

        foreach (var offset in offsets)
        {
            Write(output, string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
        }

        Write(
            output,
            string.Create(
                CultureInfo.InvariantCulture,
                $"trailer\n<< /Size {objects.Count + 1} /Root {root} 0 R >>\nstartxref\n{startXref}\n%%EOF\n"));

        return output.ToArray();
    }

    private static void Write(Stream destination, string text) =>
        destination.Write(Encoding.Latin1.GetBytes(text));

    /// <summary>Compresses bytes with the zlib wrapper a Flate stream uses.</summary>
    /// <param name="data">The bytes to compress.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var compressor = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }
}
