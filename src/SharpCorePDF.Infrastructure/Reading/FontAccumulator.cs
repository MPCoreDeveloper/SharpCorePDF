using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure.Reading;

/// <summary>
/// Collects the fonts one read of a document finds, keyed by the object each resource points at, so a font
/// selected by many pages becomes one entry carrying all of them.
/// </summary>
/// <remarks>
/// A form XObject paints with resources of its own, so its fonts are visited at depth and the walk stops at
/// <see cref="PdfEngineOptions.MaxNestingDepth"/>; a form reached twice is visited once, because an object
/// number is what a second reference to it names.
/// </remarks>
internal sealed class FontAccumulator
{
    private readonly PdfFileReader document;
    private readonly List<PdfWarning> warnings;
    private readonly List<Entry> entries = [];
    private readonly Dictionary<string, Entry> byKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> warned = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="FontAccumulator"/> class.</summary>
    /// <param name="document">The document being read.</param>
    /// <param name="warnings">The list that receives the remarks the read makes.</param>
    public FontAccumulator(PdfFileReader document, List<PdfWarning> warnings)
    {
        this.document = document;
        this.warnings = warnings;
    }

    /// <summary>Reads the fonts a resource dictionary selects.</summary>
    /// <param name="resources">The resources, or <see langword="null"/> when there are none.</param>
    /// <param name="pageNumber">The one-based number of the page being read.</param>
    /// <param name="depth">The depth of the form XObjects above these resources.</param>
    /// <param name="visited">The form XObjects already visited for this page.</param>
    public void Visit(PdfDictionary? resources, int pageNumber, int depth, HashSet<long> visited)
    {
        if (resources is null || depth > document.Options.MaxNestingDepth)
        {
            return;
        }

        if (document.ResolveDictionary(resources.Get("Font")) is { } fonts)
        {
            foreach (var entry in fonts.Entries)
            {
                Add(entry.Key, entry.Value, pageNumber);
            }
        }

        if (document.ResolveDictionary(resources.Get("XObject")) is { } xobjects)
        {
            foreach (var entry in xobjects.Entries)
            {
                VisitForm(entry.Value, pageNumber, depth, visited);
            }
        }
    }

    /// <summary>Builds the fonts found, each with the pages that select it.</summary>
    /// <returns>The fonts, in the order the pages first selected them.</returns>
    public IReadOnlyList<PdfFontInfo> Build() =>
        [.. entries.Select(entry => entry.Font with { PageNumbers = [.. entry.Pages] })];

    /// <summary>Reads one font resource and records the page that selects it.</summary>
    /// <param name="resourceName">The resource name the page selects the font with.</param>
    /// <param name="value">The value the resource dictionary states.</param>
    /// <param name="pageNumber">The one-based number of the page that selects it.</param>
    private void Add(string resourceName, PdfObject? value, int pageNumber)
    {
        if (document.ResolveDictionary(value) is not { } font)
        {
            WarnOnce(
                $"unreadable:{KeyOf(value, resourceName, string.Empty)}",
                PdfWarningKind.FontUnreadable,
                $"'{document.SourceName}': page {pageNumber} selects the font '{resourceName}', which cannot be read as a font dictionary.");
            return;
        }

        var baseFont = PdfFontDescriptorLookup.BaseFont(document, font, resourceName);
        var key = KeyOf(value, resourceName, baseFont);

        if (!byKey.TryGetValue(key, out var entry))
        {
            entry = Read(resourceName, font, baseFont, key);
            byKey[key] = entry;
            entries.Add(entry);
        }

        entry.Pages.Add(pageNumber);
    }

    /// <summary>Reads everything the document states about one font.</summary>
    /// <param name="resourceName">The resource name the page selects the font with.</param>
    /// <param name="font">The font dictionary.</param>
    /// <param name="baseFont">The base font name.</param>
    /// <param name="key">The identity of the font in this read.</param>
    /// <returns>The entry the pages of the font are collected into.</returns>
    private Entry Read(string resourceName, PdfDictionary font, string baseFont, string key)
    {
        var kind = PdfFontDescriptorLookup.Kind(document, font);
        var descriptor = PdfFontDescriptorLookup.Find(document, font);

        if (descriptor is null && kind != PdfFontKind.Type3)
        {
            WarnOnce(
                $"descriptor:{key}",
                PdfWarningKind.FontDescriptorMissing,
                $"'{document.SourceName}': the font '{baseFont}' states no /FontDescriptor, so neither its program nor its subset can be reported.");
        }

        return new Entry(
            new PdfFontInfo
            {
                ResourceName = resourceName,
                BaseFont = baseFont,
                Kind = kind,
                Program = Program(font, descriptor, kind),
                Encoding = PdfFontDescriptorLookup.Encoding(document, font),
                HasToUnicode = font.Get("ToUnicode") is not null,
                IsSubset = PdfFontDescriptorLookup.IsSubset(baseFont),
            });
    }

    /// <summary>Reads where the program of a font is stored.</summary>
    /// <param name="font">The font dictionary.</param>
    /// <param name="descriptor">The descriptor of the font, or <see langword="null"/> when it states none.</param>
    /// <param name="kind">The kind of font.</param>
    /// <returns>The kind of program the document carries.</returns>
    private static PdfEmbeddedFontProgramKind Program(
        PdfDictionary font,
        PdfDictionary? descriptor,
        PdfFontKind kind)
    {
        if (kind != PdfFontKind.Type3)
        {
            return PdfFontDescriptorLookup.Program(descriptor);
        }

        return font.Get("CharProcs") is null
            ? PdfEmbeddedFontProgramKind.None
            : PdfEmbeddedFontProgramKind.Type3Procedural;
    }

    /// <summary>Visits the resources of a form XObject a page paints with.</summary>
    /// <param name="value">The value of one entry of the page's <c>/XObject</c> dictionary.</param>
    /// <param name="pageNumber">The one-based number of the page being read.</param>
    /// <param name="depth">The depth of the form XObjects above this one.</param>
    /// <param name="visited">The form XObjects already visited for this page.</param>
    private void VisitForm(PdfObject? value, int pageNumber, int depth, HashSet<long> visited)
    {
        // A form is a stream, so a value that is not a reference is not one: its resources are reachable
        // only through the stream the dictionary does not state inline.
        if (value is not PdfReference reference || !visited.Add(reference.ObjectNumber))
        {
            return;
        }

        if (document.Resolve(reference) is not PdfStream stream
            || !string.Equals(stream.Dictionary.GetName("Subtype"), "Form", StringComparison.Ordinal))
        {
            return;
        }

        Visit(document.ResolveDictionary(stream.Dictionary.Get("Resources")), pageNumber, depth + 1, visited);
    }

    /// <summary>Identifies a font by the object its resource points at.</summary>
    /// <param name="value">The value the resource dictionary states.</param>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="baseFont">The base font name.</param>
    /// <returns>The key the pages of one font are collected under.</returns>
    private static string KeyOf(PdfObject? value, string resourceName, string baseFont) =>
        value is PdfReference reference
            ? $"obj:{reference.ObjectNumber}"
            : $"inline:{resourceName}:{baseFont}";

    /// <summary>Adds a remark once, however many pages make the same observation.</summary>
    /// <param name="key">The identity of the observation.</param>
    /// <param name="kind">The kind of the remark.</param>
    /// <param name="message">The text of the remark.</param>
    private void WarnOnce(string key, PdfWarningKind kind, string message)
    {
        if (warned.Add(key))
        {
            warnings.Add(new(kind, message));
        }
    }

    /// <summary>One font and the pages that select it.</summary>
    /// <param name="font">The font as the document states it.</param>
    private sealed class Entry(PdfFontInfo font)
    {
        /// <summary>Gets the font as the document states it.</summary>
        public PdfFontInfo Font { get; } = font;

        /// <summary>Gets the one-based numbers of the pages that select the font.</summary>
        public SortedSet<int> Pages { get; } = [];
    }
}
