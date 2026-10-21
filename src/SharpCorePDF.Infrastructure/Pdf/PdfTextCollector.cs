using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// Walks a page's content and produces the glyphs it draws, in the order it draws them.
/// </summary>
/// <remarks>
/// <para>
/// The walk is the part of text reading that is about the document rather than about typography: it
/// follows the text state (<c>Tf</c>, <c>Tm</c>, <c>Td</c>, <c>TD</c>, <c>T*</c>, <c>Tz</c>, <c>TL</c>,
/// <c>Tc</c>, <c>Tw</c>, <c>Ts</c>), applies the page transform and the current transform matrix, and
/// hands every string to the font that is selected, which is the only thing that can say what a code
/// means or how wide it is.
/// </para>
/// <para>
/// Glyph placement follows the specification's text rendering matrix, which is the text matrix applied
/// first and the page transform second, so a glyph lands where a viewer would draw it even inside a
/// scaled or rotated form.
/// </para>
/// <para>
/// Nothing here invents text. A glyph whose code no font entry maps is skipped, and the font is named
/// in a single <see cref="PdfWarningKind.TextUnreadable"/> warning per page, so a document with an
/// unreadable font yields fewer glyphs and one remark rather than a page of plausible nonsense
/// (NFR-7.8).
/// </para>
/// </remarks>
internal sealed class PdfTextCollector
{
    /// <summary>The depth to which a form inside a form is followed, as a guard against cycles.</summary>
    private const int MaxFormDepth = 8;

    private readonly PdfFileReader reader;
    private readonly int pageNumber;
    private readonly List<PdfWarning> warnings;
    private readonly List<PdfGlyph> glyphs = [];
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private readonly HashSet<long> formsInProgress = [];

    /// <summary>Initializes a new instance of the <see cref="PdfTextCollector"/> class.</summary>
    /// <param name="reader">The document.</param>
    /// <param name="pageNumber">The one-based number of the page being read.</param>
    /// <param name="warnings">The list that receives a remark per font that could not be read.</param>
    public PdfTextCollector(PdfFileReader reader, int pageNumber, List<PdfWarning> warnings)
    {
        this.reader = reader;
        this.pageNumber = pageNumber;
        this.warnings = warnings;
    }

    /// <summary>Collects the glyphs a page's content draws.</summary>
    /// <param name="content">The decoded content of the page, its streams concatenated.</param>
    /// <param name="resources">The resources in scope, which may be <see langword="null"/>.</param>
    /// <returns>The glyphs, in drawing order.</returns>
    public List<PdfGlyph> Collect(byte[] content, PdfDictionary? resources)
    {
        ArgumentNullException.ThrowIfNull(content);

        Walk(content, resources, PdfMatrix.Identity, 0);

        return glyphs;
    }

    /// <summary>Walks one content stream, and every form it paints.</summary>
    /// <param name="content">The decoded bytes of the stream.</param>
    /// <param name="resources">The resources in scope, which the stream's operators name things through.</param>
    /// <param name="ctm">The transform from the stream's space to page space.</param>
    /// <param name="depth">How many forms deep the walk is.</param>
    /// <param name="inherited">
    /// The text state in force where the stream is painted, which a form starts from, or
    /// <see langword="null"/> for a page, which starts from the defaults.
    /// </param>
    private void Walk(
        byte[] content,
        PdfDictionary? resources,
        PdfMatrix ctm,
        int depth,
        TextState? inherited = null)
    {
        var fonts = PdfFontDecoder.Load(reader, resources);
        var saved = new Stack<(PdfMatrix PageTransform, TextState Text)>();
        var state = inherited?.Copy() ?? new TextState();

        state.PageTransform = ctm;

        foreach (var operation in PdfTextScanner.Scan(content))
        {
            switch (operation.Operator)
            {
                case "q":
                    saved.Push((state.PageTransform, state.Copy()));
                    break;

                case "Q" when saved.Count > 0:
                    var (pageTransform, restored) = saved.Pop();

                    state.PageTransform = pageTransform;
                    state.Restore(restored);
                    break;

                case "cm" when operation.Numbers.Count >= 6:
                    state.PageTransform = Combine(ToMatrix(operation.Numbers, 0), state.PageTransform);
                    break;

                case "BT":
                    state.TextMatrix = PdfMatrix.Identity;
                    state.LineMatrix = state.TextMatrix;
                    break;

                case "Tf" when operation.Names.Count > 0 && operation.Numbers.Count > 0:
                    state.FontName = operation.Names[0];
                    state.Font = fonts.GetValueOrDefault(state.FontName);
                    state.FontSize = operation.Numbers[0];
                    break;

                case "Tc" when operation.Numbers.Count > 0:
                    state.CharacterSpacing = operation.Numbers[0];
                    break;

                case "Tw" when operation.Numbers.Count > 0:
                    state.WordSpacing = operation.Numbers[0];
                    break;

                case "Tz" when operation.Numbers.Count > 0:
                    state.HorizontalScale = operation.Numbers[0] / 100d;
                    break;

                case "TL" when operation.Numbers.Count > 0:
                    state.Leading = operation.Numbers[0];
                    break;

                case "Ts" when operation.Numbers.Count > 0:
                    state.Rise = operation.Numbers[0];
                    break;

                case "Td" when operation.Numbers.Count >= 2:
                    state.LineMatrix = Translate(state.LineMatrix, operation.Numbers[0], operation.Numbers[1]);
                    state.TextMatrix = state.LineMatrix;
                    break;

                case "TD" when operation.Numbers.Count >= 2:
                    state.Leading = -operation.Numbers[1];
                    state.LineMatrix = Translate(state.LineMatrix, operation.Numbers[0], operation.Numbers[1]);
                    state.TextMatrix = state.LineMatrix;
                    break;

                case "Tm" when operation.Numbers.Count >= 6:
                    state.LineMatrix = ToMatrix(operation.Numbers, 0);
                    state.TextMatrix = state.LineMatrix;
                    break;

                case "T*":
                    NextLine(state);
                    break;

                case "Tj" when operation.Strings.Count > 0:
                    Show(state, operation.Strings[0]);
                    break;

                case "'" when operation.Strings.Count > 0:
                    NextLine(state);
                    Show(state, operation.Strings[0]);
                    break;

                case "\"" when operation.Strings.Count > 0 && operation.Numbers.Count >= 2:
                    state.WordSpacing = operation.Numbers[0];
                    state.CharacterSpacing = operation.Numbers[1];
                    NextLine(state);
                    Show(state, operation.Strings[0]);
                    break;

                case "TJ" when operation.Items is not null:
                    ShowArray(state, operation.Items);
                    break;

                case "Do" when operation.Names.Count > 0:
                    PaintForm(operation.Names[0], resources, state, depth);
                    break;
            }
        }
    }

    /// <summary>Moves to the start of the next line, as <c>T*</c> and the quote operators do.</summary>
    /// <param name="state">The text state to move.</param>
    private static void NextLine(TextState state)
    {
        state.LineMatrix = Translate(state.LineMatrix, 0d, -state.Leading);
        state.TextMatrix = state.LineMatrix;
    }

    /// <summary>Shows a string, adding a glyph per code it decodes to.</summary>
    /// <param name="state">The text state the string is shown in.</param>
    /// <param name="bytes">The raw bytes of the string, which only the font can decode.</param>
    /// <remarks>
    /// The text matrix advances by the width of every code whether or not the code has text. Skipping
    /// the advance of an unreadable glyph would slide the rest of the line into the space it occupies,
    /// which would place readable text where the document does not draw it.
    /// </remarks>
    private void Show(TextState state, byte[] bytes)
    {
        var font = state.Font;

        if (font is null)
        {
            Report(
                $"font:{state.FontName ?? "none"}",
                state.FontName is null
                    ? $"'{reader.SourceName}': page {pageNumber} shows text before selecting a font, so the text it shows is not reported."
                    : $"'{reader.SourceName}': page {pageNumber} selects the font /{state.FontName}, which its resources do not define, so the text it draws with it is not reported.");

            return;
        }

        if (!font.CanDecode)
        {
            Report($"font:{font.ResourceName}", DescribeUnreadable(font));
        }

        var offset = 0;

        while (offset < bytes.Length)
        {
            var hasText = font.TryDecodeGlyph(bytes, offset, out var text, out var consumed, out var width);

            if (consumed <= 0)
            {
                break;
            }

            if (hasText)
            {
                var (x, y) = ToPage(state.PageTransform, state.TextMatrix, 0d, state.Rise);

                glyphs.Add(new PdfGlyph
                {
                    Text = text,
                    PageNumber = pageNumber,
                    X = x,
                    Y = y,
                    Width = width * state.FontSize * state.HorizontalScale,
                    FontSize = state.FontSize,
                    FontName = font.ResourceName,
                    BaseFont = font.BaseFont,
                });
            }
            else if (font.CanDecode && consumed == font.CodeLength)
            {
                Report($"font:{font.ResourceName}", DescribeUnmapped(font));
            }

            var advance = ((width * state.FontSize) + state.CharacterSpacing)
                + (text == " " ? state.WordSpacing : 0d);

            state.TextMatrix = Advance(state.TextMatrix, advance * state.HorizontalScale);
            offset += consumed;
        }
    }

    /// <summary>Shows an array of strings and spacing adjustments, as <c>TJ</c> does.</summary>
    /// <param name="state">The text state the array is shown in.</param>
    /// <param name="items">The numbers and strings of the array, in the order they were written.</param>
    private void ShowArray(TextState state, List<object> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case double adjustment:
                    // A thousandth of a unit of text space in the negative direction of the writing.
                    state.TextMatrix = Advance(
                        state.TextMatrix,
                        -adjustment / 1000d * state.FontSize * state.HorizontalScale);

                    break;

                case byte[] shown:
                    Show(state, shown);
                    break;
            }
        }
    }

    /// <summary>Walks the content of a form a <c>Do</c> paints, when it is one.</summary>
    /// <param name="name">The name the <c>Do</c> states.</param>
    /// <param name="resources">The resources in scope, which name the form.</param>
    /// <param name="state">The text state in force where the form is painted, which the form starts from.</param>
    /// <param name="depth">How many forms deep the walk is.</param>
    /// <remarks>
    /// <para>
    /// A form paints into the space its own <c>/Matrix</c> describes, so its text is walked with that
    /// matrix applied before the transform in force. A form that names its own resources replaces the
    /// ones in scope; one that names none inherits them, as the specification states. A form painted
    /// while it is already being walked is left alone, which is how a document can repeat a form
    /// without a reader running forever.
    /// </para>
    /// <para>
    /// A form starts from a copy of the graphics state in force where it is painted, and the text state
    /// is part of it, so text the form draws without selecting a font, a size or a spacing itself is
    /// read exactly as the page around it would be. Only the text matrix starts over, which is why the
    /// copy the form takes does not carry one.
    /// </para>
    /// </remarks>
    private void PaintForm(string name, PdfDictionary? resources, TextState state, int depth)
    {
        var value = reader.ResolveDictionary(resources?.Get("XObject"))?.Get(name);

        if (reader.Resolve(value) is not PdfStream stream
            || stream.Dictionary.GetName("Subtype") != "Form"
            || depth >= MaxFormDepth)
        {
            return;
        }

        var reference = value as PdfReference;

        if (reference is not null && !formsInProgress.Add(reference.ObjectNumber))
        {
            return;
        }

        try
        {
            if (!TryDecodeContent(stream, out var formContent))
            {
                warnings.Add(new(
                    PdfWarningKind.ContentStreamUnreadable,
                    $"'{reader.SourceName}': the content stream of a form on page {pageNumber} could not be decoded, so its text is not reported."));

                return;
            }

            var formResources = reader.ResolveDictionary(stream.Dictionary.Get("Resources")) ?? resources;
            var formMatrix = ReadFormMatrix(stream.Dictionary.Get("Matrix"));

            Walk(formContent, formResources, Combine(formMatrix, state.PageTransform), depth + 1, state);
        }
        finally
        {
            if (reference is not null)
            {
                formsInProgress.Remove(reference.ObjectNumber);
            }
        }
    }

    /// <summary>Adds a warning once per subject, however many glyphs the subject fails for.</summary>
    /// <param name="key">The subject, which makes the remark unique.</param>
    /// <param name="message">The remark.</param>
    private void Report(string key, string message)
    {
        if (reported.Add(key))
        {
            warnings.Add(new(PdfWarningKind.TextUnreadable, message));
        }
    }

    /// <summary>Describes a font that cannot be read at all.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The remark.</returns>
    private string DescribeUnreadable(PdfFont font) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"'{reader.SourceName}': page {pageNumber} draws text with /{font.ResourceName} ({font.BaseFont ?? "an unnamed font"}), which {font.FailureReason}; that text is not reported.");

    /// <summary>Describes a font that maps some of its codes to no text.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The remark.</returns>
    private string DescribeUnmapped(PdfFont font) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"'{reader.SourceName}': page {pageNumber} draws text with /{font.ResourceName} ({font.BaseFont ?? "an unnamed font"}), which maps no text to one or more of the codes it is given; those glyphs are not reported.");

    /// <summary>Decodes the content stream of a form.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="content">The decoded bytes when decoding succeeded.</param>
    /// <returns><see langword="true"/> when the stream could be decoded.</returns>
    private static bool TryDecodeContent(PdfStream stream, out byte[] content)
    {
        try
        {
            content = PdfFilters.Decode(stream, "form content stream");
            return true;
        }
        catch (Domain.Exceptions.PdfUnsupportedFeatureException)
        {
            content = [];
            return false;
        }
        catch (Domain.Exceptions.PdfSourceFormatException)
        {
            content = [];
            return false;
        }
    }

    /// <summary>Builds a transform from the six numbers an operator states.</summary>
    /// <param name="numbers">The numbers of the operator.</param>
    /// <param name="offset">The position of the first of the six.</param>
    /// <returns>The transform.</returns>
    private static PdfMatrix ToMatrix(List<double> numbers, int offset) => new(
        numbers[offset],
        numbers[offset + 1],
        numbers[offset + 2],
        numbers[offset + 3],
        numbers[offset + 4],
        numbers[offset + 5]);

    /// <summary>Reads a form's <c>/Matrix</c>.</summary>
    /// <param name="value">The value of the entry.</param>
    /// <returns>The transform, or the identity when the entry is absent or unreadable.</returns>
    private PdfMatrix ReadFormMatrix(PdfObject? value)
    {
        var array = reader.ResolveArray(value);

        if (array is null || array.Count < 6)
        {
            return PdfMatrix.Identity;
        }

        var numbers = new List<double>(6);

        for (var index = 0; index < 6; index++)
        {
            if (ReadNumber(array.Items[index]) is not { } number)
            {
                return PdfMatrix.Identity;
            }

            numbers.Add(number);
        }

        return ToMatrix(numbers, 0);
    }

    /// <summary>Reads a number that may be stored behind a reference.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    private double? ReadNumber(PdfObject? value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        PdfReference reference => ReadNumber(reader.Resolve(reference)),
        _ => null,
    };

    /// <summary>Applies a translation of the space a transform maps from.</summary>
    /// <param name="matrix">The transform.</param>
    /// <param name="x">The distance along the x axis.</param>
    /// <param name="y">The distance along the y axis.</param>
    /// <returns>The translated transform.</returns>
    private static PdfMatrix Translate(PdfMatrix matrix, double x, double y) => new(
        matrix.A,
        matrix.B,
        matrix.C,
        matrix.D,
        (x * matrix.A) + (y * matrix.C) + matrix.E,
        (x * matrix.B) + (y * matrix.D) + matrix.F);

    /// <summary>Moves a text matrix along its own x axis, the direction the text is written in.</summary>
    /// <param name="matrix">The text matrix.</param>
    /// <param name="distance">The distance to move, in text space.</param>
    /// <returns>The moved matrix.</returns>
    private static PdfMatrix Advance(PdfMatrix matrix, double distance) => new(
        matrix.A,
        matrix.B,
        matrix.C,
        matrix.D,
        (distance * matrix.A) + matrix.E,
        (distance * matrix.B) + matrix.F);

    /// <summary>Composes two transforms, the first applied before the second.</summary>
    /// <param name="first">The transform applied first.</param>
    /// <param name="second">The transform applied second.</param>
    /// <returns>The composed transform.</returns>
    /// <remarks>
    /// Written out rather than taken from the operator of <see cref="PdfMatrix"/> so that the order is
    /// stated where it is relied on: <c>cm</c> composes the new matrix before the state, and a form's
    /// <c>/Matrix</c> is applied before the transform in force where the form is painted.
    /// </remarks>
    private static PdfMatrix Combine(PdfMatrix first, PdfMatrix second) => new(
        (first.A * second.A) + (first.B * second.C),
        (first.A * second.B) + (first.B * second.D),
        (first.C * second.A) + (first.D * second.C),
        (first.C * second.B) + (first.D * second.D),
        (first.E * second.A) + (first.F * second.C) + second.E,
        (first.E * second.B) + (first.F * second.D) + second.F);

    /// <summary>Maps a point of text space to page space.</summary>
    /// <param name="pageTransform">The transform from user space to page space.</param>
    /// <param name="textMatrix">The text matrix in force.</param>
    /// <param name="x">The x coordinate in text space.</param>
    /// <param name="y">The y coordinate in text space.</param>
    /// <returns>The point in page space.</returns>
    private static (double X, double Y) ToPage(
        PdfMatrix pageTransform,
        PdfMatrix textMatrix,
        double x,
        double y)
    {
        var userX = (textMatrix.A * x) + (textMatrix.C * y) + textMatrix.E;
        var userY = (textMatrix.B * x) + (textMatrix.D * y) + textMatrix.F;

        return pageTransform.Transform(userX, userY);
    }

    /// <summary>The text state a content stream carries from one operator to the next.</summary>
    private sealed class TextState
    {
        /// <summary>Gets or sets the transform from user space to page space.</summary>
        public PdfMatrix PageTransform { get; set; } = PdfMatrix.Identity;

        /// <summary>Gets or sets the matrix that maps text space to the space the text is drawn in.</summary>
        public PdfMatrix TextMatrix { get; set; } = PdfMatrix.Identity;

        /// <summary>Gets or sets the matrix the next line starts from.</summary>
        public PdfMatrix LineMatrix { get; set; } = PdfMatrix.Identity;

        /// <summary>Gets or sets the name of the selected font, without its leading slash.</summary>
        public string? FontName { get; set; }

        /// <summary>Gets or sets the selected font, or <see langword="null"/> when the resources lack it.</summary>
        public PdfFont? Font { get; set; }

        /// <summary>Gets or sets the size of the em box of the selected font, in text space units.</summary>
        public double FontSize { get; set; }

        /// <summary>Gets or sets the space added after every glyph.</summary>
        public double CharacterSpacing { get; set; }

        /// <summary>Gets or sets the space added after every space glyph.</summary>
        public double WordSpacing { get; set; }

        /// <summary>Gets or sets the horizontal scale, as a fraction rather than a percentage.</summary>
        public double HorizontalScale { get; set; } = 1d;

        /// <summary>Gets or sets the distance the quote operators and <c>T*</c> move a line.</summary>
        public double Leading { get; set; }

        /// <summary>Gets or sets how far the text is raised above its baseline.</summary>
        public double Rise { get; set; }

        /// <summary>Copies the parameters that the graphics state carries.</summary>
        /// <returns>A copy of them, without a text matrix, because one is not part of the graphics state.</returns>
        public TextState Copy() => new()
        {
            PageTransform = PageTransform,
            FontName = FontName,
            Font = Font,
            FontSize = FontSize,
            CharacterSpacing = CharacterSpacing,
            WordSpacing = WordSpacing,
            HorizontalScale = HorizontalScale,
            Leading = Leading,
            Rise = Rise,
        };

        /// <summary>Restores the parameters that the graphics state carries, as <c>Q</c> does.</summary>
        /// <param name="other">The copy taken when the graphics state was pushed.</param>
        public void Restore(TextState other)
        {
            FontName = other.FontName;
            Font = other.Font;
            FontSize = other.FontSize;
            CharacterSpacing = other.CharacterSpacing;
            WordSpacing = other.WordSpacing;
            HorizontalScale = other.HorizontalScale;
            Leading = other.Leading;
            Rise = other.Rise;
        }
    }
}
