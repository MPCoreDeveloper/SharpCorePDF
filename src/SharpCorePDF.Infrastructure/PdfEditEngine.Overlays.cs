using System.Globalization;
using System.Text;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Authoring;
using SharpCorePDF.Infrastructure.Pdf;
using DomainMatrix = SharpCorePDF.Domain.ValueObjects.PdfMatrix;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The layer half of <see cref="PdfEditEngine"/>: a page of another document recorded under or over a
/// page of the edited one.
/// </summary>
/// <remarks>
/// <para>
/// The layer is a form XObject, not a second content stream: its operators keep the resources of the
/// document it came from, so a name it uses for a font or an image can never collide with a name the
/// page it is applied to already means something else by. The page draws the form from a small stream
/// of its own, and the streams the page carried are copied unchanged around that stream.
/// </para>
/// <para>
/// The form is written compressed because its operators are concatenated here; the bytes of the page
/// being edited are never touched, which is the guarantee the contract states.
/// </para>
/// </remarks>
public sealed partial class PdfEditEngine
{
    /// <summary>Writes the form XObject and the drawing stream of every layer of one page.</summary>
    /// <param name="sources">The documents of the edit.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="page">The page the layers are applied to.</param>
    private static void WriteOverlayLayers(EditSources sources, PdfWriter writer, EditedPage page)
    {
        if (page.Overlays.Count == 0)
        {
            return;
        }

        var declared = DeclaredResources(page.Reader, page.Source);
        var xObjects = page.Reader.ResolveDictionary(declared?.Get("XObject")) ?? new PdfDictionary();
        var states = page.Reader.ResolveDictionary(declared?.Get("ExtGState")) ?? new PdfDictionary();
        var area = page.CropBox ?? page.MediaBox;
        var nextXObject = 0;
        var nextState = 0;

        foreach (var layer in page.Overlays)
        {
            // A duplicated page shares the layer of the page it was copied from, so the form and the
            // stream that draws it are written once and referenced twice.
            if (layer.FormNumber != 0)
            {
                continue;
            }

            var copier = sources.CopierOf(layer.Reader);
            var box = ReadOverlayBox(layer.Reader, layer.Page);

            // The names are chosen against the resources the page already has, so an entry the page
            // wrote under the same name keeps its meaning.
            layer.FormName = FreeName(xObjects, "SCOverlay", ref nextXObject);
            layer.StateName = FreeName(states, "SCOverlayState", ref nextState);
            layer.State = StateDictionary(layer.Options);

            var form = new PdfDictionary()
                .SetName("Type", "XObject")
                .SetName("Subtype", "Form")
                .SetInteger("FormType", 1)
                .Set("BBox", ToArray(box))
                .SetName("Filter", "FlateDecode")
                .Set("Resources", LayerResources(copier, layer.Reader, layer.Page));

            layer.FormNumber = writer.AllocateObject();
            writer.WriteObject(
                layer.FormNumber,
                new PdfStream(form, EmbeddedImage.Compress(ReadOverlayContent(layer.Reader, layer.Page))));

            var matrix = OverlayMatrix(box, area, layer.Options, layer.Rotation);

            layer.StreamNumber = writer.AllocateObject();
            writer.WriteObject(
                layer.StreamNumber,
                new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(DrawingOperators(layer, matrix, area))));
        }
    }

    /// <summary>Builds the content array of a page that draws layers.</summary>
    /// <param name="copier">The copier that writes the objects of the page's own document.</param>
    /// <param name="writer">The writer of the result.</param>
    /// <param name="page">The page whose content is assembled.</param>
    /// <returns>The content array: underlay first, then the page, then the overlay.</returns>
    private static PdfArray LayeredContents(PdfObjectCopier copier, PdfWriter writer, EditedPage page)
    {
        var contents = new PdfArray();

        foreach (var layer in page.Overlays)
        {
            if (layer.Layer == PdfOverlayLayer.Under)
            {
                contents.Add(new PdfReference(layer.StreamNumber, 0));
            }
        }

        if (page.HasContentFrame)
        {
            contents.Add(new PdfReference(page.PrefixNumber, 0));
        }

        AddContentItems(page.Reader, copier, writer, page.Source.Dictionary.Get("Contents"), contents);

        if (page.HasContentFrame)
        {
            contents.Add(new PdfReference(page.SuffixNumber, 0));
        }

        foreach (var layer in page.Overlays)
        {
            if (layer.Layer == PdfOverlayLayer.Over)
            {
                contents.Add(new PdfReference(layer.StreamNumber, 0));
            }
        }

        // A flattened field is drawn last: what the reader saw in the widget is what the page shows
        // once the field is gone, so nothing the plan added may cover it.
        if (page.FlattenStreamNumber != 0)
        {
            contents.Add(new PdfReference(page.FlattenStreamNumber, 0));
        }

        return contents;
    }

    /// <summary>Builds the resources of a page that draws layers.</summary>
    /// <param name="copier">The copier that writes the objects of the page's own document.</param>
    /// <param name="page">The page whose resources are assembled.</param>
    /// <returns>The resource dictionary, with the layer named beside everything the page had.</returns>
    private static PdfDictionary LayeredResources(PdfObjectCopier copier, EditedPage page)
    {
        var reader = page.Reader;
        var declared = DeclaredResources(reader, page.Source);
        var resources = new PdfDictionary();
        var xObjects = new PdfDictionary();
        var states = new PdfDictionary();

        if (declared is not null)
        {
            foreach (var entry in declared.Entries.Where(item => item.Key is not ("XObject" or "ExtGState")))
            {
                resources.Set(entry.Key, copier.Clone(entry.Value));
            }

            CopyCategory(copier, reader, declared, "XObject", xObjects);
            CopyCategory(copier, reader, declared, "ExtGState", states);
        }

        foreach (var layer in page.Overlays)
        {
            xObjects.Set(layer.FormName, new PdfReference(layer.FormNumber, 0));
            states.Set(layer.StateName, layer.State);
        }

        foreach (var (name, number) in page.FlattenAppearances)
        {
            xObjects.Set(name, new PdfReference(number, 0));
        }

        resources.Set("XObject", xObjects);
        resources.Set("ExtGState", states);
        return resources;
    }

    /// <summary>Copies one category of a resource dictionary into a dictionary of its own.</summary>
    /// <param name="copier">The copier that writes the source objects into the result.</param>
    /// <param name="reader">The document the resources come from.</param>
    /// <param name="declared">The resource dictionary of the page.</param>
    /// <param name="key">The category to copy, <c>XObject</c> or <c>ExtGState</c>.</param>
    /// <param name="destination">The dictionary that receives the entries.</param>
    private static void CopyCategory(
        PdfObjectCopier copier,
        PdfFileReader reader,
        PdfDictionary declared,
        string key,
        PdfDictionary destination)
    {
        if (reader.ResolveDictionary(declared.Get(key)) is not { } category)
        {
            return;
        }

        foreach (var entry in category.Entries)
        {
            destination.Set(entry.Key, copier.Clone(entry.Value));
        }
    }

    /// <summary>Reads the resource dictionary a page uses, its own or an inherited one.</summary>
    /// <param name="reader">The document the page comes from.</param>
    /// <param name="page">The page.</param>
    /// <returns>The resource dictionary, or <see langword="null"/> when the page declares none.</returns>
    private static PdfDictionary? DeclaredResources(PdfFileReader reader, SourcePage page) =>
        reader.ResolveDictionary(page.Dictionary.Get("Resources"))
            ?? reader.ResolveDictionary(page.Inherited.Get("Resources"));

    /// <summary>Copies the resources of the page a layer comes from, so the form can draw with them.</summary>
    /// <param name="copier">The copier that writes the objects of that document.</param>
    /// <param name="reader">The document the layer comes from.</param>
    /// <param name="page">The page of that document.</param>
    /// <returns>The resource dictionary of the form.</returns>
    private static PdfObject LayerResources(PdfObjectCopier copier, PdfFileReader reader, SourcePage page) =>
        DeclaredResources(reader, page) is { } declared ? copier.Clone(declared) : new PdfDictionary();

    /// <summary>Reads the box a layer page states, or A4 when it states none that can be read.</summary>
    /// <param name="reader">The document the layer comes from.</param>
    /// <param name="page">The page of that document.</param>
    /// <returns>The visible box of the layer page.</returns>
    private static PdfRectangle ReadOverlayBox(PdfFileReader reader, SourcePage page) =>
        ReadBox(reader, page, "CropBox", out _)
            ?? ReadBox(reader, page, "MediaBox", out _)
            ?? new PdfRectangle(0d, 0d, PdfPageSize.A4.WidthPoints, PdfPageSize.A4.HeightPoints);

    /// <summary>Decodes the content streams of a page and concatenates them for the form.</summary>
    /// <param name="reader">The document the layer comes from.</param>
    /// <param name="page">The page of that document.</param>
    /// <returns>The operators of the page, in the order they are drawn.</returns>
    private static byte[] ReadOverlayContent(PdfFileReader reader, SourcePage page)
    {
        using var content = new MemoryStream();
        AppendOverlayContent(reader, page.Dictionary.Get("Contents"), content);
        return content.ToArray();
    }

    /// <summary>Appends one content value, descending into the array a page states.</summary>
    /// <param name="reader">The document the layer comes from.</param>
    /// <param name="value">The value to read, which may be a stream, a reference or an array.</param>
    /// <param name="content">The buffer that receives the decoded operators.</param>
    private static void AppendOverlayContent(PdfFileReader reader, PdfObject? value, MemoryStream content)
    {
        var resolved = reader.Resolve(value);

        if (resolved is PdfArray array)
        {
            foreach (var item in array.Items)
            {
                AppendOverlayContent(reader, item, content);
            }

            return;
        }

        if (resolved is not PdfStream stream)
        {
            return;
        }

        content.Write(PdfFilters.Decode(stream, reader.SourceName));
        content.WriteByte((byte)'\n');
    }

    /// <summary>Builds the graphics state that applies the opacity and the blend mode to the layer alone.</summary>
    /// <param name="options">The options of the layer.</param>
    /// <returns>The <c>/ExtGState</c> dictionary.</returns>
    private static PdfDictionary StateDictionary(PdfOverlayOptions options) =>
        new PdfDictionary()
            .Set("ca", new PdfReal(options.Opacity))
            .Set("CA", new PdfReal(options.Opacity))
            .SetName("BM", BlendModeName(options.BlendMode));

    /// <summary>Writes the operators that clip, place and draw the form of one layer.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="matrix">The transform that places the layer page on the page it is applied to.</param>
    /// <param name="area">The visible area of the page, in its own coordinates.</param>
    /// <returns>The operators of the drawing stream.</returns>
    private static string DrawingOperators(OverlayLayer layer, DomainMatrix matrix, PdfRectangle area)
    {
        var stream = new StringBuilder("q\n");

        if (layer.Options.Clip)
        {
            // Without the clip the form would paint outside the page, which viewers cut away with
            // rules of their own; stating it makes the result the same in every one of them.
            stream
                .Append(PdfNumberFormat.Format(area.Left)).Append(' ')
                .Append(PdfNumberFormat.Format(area.Bottom)).Append(' ')
                .Append(PdfNumberFormat.Format(area.Width)).Append(' ')
                .Append(PdfNumberFormat.Format(area.Height)).Append(" re W n\n");
        }

        stream
            .Append('/').Append(layer.StateName).Append(" gs\n")
            .Append(PdfNumberFormat.Format(matrix.A)).Append(' ')
            .Append(PdfNumberFormat.Format(matrix.B)).Append(' ')
            .Append(PdfNumberFormat.Format(matrix.C)).Append(' ')
            .Append(PdfNumberFormat.Format(matrix.D)).Append(' ')
            .Append(PdfNumberFormat.Format(matrix.E)).Append(' ')
            .Append(PdfNumberFormat.Format(matrix.F)).Append(" cm\n")
            .Append('/').Append(layer.FormName).Append(" Do\n")
            .Append("Q\n");

        return stream.ToString();
    }

    /// <summary>Builds the transform that places a layer page on the page it is applied to.</summary>
    /// <param name="box">The visible box of the layer page.</param>
    /// <param name="area">The visible area of the page that draws it.</param>
    /// <param name="options">How the layer is placed.</param>
    /// <param name="rotation">The rotation the layer page declares.</param>
    /// <returns>The transform, from the coordinates of the layer page to those of the page it lands on.</returns>
    private static DomainMatrix OverlayMatrix(
        PdfRectangle box,
        PdfRectangle area,
        PdfOverlayOptions options,
        PageRotation rotation)
    {
        var width = box.Width;
        var height = box.Height;

        if (width <= 0d || height <= 0d || area.Width <= 0d || area.Height <= 0d)
        {
            // A page or an area with no size leaves nothing to fit into; the layer is placed at its
            // own origin rather than at a scale the geometry cannot justify.
            return DomainMatrix.Translate(-box.Left, -box.Bottom);
        }

        var turned = rotation.Degrees is 90 or 270;
        var layerWidth = turned ? height : width;
        var layerHeight = turned ? width : height;

        double scaleX;
        double scaleY;
        double offsetX;
        double offsetY;

        switch (options.Placement)
        {
            case PdfOverlayPlacement.Stretch:
                scaleX = area.Width / layerWidth;
                scaleY = area.Height / layerHeight;
                offsetX = area.Left;
                offsetY = area.Bottom;
                break;

            case PdfOverlayPlacement.OriginalSize:
                scaleX = 1d;
                scaleY = 1d;
                offsetX = area.Left;
                offsetY = area.Bottom;
                break;

            default:
                var fit = Math.Min(area.Width / layerWidth, area.Height / layerHeight);
                scaleX = fit;
                scaleY = fit;
                offsetX = area.Left + ((area.Width - (layerWidth * fit)) / 2d);
                offsetY = area.Bottom + ((area.Height - (layerHeight * fit)) / 2d);
                break;
        }

        var placement = DomainMatrix
            .Scale(scaleX, scaleY)
            .Multiply(DomainMatrix.Translate(offsetX, offsetY));

        return DomainMatrix
            .Translate(-box.Left, -box.Bottom)
            .Multiply(Turn(rotation, width, height))
            .Multiply(placement);
    }

    /// <summary>Maps the content of a page into the coordinates a viewer shows it in.</summary>
    /// <param name="rotation">The rotation the page declares.</param>
    /// <param name="width">The width of the page box.</param>
    /// <param name="height">The height of the page box.</param>
    /// <returns>The rotation expressed as a transform.</returns>
    private static DomainMatrix Turn(PageRotation rotation, double width, double height) => rotation.Degrees switch
    {
        // A quarter turn clockwise maps the top edge to the right edge, so the corners of the box land
        // in a box of the transposed size; the translation is what brings them back to zero.
        90 => new DomainMatrix(0d, -1d, 1d, 0d, 0d, width),
        180 => new DomainMatrix(-1d, 0d, 0d, -1d, width, height),
        270 => new DomainMatrix(0d, 1d, -1d, 0d, height, 0d),
        _ => DomainMatrix.Identity,
    };

    /// <summary>Finds a resource name that the page does not use yet.</summary>
    /// <param name="declared">The resource category the name has to be unique in.</param>
    /// <param name="prefix">The prefix of the name, which says what the entry is.</param>
    /// <param name="next">The counter of the names handed out so far.</param>
    /// <returns>The free name.</returns>
    private static string FreeName(PdfDictionary declared, string prefix, ref int next)
    {
        string name;

        do
        {
            name = string.Create(CultureInfo.InvariantCulture, $"{prefix}{++next}");
        }
        while (declared.ContainsKey(name));

        return name;
    }

    /// <summary>Gets the name of the blend mode as PDF writes it.</summary>
    /// <param name="blendMode">The blend mode of the layer.</param>
    /// <returns>The PDF name.</returns>
    private static string BlendModeName(PdfOverlayBlendMode blendMode) => blendMode switch
    {
        PdfOverlayBlendMode.Normal => "Normal",
        PdfOverlayBlendMode.Multiply => "Multiply",
        PdfOverlayBlendMode.Screen => "Screen",
        PdfOverlayBlendMode.Overlay => "Overlay",
        PdfOverlayBlendMode.Darken => "Darken",
        PdfOverlayBlendMode.Lighten => "Lighten",
        _ => throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, "The blend mode is not one the engine writes."),
    };

    /// <summary>
    /// One overlay or underlay of a page while that page is written: where the layer comes from, how it
    /// is placed, and the objects that were written for it.
    /// </summary>
    /// <param name="reader">The document the layer comes from.</param>
    /// <param name="page">The page of that document.</param>
    /// <param name="options">How the layer is placed and composited.</param>
    /// <param name="layer">Whether the layer is drawn under or over the page.</param>
    private sealed class OverlayLayer(
        PdfFileReader reader,
        SourcePage page,
        PdfOverlayOptions options,
        PdfOverlayLayer layer)
    {
        /// <summary>Gets the document the layer comes from.</summary>
        public PdfFileReader Reader { get; } = reader;

        /// <summary>Gets the page of that document that is recorded.</summary>
        public SourcePage Page { get; } = page;

        /// <summary>Gets how the layer is placed and composited.</summary>
        public PdfOverlayOptions Options { get; } = options;

        /// <summary>Gets a value indicating whether the layer is drawn under or over the page.</summary>
        public PdfOverlayLayer Layer { get; } = layer;

        /// <summary>Gets the rotation the layer page declares, which the form is placed with.</summary>
        public PageRotation Rotation { get; } = ReadRotation(page);

        /// <summary>Gets or sets the object number of the form XObject, or zero before it is written.</summary>
        public int FormNumber { get; set; }

        /// <summary>Gets or sets the object number of the stream that draws the form.</summary>
        public int StreamNumber { get; set; }

        /// <summary>Gets or sets the resource name the form is drawn by.</summary>
        public string FormName { get; set; } = string.Empty;

        /// <summary>Gets or sets the resource name the graphics state is selected by.</summary>
        public string StateName { get; set; } = string.Empty;

        /// <summary>Gets or sets the graphics state that applies the opacity and the blend mode.</summary>
        public PdfDictionary State { get; set; } = new();
    }
}
