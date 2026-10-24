using System.Text;
using SharpCorePDF.Domain.Exceptions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Authoring;

/// <summary>
/// Lays a string out into lines that fit a width, so <see cref="PdfPageCanvas.DrawTextLayout"/>
/// can draw it.
/// </summary>
/// <remarks>
/// <para>
/// The layout is greedy: words are added to a line until the next one would not fit, and the line
/// ends there. Nothing about the result depends on the machine, the culture or the order the code
/// runs in — the widths come from <see cref="PdfTypeface.Measure(string, double)"/>, which reads the supplied font
/// program or the standard font's AFM data — so the same text and style produce the same lines every
/// time, which is what makes a byte-identical rebuild possible.
/// </para>
/// <para>
/// The lines are stated in a box whose origin is its top-left corner, with y growing downwards, the
/// coordinates <see cref="PdfPageCanvas"/> draws in. The first baseline sits one ascent below the top
/// of the box and every later one is a line height below the previous, so the box is what the text
/// occupies rather than what it would occupy if the glyphs had no depth.
/// </para>
/// </remarks>
public static class PdfTextLayouter
{
    /// <summary>Lays text out in a width, aligned to the left and refusing what does not fit.</summary>
    /// <param name="text">The text to lay out; it may contain line breaks and any number of paragraphs.</param>
    /// <param name="style">The face, size and text state the text will be drawn with.</param>
    /// <param name="maxWidth">The width of the box, in points.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWidth"/> is not finite or not greater than zero.</exception>
    /// <exception cref="ArgumentException">A word does not fit and the layout may not break words.</exception>
    public static PdfTextLayout Layout(string text, PdfTextStyle style, double maxWidth) =>
        Layout(text, style, new PdfTextLayoutOptions(maxWidth));

    /// <summary>Lays text out in a box described by its options.</summary>
    /// <param name="text">The text to lay out; it may contain line breaks and any number of paragraphs.</param>
    /// <param name="style">The face, size and text state the text will be drawn with.</param>
    /// <param name="options">The width, alignment, line height and overflow policy.</param>
    /// <returns>The layout.</returns>
    /// <remarks>
    /// A line break in the text starts a new line and is never wrapped away; an empty paragraph is a
    /// line of its own, so a blank line in the input stays a blank line in the output. Runs of spaces
    /// are collapsed to the single space between two words, because a line is built out of words.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A word does not fit and the layout may not break words.</exception>
    /// <exception cref="PdfUnsupportedFeatureException">The options ask for right-to-left text.</exception>
    public static PdfTextLayout Layout(string text, PdfTextStyle style, PdfTextLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Direction == PdfTextDirection.RightToLeft)
        {
            throw new PdfUnsupportedFeatureException(
                "right-to-left text layout",
                "the engine lays glyphs out in the order the string states them and does not reorder or shape them, so a right-to-left paragraph would be drawn backwards rather than refused.");
        }

        var metrics = style.Typeface.Metrics;
        var lineHeight = options.LineHeight
            ?? (style.Leading > 0d ? style.Leading : metrics.LineHeight(style.FontSize));
        var baseline = metrics.Ascent * style.FontSize / metrics.UnitsPerEm;
        var lines = new List<PdfTextLine>();

        foreach (var paragraph in Paragraphs(text))
        {
            var wrapped = Wrap(paragraph, style, options);

            for (var index = 0; index < wrapped.Count; index++)
            {
                var last = index == wrapped.Count - 1;
                lines.Add(BuildLine(wrapped[index], style, options, baseline, last));
                baseline += lineHeight;
            }
        }

        var descent = metrics.Descent * style.FontSize / metrics.UnitsPerEm;
        var height = lines.Count == 0 ? 0d : lines[^1].Baseline - descent;

        return new PdfTextLayout(style, lines, new PdfRectangle(0d, 0d, options.MaxWidth, height), lineHeight);
    }

    /// <summary>Splits text into paragraphs at its line breaks, keeping empty paragraphs.</summary>
    /// <param name="text">The text to split.</param>
    /// <returns>The paragraphs, in reading order; at least one, which may be empty.</returns>
    private static string[] Paragraphs(string text)
    {
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        return paragraphs.Length == 0 ? [string.Empty] : paragraphs;
    }

    /// <summary>Wraps one paragraph into lines of words that fit the maximum width.</summary>
    /// <param name="paragraph">The paragraph, which contains no line break.</param>
    /// <param name="style">The style the text will be drawn with.</param>
    /// <param name="options">The width and the policy for text that does not fit.</param>
    /// <returns>The lines, each as the words it is made of; never empty.</returns>
    private static List<string[]> Wrap(string paragraph, PdfTextStyle style, PdfTextLayoutOptions options)
    {
        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string[]>();

        if (words.Length == 0)
        {
            lines.Add([]);

            return lines;
        }

        var space = Advance(" ", style);
        var current = new List<string>();
        var used = 0d;

        foreach (var word in words)
        {
            var width = Advance(word, style);

            if (width > options.MaxWidth)
            {
                if (current.Count > 0)
                {
                    lines.Add([.. current]);
                    current.Clear();
                    used = 0d;
                }

                var pieces = Piece(word, width, style, options);
                for (var index = 0; index < pieces.Count - 1; index++)
                {
                    lines.Add([pieces[index]]);
                }

                if (pieces.Count > 0)
                {
                    current.Add(pieces[^1]);
                    used = Advance(pieces[^1], style);
                }

                continue;
            }

            var candidate = current.Count == 0 ? width : used + space + width;

            if (current.Count > 0 && candidate > options.MaxWidth)
            {
                lines.Add([.. current]);
                current.Clear();
                current.Add(word);
                used = width;
            }
            else
            {
                current.Add(word);
                used = candidate;
            }
        }

        if (current.Count > 0)
        {
            lines.Add([.. current]);
        }

        return lines.Count == 0 ? [[]] : lines;
    }

    /// <summary>Decides what one word wider than the line becomes.</summary>
    /// <param name="word">The word, which is known not to fit.</param>
    /// <param name="width">The width of the word, in points.</param>
    /// <param name="style">The style the text will be drawn with.</param>
    /// <param name="options">The width and the policy for text that does not fit.</param>
    /// <returns>The pieces of the word, in order; empty when the policy drops it.</returns>
    /// <exception cref="ArgumentException">The policy refuses to place the word.</exception>
    private static List<string> Piece(
        string word,
        double width,
        PdfTextStyle style,
        PdfTextLayoutOptions options)
    {
        if (!options.BreakWords)
        {
            return options.Overflow switch
            {
                PdfTextOverflow.Refuse => throw new ArgumentException(
                    $"The word '{word}' is {width:0.###} points wide, wider than the {options.MaxWidth:0.###} points a line may occupy, and the options do not allow breaking words.",
                    nameof(options)),
                PdfTextOverflow.Clip => [Fit(word, style, options.MaxWidth)],
                _ => [word],
            };
        }

        var pieces = new List<string>();
        var buffer = new StringBuilder();
        var used = 0d;

        foreach (var rune in word.EnumerateRunes())
        {
            var character = rune.ToString();
            var characterWidth = Advance(character, style);

            if (characterWidth > options.MaxWidth)
            {
                if (options.Overflow == PdfTextOverflow.Refuse)
                {
                    throw new ArgumentException(
                        $"The character '{character}' of the word '{word}' is {characterWidth:0.###} points wide, wider than the {options.MaxWidth:0.###} points a line may occupy.",
                        nameof(options));
                }

                if (buffer.Length > 0)
                {
                    pieces.Add(buffer.ToString());
                    buffer.Clear();
                    used = 0d;
                }

                if (options.Overflow == PdfTextOverflow.Continue)
                {
                    pieces.Add(character);
                }

                continue;
            }

            if (buffer.Length > 0 && used + characterWidth > options.MaxWidth)
            {
                pieces.Add(buffer.ToString());
                buffer.Clear();
                used = 0d;
            }

            buffer.Append(character);
            used += characterWidth;
        }

        if (buffer.Length > 0)
        {
            pieces.Add(buffer.ToString());
        }

        return pieces;
    }

    /// <summary>Takes the leading part of a word that fits the width.</summary>
    /// <param name="word">The word to cut.</param>
    /// <param name="style">The style the text will be drawn with.</param>
    /// <param name="maxWidth">The width the part may occupy, in points.</param>
    /// <returns>The longest leading part of the word that fits, which may be empty.</returns>
    private static string Fit(string word, PdfTextStyle style, double maxWidth)
    {
        var buffer = new StringBuilder();
        var used = 0d;

        foreach (var rune in word.EnumerateRunes())
        {
            var character = rune.ToString();
            var width = Advance(character, style);

            if (used + width > maxWidth)
            {
                break;
            }

            buffer.Append(character);
            used += width;
        }

        return buffer.ToString();
    }

    /// <summary>Places the words of one line at the position and alignment they belong at.</summary>
    /// <param name="words">The words of the line; empty for a blank line.</param>
    /// <param name="style">The style the text will be drawn with.</param>
    /// <param name="options">The width, alignment and line height the line is laid out in.</param>
    /// <param name="baseline">The y coordinate of the line's baseline.</param>
    /// <param name="last">Whether this is the last line of its paragraph.</param>
    /// <returns>The line.</returns>
    private static PdfTextLine BuildLine(
        string[] words,
        PdfTextStyle style,
        PdfTextLayoutOptions options,
        double baseline,
        bool last)
    {
        var text = string.Join(' ', words);
        var width = Advance(text, style);
        var stretch = options.Alignment == PdfTextAlignment.Justify && !last && words.Length > 1
            ? (options.MaxWidth - width) / (words.Length - 1)
            : 0d;

        if (stretch > 0d)
        {
            var runs = new List<PdfTextRun>(words.Length);
            var cursor = 0d;

            for (var index = 0; index < words.Length; index++)
            {
                var piece = index < words.Length - 1 ? words[index] + " " : words[index];
                var pieceWidth = Advance(piece, style);
                runs.Add(new PdfTextRun(piece, new PdfPoint(cursor, baseline), pieceWidth));
                cursor += pieceWidth + (index < words.Length - 1 ? stretch : 0d);
            }

            return new PdfTextLine(text, new PdfPoint(0d, baseline), options.MaxWidth, baseline, runs);
        }

        var start = options.Alignment switch
        {
            PdfTextAlignment.Center => (options.MaxWidth - width) / 2d,
            PdfTextAlignment.Right => options.MaxWidth - width,
            _ => 0d,
        };

        return new PdfTextLine(text, new PdfPoint(start, baseline), width, baseline);
    }

    /// <summary>Measures a run of text as the writer will draw it.</summary>
    /// <param name="text">The text to measure, which contains no line break.</param>
    /// <param name="style">The style the text will be drawn with.</param>
    /// <returns>The advance width in points, with the spacing and the scale of the style applied.</returns>
    private static double Advance(string text, PdfTextStyle style) =>
        text.Length == 0
            ? 0d
            : style.Typeface.Measure(text, style.FontSize, style.CharacterSpacing, style.WordSpacing)
                * style.HorizontalScale / 100d;
}
