using SharpCorePDF.Domain.ReadModels;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Infrastructure;

/// <summary>
/// The image half of <see cref="PdfPageContentReader"/>: what a page paints, where it paints it, and
/// the bytes the document holds for it.
/// </summary>
public sealed partial class PdfPageContentReader
{
    /// <summary>The depth to which a form inside a form is followed, as a guard against cycles.</summary>
    private const int MaxFormDepth = 8;

    /// <inheritdoc />
    public async Task<PdfReadResult<PdfImageInfo>> ReadImagesAsync(
        string sourceName,
        Stream content,
        PageRange range,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        NoteClosedEncryption(reader, warnings);

        var walker = new ImageWalker(reader, options.MaxInlineContentBytes, warnings);

        foreach (var page in SelectPages(reader, range))
        {
            walker.WalkPage(page);
        }

        return Result(reader, walker.Build(), warnings);
    }

    /// <inheritdoc />
    public async Task CopyImageDataAsync(
        string sourceName,
        Stream content,
        PdfImageInfo image,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();
        var pages = reader.Pages;

        IEnumerable<SourcePage> candidates = image.Pages.Count == 0
            ? pages
            : image.Pages
                .Where(number => number >= 1 && number <= pages.Count)
                .Select(number => pages[number - 1]);

        var walker = new ImageWalker(reader, options.MaxInlineContentBytes, warnings) { FindId = image.Id };

        foreach (var page in candidates)
        {
            walker.WalkPage(page);

            if (walker.Found is not null)
            {
                break;
            }
        }

        if (walker.Found is not { } stream)
        {
            throw new ArgumentException(
                $"'{image.Id}' is not an image of '{sourceName}'. Read the image from the document it came from.",
                nameof(image));
        }

        await PdfImageFilters
            .CopyAsync(stream, destination, reader.SourceName, cancellationToken)
            .ConfigureAwait(false);

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CopyImageMaskDataAsync(
        string sourceName,
        Stream content,
        PdfImageMask mask,
        Stream destination,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (mask.Kind == PdfImageMaskKind.ColorKey)
        {
            throw new ArgumentException("A colour key mask is not an image and carries no payload.", nameof(mask));
        }

        var reader = await OpenAsync(sourceName, content, password, cancellationToken).ConfigureAwait(false);
        var warnings = new List<PdfWarning>();

        var walker = new ImageWalker(reader, options.MaxInlineContentBytes, warnings)
        {
            FindId = mask.ImageId,
            FindMaskKind = mask.Kind,
        };

        foreach (var page in reader.Pages)
        {
            walker.WalkPage(page);

            if (walker.FoundMask is not null)
            {
                break;
            }
        }

        if (walker.FoundMask is not { } stream)
        {
            throw new ArgumentException(
                $"'{mask.ImageId}' has no {mask.Kind} in '{sourceName}'. Read the mask from the document it came from.",
                nameof(mask));
        }

        await PdfImageFilters
            .CopyAsync(stream, destination, reader.SourceName, cancellationToken)
            .ConfigureAwait(false);

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Walks what the pages of a range paint and collects the image objects it finds.</summary>
    private sealed class ImageWalker
    {
        private readonly PdfFileReader reader;
        private readonly int maxInlineBytes;
        private readonly List<PdfWarning> warnings;
        private readonly List<ImageEntry> order = [];
        private readonly Dictionary<string, ImageEntry> entries = new(StringComparer.Ordinal);
        private readonly HashSet<long> formsInProgress = [];
        private int anonymous;

        /// <summary>Initializes a new instance of the <see cref="ImageWalker"/> class.</summary>
        /// <param name="reader">The document.</param>
        /// <param name="maxInlineBytes">The largest payload that is buffered.</param>
        /// <param name="warnings">The list that receives the remarks made while walking.</param>
        public ImageWalker(PdfFileReader reader, int maxInlineBytes, List<PdfWarning> warnings)
        {
            this.reader = reader;
            this.maxInlineBytes = maxInlineBytes;
            this.warnings = warnings;
        }

        /// <summary>Gets the identifier of the single image the walk is looking for, if any.</summary>
        public string? FindId { get; init; }

        /// <summary>
        /// Gets the kind of mask the walk is looking for, or <see langword="null"/> when it looks for the
        /// image itself.
        /// </summary>
        public PdfImageMaskKind? FindMaskKind { get; init; }

        /// <summary>Gets the stream of the image the walk was looking for, once it has been found.</summary>
        public PdfStream? Found { get; private set; }

        /// <summary>Gets the stream of the mask the walk was looking for, once it has been found.</summary>
        public PdfStream? FoundMask { get; private set; }

        /// <summary>Gets a value indicating whether what the walk was looking for has been found.</summary>
        public bool IsFound => Found is not null || FoundMask is not null;

        /// <summary>Walks one page.</summary>
        /// <param name="page">The page.</param>
        public void WalkPage(SourcePage page)
        {
            if (IsFound)
            {
                return;
            }

            var pageNumber = page.Index + 1;
            var content = ReadPageContent(reader, page, warnings, pageNumber);

            if (content is null)
            {
                return;
            }

            var ordinal = 0;
            WalkContent(content, PageResources(reader, page), PdfMatrix.Identity, pageNumber, 0, ref ordinal);
        }

        /// <summary>Builds the read models, in the order the document draws the images.</summary>
        /// <returns>The images.</returns>
        public List<PdfImageInfo> Build()
        {
            var images = new List<PdfImageInfo>(order.Count);

            foreach (var entry in order)
            {
                entry.Pages.Sort();

                images.Add(new PdfImageInfo
                {
                    Id = entry.Id,
                    ObjectNumber = entry.ObjectNumber,
                    Generation = entry.Generation,
                    Pages = entry.Pages,
                    Width = entry.Width,
                    Height = entry.Height,
                    BitsPerComponent = entry.BitsPerComponent,
                    ColorSpace = entry.ColorSpace,
                    Filter = entry.Filter,
                    IsDecodable = entry.IsDecodable,
                    ByteLength = entry.ByteLength,
                    IsBuffered = entry.Data is not null,
                    Data = entry.Data,
                    Mask = entry.Mask,
                    Position = entry.Position,
                });
            }

            return images;
        }

        private void WalkContent(
            byte[] content,
            PdfDictionary? resources,
            PdfMatrix ctm,
            int pageNumber,
            int depth,
            ref int ordinal)
        {
            var saved = new Stack<PdfMatrix>();

            foreach (var operation in PdfContentScanner.Scan(content, reader.SourceName, pageNumber, warnings))
            {
                if (IsFound)
                {
                    return;
                }

                switch (operation.Operator)
                {
                    case "q":
                        saved.Push(ctm);
                        break;

                    case "Q" when saved.Count > 0:
                        ctm = saved.Pop();
                        break;

                    case "cm" when operation.Numbers.Count >= 6:
                        ctm = new PdfMatrix(
                            operation.Numbers[0],
                            operation.Numbers[1],
                            operation.Numbers[2],
                            operation.Numbers[3],
                            operation.Numbers[4],
                            operation.Numbers[5]) * ctm;

                        break;

                    case "Do" when operation.Names.Count > 0:
                        Paint(operation.Names[0], resources, ctm, pageNumber, depth, ref ordinal);
                        break;

                    case "BI" when operation.InlineImage is { } dictionary:
                        AddInline(dictionary, operation.InlineData, ctm, pageNumber, ++ordinal);
                        break;
                }
            }
        }

        private void Paint(
            string name,
            PdfDictionary? resources,
            PdfMatrix ctm,
            int pageNumber,
            int depth,
            ref int ordinal)
        {
            var value = reader.ResolveDictionary(resources?.Get("XObject"))?.Get(name);

            if (value is null)
            {
                warnings.Add(new(
                    PdfWarningKind.XObjectResourceMissing,
                    $"'{reader.SourceName}': page {pageNumber} paints /{name}, which its resources do not define."));
                return;
            }

            var reference = value as PdfReference;

            if (reader.Resolve(value) is not PdfStream stream)
            {
                warnings.Add(new(
                    PdfWarningKind.XObjectResourceMissing,
                    $"'{reader.SourceName}': page {pageNumber} paints /{name}, which is not a stream."));
                return;
            }

            if (stream.Dictionary.GetName("Subtype") == "Image")
            {
                AddObjectImage(reference, stream, ctm, pageNumber);
                return;
            }

            if (stream.Dictionary.GetName("Subtype") != "Form" || depth >= MaxFormDepth)
            {
                return;
            }

            if (reference is not null && !formsInProgress.Add(reference.ObjectNumber))
            {
                return;
            }

            if (TryDecodeStream(stream, out var formContent))
            {
                var formResources = reader.ResolveDictionary(stream.Dictionary.Get("Resources")) ?? resources;
                var formMatrix = ReadMatrix(reader, stream.Dictionary.Get("Matrix")) ?? PdfMatrix.Identity;

                WalkContent(formContent, formResources, formMatrix * ctm, pageNumber, depth + 1, ref ordinal);
            }
            else
            {
                warnings.Add(new(
                    PdfWarningKind.ContentStreamUnreadable,
                    $"'{reader.SourceName}': the content stream of a form on page {pageNumber} could not be decoded."));
            }

            if (reference is not null)
            {
                formsInProgress.Remove(reference.ObjectNumber);
            }
        }

        private void AddObjectImage(PdfReference? reference, PdfStream stream, PdfMatrix ctm, int pageNumber)
        {
            var id = reference is null
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"resource:{pageNumber}:{++anonymous}")
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{reference.ObjectNumber} {reference.Generation}");

            if (entries.TryGetValue(id, out var existing))
            {
                // One object painted several times, or by several pages, is one entry carrying every
                // page that paints it.
                if (!existing.Pages.Contains(pageNumber))
                {
                    existing.Pages.Add(pageNumber);
                }

                return;
            }

            var entry = new ImageEntry
            {
                Id = id,
                ObjectNumber = reference?.ObjectNumber,
                Generation = reference?.Generation,
                Position = ctm.TransformUnitSquare(),
            };

            entry.Pages.Add(pageNumber);
            Populate(entry, stream, pageNumber);

            entries[id] = entry;
            order.Add(entry);
            Record(entry, stream);
        }

        private void AddInline(PdfDictionary dictionary, ReadOnlyMemory<byte> data, PdfMatrix ctm, int pageNumber, int ordinal)
        {
            var id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"inline:{pageNumber}:{ordinal}");

            if (entries.ContainsKey(id))
            {
                return;
            }

            var stream = new PdfStream(dictionary, data);
            var entry = new ImageEntry
            {
                Id = id,
                Position = ctm.TransformUnitSquare(),
            };

            entry.Pages.Add(pageNumber);
            Populate(entry, stream, pageNumber);

            entries[id] = entry;
            order.Add(entry);
            Record(entry, stream);
        }

        /// <summary>
        /// Records the stream the walk was looking for: the image itself, or the mask of a named image
        /// resolved by the pair <c>(ImageId, Kind)</c> the mask carries.
        /// </summary>
        /// <param name="entry">The image entry that was just built.</param>
        /// <param name="stream">The image stream itself.</param>
        private void Record(ImageEntry entry, PdfStream stream)
        {
            if (FindId != entry.Id)
            {
                return;
            }

            if (FindMaskKind is not { } kind)
            {
                Found = stream;
                return;
            }

            if (entry.Mask is { } mask && mask.Kind == kind)
            {
                FoundMask = entry.MaskStream;
            }
        }

        private void Populate(ImageEntry entry, PdfStream stream, int pageNumber)
        {
            var dictionary = stream.Dictionary;

            entry.Width = ToInt(dictionary.GetInteger("Width"));
            entry.Height = ToInt(dictionary.GetInteger("Height"));
            entry.BitsPerComponent = ToInt(dictionary.GetInteger("BitsPerComponent"));
            entry.ColorSpace = DescribeColorSpace(dictionary.Get("ColorSpace"));
            entry.Filter = PdfImageFilters.DescribeFilter(dictionary);

            var payload = PdfImageFilters.Decode(stream, maxInlineBytes, reader.SourceName);

            entry.IsDecodable = payload.IsDecodable;
            entry.ByteLength = payload.Length;
            entry.Data = payload.Data;

            var (mask, maskStream) = BuildMask(entry.Id, pageNumber, dictionary);

            entry.Mask = mask;
            entry.MaskStream = maskStream;

            if (payload.Failed)
            {
                warnings.Add(new(
                    PdfWarningKind.ImageDataUnreadable,
                    $"'{reader.SourceName}': the payload of image {entry.Id} on page {pageNumber} could not be decoded, so its raw bytes are returned."));
            }
            else if (!payload.IsDecodable)
            {
                warnings.Add(new(
                    PdfWarningKind.ImageFilterUnsupported,
                    $"'{reader.SourceName}': image {entry.Id} on page {pageNumber} uses the filter chain {entry.Filter}, which the library does not decode; its raw bytes are returned."));
            }

            if (entry.Width is null || entry.Height is null)
            {
                warnings.Add(new(
                    PdfWarningKind.ImageDataUnreadable,
                    $"'{reader.SourceName}': image {entry.Id} on page {pageNumber} does not state a readable /Width or /Height."));
            }
        }

        private (PdfImageMask? Mask, PdfStream? Stream) BuildMask(
            string imageId,
            int pageNumber,
            PdfDictionary dictionary)
        {
            var soft = dictionary.Get("SMask");

            if (reader.Resolve(soft) is PdfStream softMask)
            {
                return (
                    BuildImageMask(imageId, pageNumber, softMask, PdfImageMaskKind.SoftMask, soft as PdfReference),
                    softMask);
            }

            var mask = dictionary.Get("Mask");

            if (reader.Resolve(mask) is PdfStream explicitMask)
            {
                return (
                    BuildImageMask(imageId, pageNumber, explicitMask, PdfImageMaskKind.ExplicitMask, mask as PdfReference),
                    explicitMask);
            }

            if (reader.ResolveArray(mask) is not { Count: > 0 } colorKeys)
            {
                return (null, null);
            }

            var values = new List<double>();

            foreach (var item in colorKeys.Items)
            {
                if (ReadNumber(reader, item) is { } number)
                {
                    values.Add(number);
                }
            }

            return (
                new PdfImageMask
                {
                    ImageId = imageId,
                    Kind = PdfImageMaskKind.ColorKey,
                    ColorKey = values,
                },
                null);
        }

        private PdfImageMask BuildImageMask(
            string imageId,
            int pageNumber,
            PdfStream stream,
            PdfImageMaskKind kind,
            PdfReference? reference)
        {
            var dictionary = stream.Dictionary;
            var payload = PdfImageFilters.Decode(stream, maxInlineBytes, reader.SourceName);

            if (payload.Failed || !payload.IsDecodable)
            {
                warnings.Add(new(
                    payload.Failed ? PdfWarningKind.ImageDataUnreadable : PdfWarningKind.ImageFilterUnsupported,
                    $"'{reader.SourceName}': the {kind} of image {imageId} on page {pageNumber} could not be decoded, so its raw bytes are returned."));
            }

            return new PdfImageMask
            {
                ImageId = imageId,
                Kind = kind,
                ObjectNumber = reference?.ObjectNumber,
                Generation = reference?.Generation,
                Width = ToInt(dictionary.GetInteger("Width")),
                Height = ToInt(dictionary.GetInteger("Height")),
                BitsPerComponent = ToInt(dictionary.GetInteger("BitsPerComponent")),
                ColorSpace = DescribeColorSpace(dictionary.Get("ColorSpace")),
                Filter = PdfImageFilters.DescribeFilter(dictionary),
                IsDecodable = payload.IsDecodable,
                ByteLength = payload.Length,
                IsBuffered = payload.Data is not null,
                Data = payload.Data,
            };
        }

        private static int? ToInt(long? value) =>
            value is { } number && number is >= int.MinValue and <= int.MaxValue ? (int)number : null;

        private static string? DescribeColorSpace(PdfObject? value) => value switch
        {
            PdfName name => name.Value,
            PdfArray { Count: > 0 } array => string.Join(" ", array.Items.Take(4).Select(DescribeColorSpaceItem)),
            _ => null,
        };

        private static string DescribeColorSpaceItem(PdfObject item) => item switch
        {
            PdfName name => name.Value,
            PdfInteger integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PdfReference => "reference",
            _ => "?",
        };
    }

    /// <summary>One image object as it is found, before the read model is built.</summary>
    private sealed class ImageEntry
    {
        public required string Id { get; init; }

        public long? ObjectNumber { get; init; }

        public int? Generation { get; init; }

        public List<int> Pages { get; } = [];

        public int? Width { get; set; }

        public int? Height { get; set; }

        public int? BitsPerComponent { get; set; }

        public string? ColorSpace { get; set; }

        public string? Filter { get; set; }

        public bool IsDecodable { get; set; } = true;

        public long ByteLength { get; set; }

        public ReadOnlyMemory<byte>? Data { get; set; }

        public PdfImageMask? Mask { get; set; }

        /// <summary>Gets or sets the stream the mask is stored in, when it has one.</summary>
        public PdfStream? MaskStream { get; set; }

        public PdfRectangle? Position { get; init; }
    }
}
