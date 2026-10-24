using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpCorePDF.Manual;

/// <summary>One heading of the manual, as the contents and the outline need it.</summary>
/// <param name="Level">The heading level: 1, 2 or 3.</param>
/// <param name="Title">The heading as plain text, without markup.</param>
/// <param name="Anchor">The unique id the heading carries in the document.</param>
internal sealed record Heading(int Level, string Title, string Anchor);

/// <summary>
/// Reads the manual's Markdown and writes the HTML the print stylesheet expects.
/// </summary>
/// <remarks>
/// <para>
/// The reader covers the constructs the chapters actually use and nothing else: ATX headings,
/// paragraphs, fenced code, pipe tables, blockquotes, flat lists, inline code, strong and emphasis,
/// links and thematic breaks. Raw HTML is not passed through and no attribute-list extension is
/// implemented, because no chapter uses either.
/// </para>
/// <para>
/// Fenced code is handled by this reader rather than by a pre-pass: the block is opaque from the
/// opening fence to the closing one, so the highlighter sees the text exactly as it was written.
/// </para>
/// </remarks>
internal static partial class MarkdownConverter
{
    private const int WrapAfterLines = 34;
    private const char PlaceholderOpen = '\uE000';
    private const char PlaceholderClose = '\uE001';

    private static readonly Regex HeadingTag = new(@"<h([123])([^>]*)>", RegexOptions.Compiled);
    private static readonly Regex IdAttribute = new("\\bid=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex HeadingContent = new(
        @"<h([123])(?:\s+id=""([^""]*)"")?[^>]*>(.*?)</h\1>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InlineTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new(
        @"\[([^\]]+)\]\(([^)\s]+)(?:\s+""([^""]*)""\s*)?\)",
        RegexOptions.Compiled);
    private static readonly Regex StrongStar = new(
        @"\*\*(?=\S)(.+?)(?<=\S)\*\*",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex StrongUnderscore = new(
        @"(?<![\w])__(?=\S)(.+?)(?<=\S)__(?![\w])",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex EmphasisStar = new(
        @"(?<!\*)\*(?=\S)([^*]+?)(?<=\S)\*(?!\*)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex EmphasisUnderscore = new(
        @"(?<![\w])_(?=\S)([^_]+?)(?<=\S)_(?![\w])",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Converts one chapter to HTML.</summary>
    /// <param name="markdown">The text of the chapter.</param>
    /// <returns>The HTML the stylesheet and the contents expect.</returns>
    public static string Convert(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var lines = SplitLines(markdown);
        return RenderBlocks(lines, 0, lines.Count, compactFirst: false);
    }

    /// <summary>
    /// Gives every heading an id that is unique across the whole book.
    /// </summary>
    /// <remarks>
    /// Markdown makes ids unique within one file, not across chapters, so two chapters with a
    /// "What is not generated" section would otherwise share one id and the contents would link
    /// both entries to whichever page was measured last.
    /// </remarks>
    /// <param name="html">The HTML of one chapter.</param>
    /// <param name="used">The ids already taken, extended with the ones this chapter uses.</param>
    /// <returns>The HTML with unique heading ids.</returns>
    public static string UniquifyHeadingIds(string html, HashSet<string> used)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(used);

        return HeadingTag.Replace(html, match =>
        {
            var level = match.Groups[1].Value;
            var attributes = match.Groups[2].Value;
            var id = IdAttribute.Match(attributes);
            var baseId = id.Success ? id.Groups[1].Value : "section";
            var candidate = baseId;
            var suffix = 1;

            while (!used.Add(candidate))
            {
                candidate = string.Create(CultureInfo.InvariantCulture, $"{baseId}-{suffix}");
                suffix++;
            }

            attributes = id.Success
                ? string.Concat(
                    attributes.AsSpan(0, id.Groups[1].Index),
                    candidate,
                    attributes.AsSpan(id.Groups[1].Index + id.Groups[1].Length))
                : string.Concat(" id=\"", candidate, "\"", attributes);

            return string.Concat("<h", level, attributes, ">");
        });
    }

    /// <summary>Collects every level 1 to 3 heading of a chapter, in document order.</summary>
    /// <param name="html">The HTML of one chapter.</param>
    /// <returns>The headings, as the contents and the outline need them.</returns>
    public static IReadOnlyList<Heading> CollectHeadings(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var found = new List<Heading>();

        foreach (Match match in HeadingContent.Matches(html))
        {
            var level = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var existing = match.Groups[2].Success ? match.Groups[2].Value : null;
            var plain = WebUtility.HtmlDecode(InlineTag.Replace(match.Groups[3].Value, string.Empty)).Trim();

            if (level == 1 && plain == "Contents")
            {
                continue;
            }

            found.Add(new Heading(
                level,
                plain,
                string.IsNullOrEmpty(existing) ? Slug(plain) : existing));
        }

        return found;
    }

    private static string RenderBlocks(
        List<string> lines,
        int start,
        int end,
        bool compactFirst)
    {
        var html = new StringBuilder();
        var index = start;
        var first = true;

        while (index < end)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            if (TryHeading(lines[index], out var level, out var heading))
            {
                var inner = RenderInline(heading);
                var anchor = Slug(PlainText(inner));

                html.Append("<h").Append(level).Append(" id=\"").Append(EscapeAttribute(anchor)).Append("\">")
                    .Append(inner).Append("</h").Append(level).Append(">\n");
                index++;
            }
            else if (TryFence(lines[index], out var info, out var fenceChar, out var fenceLength))
            {
                html.Append(RenderFence(lines, ref index, end, info, fenceChar, fenceLength));
            }
            else if (TryTable(lines, index, end, out var afterTable, out var table))
            {
                html.Append(table);
                index = afterTable;
            }
            else if (IsBlockQuote(lines[index]))
            {
                html.Append(RenderBlockQuote(lines, ref index, end));
            }
            else if (TryList(lines, index, end, out var afterList, out var list))
            {
                html.Append(list);
                index = afterList;
            }
            else if (IsThematicBreak(lines[index]))
            {
                html.Append("<hr />\n");
                index++;
            }
            else
            {
                var paragraph = new List<string>();
                while (index < end && !StartsBlock(lines, index, end))
                {
                    paragraph.Add(lines[index].TrimEnd());
                    index++;
                }

                if (paragraph.Count == 0)
                {
                    index++;
                    continue;
                }

                var inner = RenderInline(string.Join('\n', paragraph));
                if (first && compactFirst)
                {
                    html.Append(inner).Append('\n');
                }
                else
                {
                    html.Append("<p>").Append(inner).Append("</p>\n");
                }
            }

            first = false;
        }

        return html.ToString();
    }

    private static List<string> SplitLines(string text)
    {
        var normalised = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return [.. normalised.Split('\n')];
    }

    private static string RenderFence(
        List<string> lines,
        ref int index,
        int end,
        string info,
        char fenceChar,
        int fenceLength)
    {
        var start = index + 1;
        var close = start;
        while (close < end && !IsFenceClose(lines[close], fenceChar, fenceLength))
        {
            close++;
        }

        var code = new StringBuilder();
        for (var line = start; line < close; line++)
        {
            code.Append(lines[line]).Append('\n');
        }

        var language = info.Split(' ', '\t')[0].ToLowerInvariant();
        var label = SyntaxHighlighter.Label(language);
        var wrap = close - start > WrapAfterLines ? " wrap" : string.Empty;

        var html = new StringBuilder();
        html.Append("<div class=\"codeblock").Append(wrap)
            .Append("\" data-lang=\"").Append(EscapeAttribute(label)).Append("\">")
            .Append("<span class=\"lang\">").Append(Escape(label)).Append("</span>")
            .Append("<pre><code>").Append(SyntaxHighlighter.Highlight(code.ToString(), language))
            .Append("</code></pre></div>\n");

        index = close < end ? close + 1 : end;
        return html.ToString();
    }

    private static bool TryTable(
        List<string> lines,
        int start,
        int end,
        out int next,
        out string html)
    {
        next = start;
        html = string.Empty;

        if (start + 1 >= end
            || !lines[start].Contains('|')
            || !IsDelimiterRow(lines[start + 1]))
        {
            return false;
        }

        var header = SplitRow(lines[start]);
        var rows = new List<IReadOnlyList<string>>();
        var index = start + 2;

        while (index < end && !string.IsNullOrWhiteSpace(lines[index]) && lines[index].Contains('|'))
        {
            rows.Add(SplitRow(lines[index]));
            index++;
        }

        var table = new StringBuilder();
        table.Append("<table>\n<thead>\n<tr>\n");
        foreach (var cell in header)
        {
            table.Append("<th>").Append(RenderInline(cell)).Append("</th>\n");
        }

        table.Append("</tr>\n</thead>\n");

        if (rows.Count > 0)
        {
            table.Append("<tbody>\n");
            foreach (var row in rows)
            {
                table.Append("<tr>\n");
                for (var column = 0; column < header.Count; column++)
                {
                    var cell = column < row.Count ? row[column] : string.Empty;
                    table.Append("<td>").Append(RenderInline(cell)).Append("</td>\n");
                }

                table.Append("</tr>\n");
            }

            table.Append("</tbody>\n");
        }

        table.Append("</table>\n");

        next = index;
        html = table.ToString();
        return true;
    }

    private static bool IsDelimiterRow(string line)
    {
        if (!line.Contains('-') || !line.Contains('|'))
        {
            return false;
        }

        var cells = SplitRow(line);
        if (cells.Count == 0)
        {
            return false;
        }

        foreach (var cell in cells)
        {
            var dashes = cell.Trim().Trim(':');
            if (dashes.Length == 0)
            {
                return false;
            }

            foreach (var character in dashes)
            {
                if (character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static List<string> SplitRow(string line)
    {
        var text = line.Trim();

        if (text.StartsWith('|'))
        {
            text = text[1..];
        }

        if (text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal))
        {
            text = text[..^1];
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        var inCode = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '\\' && index + 1 < text.Length && text[index + 1] == '|')
            {
                current.Append('|');
                index++;
                continue;
            }

            if (character == '`')
            {
                inCode = !inCode;
                current.Append(character);
                continue;
            }

            if (character == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    private readonly record struct ListMarker(
        bool Ordered,
        int Number,
        int Indent,
        int ContentStart,
        string Content);

    private static bool TryList(
        List<string> lines,
        int start,
        int end,
        out int next,
        out string html)
    {
        next = start;
        html = string.Empty;

        if (FindListMarker(lines[start]) is not { } marker)
        {
            return false;
        }

        var items = new List<List<string>>();
        var tight = true;
        var index = start;

        while (index < end)
        {
            if (FindListMarker(lines[index]) is not { } current
                || current.Indent != marker.Indent
                || current.Ordered != marker.Ordered)
            {
                break;
            }

            var item = new List<string> { current.Content };
            index++;

            while (index < end)
            {
                if (string.IsNullOrWhiteSpace(lines[index]))
                {
                    var lookahead = index;
                    while (lookahead < end && string.IsNullOrWhiteSpace(lines[lookahead]))
                    {
                        lookahead++;
                    }

                    var continues = lookahead < end
                        && (IndentOf(lines[lookahead]) > marker.Indent
                            || FindListMarker(lines[lookahead]) is { } following
                            && following.Indent == marker.Indent
                            && following.Ordered == marker.Ordered);

                    if (!continues)
                    {
                        break;
                    }

                    tight = false;
                    for (var blank = index; blank < lookahead; blank++)
                    {
                        item.Add(string.Empty);
                    }

                    index = lookahead;
                    continue;
                }

                if (FindListMarker(lines[index]) is { } sibling && sibling.Indent <= marker.Indent)
                {
                    break;
                }

                var indent = IndentOf(lines[index]);
                if (indent <= marker.Indent)
                {
                    break;
                }

                item.Add(Dedent(lines[index], Math.Min(indent, current.ContentStart)));
                index++;
            }

            items.Add(item);
        }

        if (items.Count == 0)
        {
            return false;
        }

        var list = new StringBuilder();
        list.Append(marker.Ordered ? "<ol" : "<ul");
        if (marker.Ordered && marker.Number != 1)
        {
            list.Append(" start=\"").Append(marker.Number.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        list.Append(">\n");

        foreach (var item in items)
        {
            var body = RenderBlocks(item, 0, item.Count, compactFirst: tight);
            list.Append("<li>").Append(body.TrimEnd('\n')).Append("</li>\n");
        }

        list.Append(marker.Ordered ? "</ol>\n" : "</ul>\n");

        next = index;
        html = list.ToString();
        return true;
    }

    private static ListMarker? FindListMarker(string line)
    {
        var indent = IndentOf(line);
        if (indent > 3 || indent >= line.Length)
        {
            return null;
        }

        var character = line[indent];

        if (character is '-' or '*' or '+')
        {
            if (indent + 1 >= line.Length || line[indent + 1] is not (' ' or '\t'))
            {
                return null;
            }

            var content = indent + 2;
            while (content < line.Length && line[content] is ' ' or '\t')
            {
                content++;
            }

            return new ListMarker(false, 0, indent, content, line[content..]);
        }

        if (!char.IsAsciiDigit(character))
        {
            return null;
        }

        var digits = indent;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        if (digits >= line.Length
            || line[digits] is not ('.' or ')')
            || digits + 1 >= line.Length
            || line[digits + 1] is not (' ' or '\t'))
        {
            return null;
        }

        if (!int.TryParse(
                line.AsSpan(indent, digits - indent),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return null;
        }

        var start = digits + 2;
        while (start < line.Length && line[start] is ' ' or '\t')
        {
            start++;
        }

        return new ListMarker(true, number, indent, start, line[start..]);
    }

    private static int IndentOf(string line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        return indent;
    }

    private static string Dedent(string line, int count)
    {
        var index = 0;
        while (index < line.Length && index < count && line[index] == ' ')
        {
            index++;
        }

        return line[index..];
    }

    private static string RenderBlockQuote(List<string> lines, ref int index, int end)
    {
        var inner = new List<string>();

        while (index < end && IsBlockQuote(lines[index]))
        {
            var line = lines[index].TrimStart()[1..];
            if (line.StartsWith(' '))
            {
                line = line[1..];
            }

            inner.Add(line);
            index++;
        }

        var body = RenderBlocks(inner, 0, inner.Count, compactFirst: false);
        return "<blockquote>\n" + body.TrimEnd('\n') + "\n</blockquote>\n";
    }

    private static bool StartsBlock(List<string> lines, int index, int end)
    {
        var line = lines[index];

        return string.IsNullOrWhiteSpace(line)
            || TryHeading(line, out _, out _)
            || TryFence(line, out _, out _, out _)
            || IsBlockQuote(line)
            || FindListMarker(line) is not null
            || IsThematicBreak(line)
            || (index + 1 < end && line.Contains('|') && IsDelimiterRow(lines[index + 1]));
    }

    private static bool TryHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        var index = 0;
        while (index < line.Length && line[index] == '#')
        {
            index++;
        }

        if (index is < 1 or > 6)
        {
            return false;
        }

        if (index < line.Length && line[index] is not (' ' or '\t'))
        {
            return false;
        }

        level = index;
        text = line[index..].Trim();

        var end = text.Length;
        while (end > 0 && text[end - 1] == '#')
        {
            end--;
        }

        if (end < text.Length)
        {
            text = text[..end].TrimEnd();
        }

        return true;
    }

    private static bool TryFence(string line, out string info, out char fenceChar, out int fenceLength)
    {
        info = string.Empty;
        fenceChar = '\0';
        fenceLength = 0;

        var trimmed = line.TrimStart();
        if (trimmed.Length < 3)
        {
            return false;
        }

        var character = trimmed[0];
        if (character is not ('`' or '~'))
        {
            return false;
        }

        var run = 0;
        while (run < trimmed.Length && trimmed[run] == character)
        {
            run++;
        }

        if (run < 3)
        {
            return false;
        }

        info = trimmed[run..].Trim();
        fenceChar = character;
        fenceLength = run;
        return true;
    }

    private static bool IsFenceClose(string line, char fenceChar, int fenceLength)
    {
        var trimmed = line.TrimStart();
        var run = 0;
        while (run < trimmed.Length && trimmed[run] == fenceChar)
        {
            run++;
        }

        return run >= fenceLength && trimmed[run..].Trim().Length == 0;
    }

    private static bool IsBlockQuote(string line) => line.TrimStart().StartsWith('>');

    private static bool IsThematicBreak(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        var character = trimmed[0];
        if (character is not ('-' or '*' or '_'))
        {
            return false;
        }

        var count = 0;
        foreach (var value in trimmed)
        {
            if (value == character)
            {
                count++;
            }
            else if (value != ' ')
            {
                return false;
            }
        }

        return count >= 3;
    }

    private static string PlainText(string html) =>
        WebUtility.HtmlDecode(InlineTag.Replace(html, string.Empty));

    private static string Slug(string text)
    {
        var slug = new StringBuilder(text.Length);
        var lastDash = false;

        foreach (var character in text.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                slug.Append(character);
                lastDash = false;
                continue;
            }

            if ((char.IsWhiteSpace(character) || character == '-') && !lastDash && slug.Length > 0)
            {
                slug.Append('-');
                lastDash = true;
            }
        }

        var value = slug.ToString().Trim('-');
        return value.Length == 0 ? "section" : value;
    }

    private static string Escape(string text)
    {
        var html = new StringBuilder(text.Length + 16);

        foreach (var character in text)
        {
            switch (character)
            {
                case '&':
                    html.Append("&amp;");
                    break;
                case '<':
                    html.Append("&lt;");
                    break;
                case '>':
                    html.Append("&gt;");
                    break;
                default:
                    html.Append(character);
                    break;
            }
        }

        return html.ToString();
    }

    private static string EscapeAttribute(string text) =>
        Escape(text).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string RenderInline(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var placeholders = new List<string>();
        var stripped = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '`')
            {
                var run = 1;
                while (index + run < text.Length && text[index + run] == '`')
                {
                    run++;
                }

                var close = FindBacktickRun(text, index + run, run);
                if (close >= 0)
                {
                    var content = text.Substring(index + run, close - (index + run));
                    if (content.Length >= 2 && content[0] == ' ' && content[^1] == ' ')
                    {
                        content = content[1..^1];
                    }

                    placeholders.Add("<code>" + Escape(content) + "</code>");
                    stripped.Append(PlaceholderOpen)
                        .Append((placeholders.Count - 1).ToString(CultureInfo.InvariantCulture))
                        .Append(PlaceholderClose);

                    index = close + run;
                    continue;
                }
            }

            stripped.Append(text[index]);
            index++;
        }

        var html = Escape(stripped.ToString());

        html = LinkPattern.Replace(html, match =>
        {
            var label = match.Groups[1].Value;
            var href = match.Groups[2].Value;
            var title = match.Groups[3].Success
                ? " title=\"" + match.Groups[3].Value + "\""
                : string.Empty;

            return "<a href=\"" + href + "\"" + title + ">" + label + "</a>";
        });

        html = StrongStar.Replace(html, "<strong>$1</strong>");
        html = StrongUnderscore.Replace(html, "<strong>$1</strong>");
        html = EmphasisStar.Replace(html, "<em>$1</em>");
        html = EmphasisUnderscore.Replace(html, "<em>$1</em>");

        for (var placeholder = 0; placeholder < placeholders.Count; placeholder++)
        {
            html = html.Replace(
                string.Concat(
                    PlaceholderOpen.ToString(),
                    placeholder.ToString(CultureInfo.InvariantCulture),
                    PlaceholderClose.ToString()),
                placeholders[placeholder],
                StringComparison.Ordinal);
        }

        return html;
    }

    private static int FindBacktickRun(string text, int start, int run)
    {
        var index = start;

        while (index < text.Length)
        {
            if (text[index] != '`')
            {
                index++;
                continue;
            }

            var length = 1;
            while (index + length < text.Length && text[index + length] == '`')
            {
                length++;
            }

            if (length == run)
            {
                return index;
            }

            index += length;
        }

        return -1;
    }
}
