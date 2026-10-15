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
/// renamed to .pdf, one whose trailer declares encryption, and one whose first page declares no size and
/// whose catalog carries named destinations — so that inspection and merging each have something to report.
/// </para>
/// </remarks>
internal static class SampleDocuments
{
    /// <summary>The producer the sample documents declare, so that a round trip through a merge is visible.</summary>
    private const string Producer = "SharpCorePDF.Examples";

    /// <summary>Everything that makes one sample document different from another.</summary>
    /// <param name="Title">The title in the document information, and the text drawn on every page.</param>
    /// <param name="Pages">The number of pages.</param>
    /// <param name="Width">The width of the first page in points; each later page is ten points narrower.</param>
    /// <param name="Height">The height of every page in points.</param>
    /// <param name="Author">The author in the document information, when one is wanted.</param>
    /// <param name="CreationDate">The creation date in the document information, when one is wanted.</param>
    /// <param name="Encrypt">Whether the trailer declares an encryption dictionary.</param>
    /// <param name="OmitFirstPageMediaBox">Whether the first page leaves out its page size.</param>
    /// <param name="NamedDestinations">Whether the catalog carries named destinations.</param>
    private sealed record Sample(
        string Title,
        int Pages,
        int Width,
        int Height,
        string? Author = null,
        string? CreationDate = null,
        bool Encrypt = false,
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

        // A file that is not a PDF at all, and one whose trailer declares encryption. A caller meets both in
        // the wild, and both are what IPdfInspector exists to report without an exception.
        File.WriteAllText(
            Path.Combine(folder, "Broken.pdf"),
            "This file is a text report that someone renamed to .pdf.\n");

        Write(
            folder,
            "Encrypted.pdf",
            new Sample("Encrypted sample", Pages: 1, Width: 595, Height: 842, Encrypt: true));

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
        var security = sample.Encrypt ? Take() : 0;

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

        if (sample.Encrypt)
        {
            // The engine classifies a document as encrypted when its trailer declares /Encrypt, and it never
            // decrypts anything, so a well-formed security handler is not needed for the example: the zeros
            // stand in for the strings that handler would carry.
            bodies[security] =
                "<< /Filter /Standard /V 2 /R 3 /Length 128 "
                + $"/O <{new string('0', 64)}> /U <{new string('0', 64)}> /P -1 >>";
        }

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

        if (sample.Encrypt)
        {
            trailer.Append(CultureInfo.InvariantCulture, $" /Encrypt {security} 0 R");
        }

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

