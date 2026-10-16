using System.Globalization;
using System.Text;

namespace SharpCorePDF.Examples;

/// <summary>
/// Writes the small PDF documents the examples merge.
/// </summary>
/// <remarks>
/// <para>
/// The documents are built byte by byte, including a correct cross-reference table, so the examples need no
/// fixture files and run from any folder. This is not part of the library and not a recommendation: a real
/// caller merges documents it already has. It exists so that running the examples has something to merge,
/// and it doubles as a demonstration of how modest the requirement for a source document is — a header, a
/// catalog, a page tree, pages with content, and a trailer that points at the catalog.
/// </para>
/// <para>
/// Every page of a document is ten points narrower than the one before it, so an example can name a page by
/// printing its size. That is how the page selection example shows the order it produced.
/// </para>
/// <para>
/// Some of the files are awkward on purpose, because that is what a caller meets in the wild: a text file
/// renamed to .pdf, one that is encrypted with the standard security handler and needs its password, and one
/// whose first page declares no size and whose catalog carries named destinations — so that inspection and
/// merging each have something to report.
/// </para>
/// <para>
/// The encrypted sample is the one document that is not built here. Writing it would mean implementing the
/// key derivation of the security handler inside an example, and what the example has to show is that the
/// library opens a document somebody else encrypted. Its bytes are therefore carried as the base64 of the
/// fixture that <c>docs/tools/make-fixtures.py</c> writes with pikepdf: one page, AES-128, security handler
/// revision 4, opened with <see cref="EncryptedPassword"/>.
/// </para>
/// </remarks>
internal static class SampleDocuments
{
    /// <summary>The producer the sample documents declare, so that a round trip through a merge is visible.</summary>
    private const string Producer = "SharpCorePDF.Examples";

    /// <summary>The user password of the encrypted sample, which is the password merging it needs.</summary>
    public const string EncryptedPassword = "user-password";

    /// <summary>The encrypted sample, as the base64 of the fixture that <c>make-fixtures.py</c> writes.</summary>
    private const string EncryptedSampleBase64 =
        "JVBERi0xLjYKJb/3ov4KMSAwIG9iago8PCAvUGFnZXMgMyAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoK"
        + "PDwgL1RpdGxlIDw5ZWU2NWQ1OTk0YWFhMDU1NjdlNWM5ZWUzYjY1YWZkZWI2MWViNjQzZGQ2NWNhYTY1YzQ3NGNmMmZmNGNi"
        + "MTVhMzNmMzkwNTU4YjYwZjI0MzVjMTVmNDgzOTdlYjE2MDE+ID4+CmVuZG9iagozIDAgb2JqCjw8IC9Db3VudCAxIC9LaWRz"
        + "IFsgNCAwIFIgXSAvVHlwZSAvUGFnZXMgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0NvbnRlbnRzIDUgMCBSIC9NZWRpYUJveCBb"
        + "IDAgMCA1OTUuMjc2IDg0MS44OSBdIC9QYXJlbnQgMyAwIFIgL1Jlc291cmNlcyA8PCA+PiAvVHlwZSAvUGFnZSA+PgplbmRv"
        + "YmoKNSAwIG9iago8PCAvTGVuZ3RoIDY0IC9GaWx0ZXIgL0ZsYXRlRGVjb2RlID4+CnN0cmVhbQrh3P0bqd0yiQwSjL0v5VlD"
        + "86J6/mFtu10gYhyUpqSnn3wRGqY1zFkCkqJT4xns/8usZvtUokZZle8SR5rq1B/ICmVuZHN0cmVhbQplbmRvYmoKNiAwIG9i"
        + "ago8PCAvQ0YgPDwgL1N0ZENGIDw8IC9BdXRoRXZlbnQgL0RvY09wZW4gL0NGTSAvQUVTVjIgL0xlbmd0aCAxNiA+PiA+PiAv"
        + "RmlsdGVyIC9TdGFuZGFyZCAvTGVuZ3RoIDEyOCAvTyA8ZmQ0YmUyZjAyYWI2YzMzOTUyYTg2NDBlYzFlZmFkZjRlY2Y3MTM4"
        + "NTgyZDE0MTIzMmY0MDdjYmNiYzhmZDMwMz4gL09FIDw+IC9QIC0xMDI4IC9SIDQgL1N0bUYgL1N0ZENGIC9TdHJGIC9TdGRD"
        + "RiAvVSA8MjdlYzU0ZTZhMGQ3YzE2MDg2ZDdlN2NlZDg3M2U5N2QwMDIxNDQ2OTkwYjllNDExNDA3MWE0ZDkxMDQ5ODRjMT4g"
        + "L1VFIDw+IC9WIDQgPj4KZW5kb2JqCnhyZWYKMCA3CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDAxNSAwMDAwMCBuIAow"
        + "MDAwMDAwMDY0IDAwMDAwIG4gCjAwMDAwMDAxOTEgMDAwMDAgbiAKMDAwMDAwMDI1MCAwMDAwMCBuIAowMDAwMDAwMzYzIDAw"
        + "MDAwIG4gCjAwMDAwMDA0OTggMDAwMDAgbiAKdHJhaWxlciA8PCAvSW5mbyAyIDAgUiAvUm9vdCAxIDAgUiAvU2l6ZSA3IC9J"
        + "RCBbPGFlYjNlMDYyYTJiNzcxM2Q2NTE3OWUyMGIwNzJiZTE5PjxhZWIzZTA2MmEyYjc3MTNkNjUxNzllMjBiMDcyYmUxOT5d"
        + "IC9FbmNyeXB0IDYgMCBSID4+CnN0YXJ0eHJlZgo4MTQKJSVFT0YK";

    /// <summary>Everything that makes one sample document different from another.</summary>
    /// <param name="Title">The title in the document information, and the text drawn on every page.</param>
    /// <param name="Pages">The number of pages.</param>
    /// <param name="Width">The width of the first page in points; each later page is ten points narrower.</param>
    /// <param name="Height">The height of every page in points.</param>
    /// <param name="Author">The author in the document information, when one is wanted.</param>
    /// <param name="CreationDate">The creation date in the document information, when one is wanted.</param>
    /// <param name="OmitFirstPageMediaBox">Whether the first page leaves out its page size.</param>
    /// <param name="NamedDestinations">Whether the catalog carries named destinations.</param>
    private sealed record Sample(
        string Title,
        int Pages,
        int Width,
        int Height,
        string? Author = null,
        string? CreationDate = null,
        bool OmitFirstPageMediaBox = false,
        bool NamedDestinations = false);

    /// <summary>Writes every sample document and returns the three that merge cleanly, in a sensible order.</summary>
    /// <param name="folder">The folder to write to; it is created when it does not exist, and emptied when it does.</param>
    /// <returns>The paths of Summary.pdf, Appendix.pdf and Annex.pdf.</returns>
    public static IReadOnlyList<string> CreateAll(string folder)
    {
        // The folder is emptied first, so that running the examples twice does not leave the files of an
        // earlier version behind for the inspection example to find.
        if (Directory.Exists(folder))
        {
            foreach (var stale in Directory.GetFiles(folder))
            {
                File.Delete(stale);
            }
        }

        Directory.CreateDirectory(folder);

        var summary = Write(
            folder,
            "Summary.pdf",
            new Sample("Quarterly summary", Pages: 2, Width: 595, Height: 842));

        var appendix = Write(
            folder,
            "Appendix.pdf",
            new Sample("Technical appendix", Pages: 3, Width: 842, Height: 595, Author: "Dana", CreationDate: "D:20240115090000Z"));

        var annex = Write(
            folder,
            "Annex.pdf",
            new Sample("Reading guide", Pages: 1, Width: 595, Height: 842));

        // A file that is not a PDF at all, and one that is really encrypted. A caller meets both in the wild,
        // and the first is what IPdfInspector reports without an exception; the second is reported as needing
        // a password, and opens once it is given one.
        File.WriteAllText(
            Path.Combine(folder, "Broken.pdf"),
            "This file is a text report that someone renamed to .pdf.\n");

        File.WriteAllBytes(
            Path.Combine(folder, "Encrypted.pdf"),
            Convert.FromBase64String(EncryptedSampleBase64));

        // The awkward but mergeable one: its first page declares no page size, which the inspection reports,
        // and its catalog carries named destinations, which a merge can only take from the primary source, so
        // merging it as a secondary source is what produces a warning from the merge itself.
        Write(
            folder,
            "NamedDests.pdf",
            new Sample(
                "Appendix with named destinations",
                Pages: 2,
                Width: 595,
                Height: 842,
                OmitFirstPageMediaBox: true,
                NamedDestinations: true));

        return [summary, appendix, annex];
    }

    /// <summary>Builds one document and writes it to a file.</summary>
    /// <param name="folder">The folder to write to.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="sample">What to put in the document.</param>
    /// <returns>The full path of the file.</returns>
    private static string Write(string folder, string fileName, Sample sample)
    {
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, Build(sample));
        return path;
    }

    /// <summary>Builds a document: a header, a catalog, a page tree, one object per page and its content, one font, one trailer.</summary>
    /// <param name="sample">What to put in the document.</param>
    /// <returns>The bytes of the document.</returns>
    private static byte[] Build(Sample sample)
    {
        // Every object number is handed out first, so an object can refer to numbers that do not exist yet.
        var last = 0;
        int Take() => ++last;

        var catalog = Take();
        var pageTree = Take();
        var pageObjects = Enumerable.Range(0, sample.Pages).Select(_ => (Page: Take(), Contents: Take())).ToArray();
        var font = Take();
        var information = Take();

        var bodies = new string[last + 1];

        bodies[catalog] = $"<< /Type /Catalog /Pages {pageTree} 0 R{Destinations(sample, pageObjects[0].Page)} >>";
        bodies[pageTree] =
            $"<< /Type /Pages /Kids [{string.Join(" ", pageObjects.Select(entry => $"{entry.Page} 0 R"))}] /Count {sample.Pages} >>";

        for (var index = 0; index < pageObjects.Length; index++)
        {
            var (page, contents) = pageObjects[index];

            // Every page is ten points narrower than the one before it, so an example can name a page by
            // printing its size.
            var mediaBox = sample.OmitFirstPageMediaBox && index == 0
                ? string.Empty
                : $"/MediaBox [0 0 {sample.Width - (10 * index)} {sample.Height}] ";

            bodies[page] =
                $"<< /Type /Page /Parent {pageTree} 0 R "
                + mediaBox
                + $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {contents} 0 R >>";

            var text = $"{sample.Title} - page {index + 1} of {sample.Pages}";
            var stream = string.Create(
                CultureInfo.InvariantCulture,
                $"BT /F1 24 Tf 72 {sample.Height - 100} Td ({Escape(text)}) Tj ET");

            // /Length counts the bytes between "stream" and "endstream", which is the content itself.
            bodies[contents] = $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream";
        }

        bodies[font] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>";

        var entries = new StringBuilder($"/Title ({Escape(sample.Title)}) /Producer ({Escape(Producer)})");

        if (sample.Author is not null)
        {
            entries.Append(CultureInfo.InvariantCulture, $" /Author ({Escape(sample.Author)})");
        }

        if (sample.CreationDate is not null)
        {
            entries.Append(CultureInfo.InvariantCulture, $" /CreationDate ({sample.CreationDate})");
        }

        bodies[information] = $"<< {entries} >>";

        using var buffer = new MemoryStream();
        var offsets = new long[last + 1];

        Write("%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");

        for (var number = 1; number <= last; number++)
        {
            offsets[number] = buffer.Position;
            Write(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{bodies[number]}\nendobj\n"));
        }

        var startOfTable = buffer.Position;
        var size = last + 1;

        // A classic cross-reference table: ten digits of byte offset, a generation number and a type, one
        // twenty byte line per object, object 0 being the free head of the list.
        Write(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {size}\n"));
        Write("0000000000 65535 f \n");

        for (var number = 1; number <= last; number++)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n \n"));
        }

        var trailer = new StringBuilder($"trailer\n<< /Size {size} /Root {catalog} 0 R /Info {information} 0 R");

        trailer.Append(CultureInfo.InvariantCulture, $" >>\nstartxref\n{startOfTable}\n%%EOF\n");

        Write(trailer.ToString());

        return buffer.ToArray();

        void Write(string text) => buffer.Write(Encoding.Latin1.GetBytes(text));
    }

    /// <summary>Builds the named destination entry of the catalog, when the sample carries one.</summary>
    /// <param name="sample">The document being built.</param>
    /// <param name="firstPage">The object number of the first page.</param>
    /// <returns>The catalog entries to add, or an empty string.</returns>
    private static string Destinations(Sample sample, int firstPage) => sample.NamedDestinations
        ? $" /Dests << /introduction [{firstPage} 0 R /Fit] >>"
        : string.Empty;

    /// <summary>Escapes the characters a PDF string literal may not contain unescaped.</summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
}

