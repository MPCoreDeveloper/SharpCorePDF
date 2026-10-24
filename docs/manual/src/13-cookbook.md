# 13. Cookbook

Short, complete recipes for the shapes that come up most often. Each one assumes `IPdfMerger` and
`IPdfInspector` are available, as described in Chapter 10, and that the destination path is not one of
the input files.

## Merge every PDF in a folder, in a defined order

`EnumerateFiles` returns files in an unspecified order on most platforms. Sort explicitly, and sort
numerically if your files are numbered without zero padding.

```csharp
var files = Directory
    .EnumerateFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (files.Length == 0)
{
    throw new InvalidOperationException($"No PDF files found in '{folder}'.");
}

var plan = MergePlan.Create();

foreach (var file in files)
{
    plan = plan.AddFile(file);
}

await merger.MergeToFileAsync(plan, Path.Combine(folder, "merged.pdf"));
```

`merged.pdf` itself matches `*.pdf`; on a second run the plan would include the previous result and the
destination would truncate it before the merge reads it. Filter the output name out, write outside the
folder, or use the temporary-file pattern from Chapter 9.

For `report-2.pdf` before `report-10.pdf`, the default comparator is not enough: it compares text, so
`report-10.pdf` sorts before `report-2.pdf`. Zero-pad the names, or sort with a natural-order comparer
that understands embedded numbers.

## Ask for a password only when one is needed

Inspection never throws for an encrypted document it cannot open; it reports it.

```csharp
var content = new PdfFileSource(path);
var info = await inspector.InspectAsync(content, cancellationToken: cancellationToken);

string? password = null;

if (!info.IsReadable)
{
    if (!info.RequiresPassword)
    {
        throw new InvalidOperationException(
            $"'{info.Metadata.Title ?? Path.GetFileName(path)}' cannot be opened, and a password would not help.");
    }

    password = PromptForPassword(path);

    if (password is null)
    {
        return;
    }

    info = await inspector.InspectAsync(content, password, cancellationToken);

    if (!info.IsReadable)
    {
        throw new InvalidOperationException("The password did not open the document.");
    }
}

var plan = MergePlan.Create().AddFile(path, password: password);
await merger.MergeToFileAsync(plan, destination, cancellationToken);

static string? PromptForPassword(string path)
{
    Console.Write($"Password for '{Path.GetFileName(path)}': ");
    return Console.ReadLine();
}
```

Two details matter. A document that is only restricted against editing opens with no password at all,
so `password` stays `null` and the merge succeeds — asking for a password there would be confusing.
And `InspectAsync` must be repeated with the password if you want to use the page count for a range;
`info.PageCount` from the unreadable result is not trustworthy.

`Console.ReadLine` echoes the password. In a real client, use the password control of your UI or a
masked console helper; the branching logic stays the same.

## Extract a page range

Extraction is a merge of one source with a selection:

```csharp
var plan = MergePlan.Create()
    .AddFile("manual.pdf", pages: "3-7,12")
    .WithOutput(options => options with
    {
        OutlinePolicy = OutlinePolicy.Preserve,
        MetadataPolicy = MetadataPolicy.FromPrimarySource,
    });

await merger.MergeToFileAsync(plan, "excerpt.pdf");
```

The result contains six pages in the order written: 3, 4, 5, 6, 7, 12. With `OutlinePolicy.Preserve` the
bookmarks of the source are carried over and renumbered onto the pages of the excerpt; the policy is the
default. Page labels are dropped by default because a label such as "iv" no longer describes the same
position in the new document. Set `PageLabelPolicy = PageLabelPolicy.FromPrimarySource` only when the
copied labels still make sense from the first output page, as they do when the selection starts at
page 1.

## Encrypt the result

```csharp
var options = new PdfEncryptionOptions
{
    Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey,
    UserPassword = "reader-secret",
    OwnerPassword = "owner-secret",
    Permissions = PdfPermissions.PrintOnly,
};

var plan = MergePlan.Create()
    .AddFile("internal.pdf")
    .WithOutput(output => output with
    {
        TargetVersion = PdfVersion.V1_7,
        Encryption = options,
    });

await merger.MergeToFileAsync(plan, "internal-protected.pdf");
```

AES-256 requires PDF 1.7, which is the default target, so the explicit version is only there to show the
dependency. The reader who supplies `reader-secret` may print but not copy text or modify the document;
the owner password unlocks every permission. If you omit `OwnerPassword`, the user password acts as the
owner password too — convenient, but then anyone who can open the document can also remove the
restrictions.

## Merge uploads in an ASP.NET Core endpoint

`IFormFile` gives you a stream; `AddStream` accepts one and reads it exactly once at merge time. Merging
to bytes keeps the response simple because a byte array has a known length:

```csharp
[HttpPost("merge")]
[RequestSizeLimit(100_000_000)]
public async Task<IActionResult> MergeAsync(
    [FromForm] List<IFormFile> files,
    CancellationToken cancellationToken)
{
    if (files.Count == 0)
    {
        return BadRequest("Upload at least one PDF.");
    }

    var plan = MergePlan.Create();

    foreach (var file in files)
    {
        plan = plan.AddStream(file.OpenReadStream(), file.FileName);
    }

    var merged = await merger.MergeToBytesAsync(plan, cancellationToken);

    return File(merged, "application/pdf", "merged.pdf");
}
```

The streams belong to the request and are read before the action returns, so no upload outlives the
request. A total request limit is not optional: each source is buffered in full, so an unlimited upload
is an unlimited allocation. If the result may be large, write it to a temporary file and return a
`PhysicalFile` or `FileStreamResult` instead of holding a second copy in memory.

`MergeToBytesAsync` is all-or-nothing: an exception leaves no partial response body to send, because the
body is only produced after the merge succeeded. Streaming the merge directly to `Response.Body` would be
cheaper but sends a truncated document when a later source fails.

## Report what was written

The result is meant to be logged as-is. Do not parse the text of a warning for control flow; branch on
its `Kind`, which is the stable part of the contract.

```csharp
var result = await merger.MergeToFileAsync(plan, destination, cancellationToken);

logger.LogInformation(
    "Merged {SourceCount} source(s) into {PageCount} page(s), {OutputBytes} bytes, PDF {Version} in {Duration} ms.",
    result.SourceCount,
    result.PageCount,
    result.OutputBytes,
    result.OutputVersion,
    result.Duration.TotalMilliseconds);

foreach (var source in result.Sources.Where(source => !source.IsComplete))
{
    logger.LogInformation(
        "{SourceName}: {PagesContributed} of {SourcePageCount} pages selected.",
        source.SourceName,
        source.PagesContributed,
        source.SourcePageCount);
}

foreach (var warning in result.Warnings)
{
    logger.LogWarning("{WarningKind}: {WarningMessage}", warning.Kind, warning.Message);
}
```

`SourceCount` counts the sources of the plan, not distinct files: adding the same file twice produces two
entries. `OutputBytes` is the size of the document the engine wrote; for `MergeToFileAsync` the file has
already been flushed when the call returns, so it matches the file length.

How many warnings are too many is a policy decision. A production service usually logs known-benign
kinds at information level and the rest at warning level, and keeps the warning kinds in its metrics so a
rise in repaired documents is visible before users report it.

## Override metadata and fix the modification date

`MetadataOverride` replaces only the fields you set. Everything else still follows
`MetadataPolicy`, which is `FromPrimarySource` by default:

```csharp
var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

var plan = MergePlan.Create()
    .AddFile("cover.pdf")
    .AddFile("body.pdf")
    .WithOutput(options => options with
    {
        MetadataOverride = new PdfMetadata
        {
            Title = "Annual Report 2026",
            Author = "Contoso",
            Subject = "Results for the financial year",
            Keywords = "annual report, 2026, results",
        },
        Producer = "Contoso Report Server 4.2",
        ModificationDate = created,
    });

await merger.MergeToFileAsync(plan, "annual-report.pdf");
```

The primary source is the first source unless `WithPrimarySource` says otherwise, so the title you did not
override, the custom properties and the creation date come from `cover.pdf`. Viewer preferences, page
layout and page mode are also taken from the primary source, but they are catalog entries rather than
document information and are not affected by `MetadataPolicy`. To drop the metadata fields you did not
override, set `MetadataPolicy = MetadataPolicy.None`; the policy starts from an empty information
dictionary and the override is still applied on top. Chapter 6 lists the policies.

`Producer` and `ModificationDate` are output options: every merge writes them, and setting them pins the
values that would otherwise describe the merge run. Neither can be set through `MetadataOverride`;
`PdfMetadata.Producer` and `PdfMetadata.ModificationDate` are ignored for the result. Fixing the
modification date removes one source of run-to-run difference. The output is reproducible only when the
inputs, the modification date and the producer are the same; Chapter 6 explains the rest.

## Split a large job into batches

Every source is held in memory until the merge ends, so a hundred large files can exhaust a process even
though each one is below `MaxSourceBytes`. Merging in batches makes each intermediate job smaller and
isolates a failure to one batch, but it does not make the final merge hold one batch: every batch file is
a source of that merge too.

```csharp
static async Task MergeInBatchesAsync(
    IPdfMerger merger,
    IReadOnlyList<string> files,
    string destination,
    int batchSize,
    CancellationToken cancellationToken)
{
    if (files.Count == 0)
    {
        throw new ArgumentException("There must be at least one file to merge.", nameof(files));
    }

    if (batchSize < 1)
    {
        throw new ArgumentOutOfRangeException(nameof(batchSize), "The batch size must be at least one.");
    }

    var workDirectory = Path.Combine(Path.GetTempPath(), $"sharpcorepdf-{Guid.NewGuid():N}");
    Directory.CreateDirectory(workDirectory);

    try
    {
        var batches = new List<string>();

        for (var offset = 0; offset < files.Count; offset += batchSize)
        {
            var plan = MergePlan.Create();

            foreach (var file in files.Skip(offset).Take(batchSize))
            {
                plan = plan.AddFile(file);
            }

            var batchPath = Path.Combine(workDirectory, $"batch-{batches.Count:D4}.pdf");
            await merger.MergeToFileAsync(plan, batchPath, cancellationToken);
            batches.Add(batchPath);
        }

        var final = MergePlan.Create();

        foreach (var batch in batches)
        {
            final = final.AddFile(batch);
        }

        await merger.MergeToFileAsync(final, destination, cancellationToken);
    }
    finally
    {
        if (Directory.Exists(workDirectory))
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }
}
```

The cost is reading and writing the bytes twice and losing some document-level features twice: each
intermediate merge applies the default policies, so page labels are already gone before the final merge.
The final merge still reads and holds every batch, so peak memory is the total size of the batch files,
not the size of one batch. If that total is too large, produce several smaller output documents, select
fewer pages per document, or give the process more memory; the engine cannot write one merged document
without holding all of its sources. If document-level features matter, keep the batch count low enough
that the final merge is acceptable.

## Accept a document only after checking it

A preflight gate turns a class of runtime failures into a clear rejection:

```csharp
const int MaximumPages = 2_000;

var content = new PdfFileSource(candidatePath);
PdfDocumentInfo info;

try
{
    info = await inspector.InspectAsync(content, cancellationToken: cancellationToken);
}
catch (PdfSourceFormatException exception)
{
    return Reject($"not a readable PDF: {exception.Detail}");
}

if (!info.IsReadable)
{
    return Reject(info.RequiresPassword
        ? "the document needs a password"
        : "the document uses an unsupported security handler");
}

if (info.PageCount == 0)
{
    return Reject("the document has no pages");
}

if (info.PageCount > MaximumPages)
{
    return Reject($"the document has {info.PageCount} pages; the limit is {MaximumPages}");
}

if (info.Warnings.Any(warning => warning.Kind is
        PdfWarningKind.PageTreeMissing or PdfWarningKind.PageTreeCycle))
{
    return Reject("the page tree is damaged; fix the source before uploading it");
}

return Accept(info);
```

A few warnings deserve rejection, most do not. `PageTreeMissing` means the document contributes no
pages; `PageTreeCycle` means a page tree pointed back at itself. A repaired cross-reference table, an
assumed page size or a truncated object stream on an otherwise readable document is usually a reason to
log, not to refuse the file. Chapter 8 lists every kind with its meaning.

Notice that `PdfSourceFormatException` is caught even though inspection is supposed to report rather than
throw. It still throws when the bytes are not a PDF at all or the limits are exceeded — those are not
"encrypted but closed" conditions that a `PdfDocumentInfo` can describe.

## Extract the text a document carries, and keep its coordinates

A search needs the words; a highlight needs where they are. One read returns both:

```csharp
var reader = provider.GetRequiredService<IPdfContentReader>();
var source = PdfFileSource.From("report.pdf");

var text = await reader.ReadTextAsync(
    source,
    PageRange.FromTo(1, 20),
    cancellationToken: cancellationToken);

foreach (var block in text.Items)
{
    hits.AddRange(
        block.Words
            .Where(word => word.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Select(word => (block.PageNumber, word.Text, word.BoundingBox)));
}

foreach (var warning in text.Warnings)
{
    logger.LogWarning("{Kind}: {Message}", warning.Kind, warning.Message);
}
```

Blocks come in reading order — down the page, then along the line — so `block.Text` is a paragraph a
search index or a translation call can take as it is, while `block.Words` carries the rectangle of every
word for a viewer that has to draw a highlight. A `PdfWarningKind.TextUnreadable` warning is the one to
expect on a scan, or on a font this library cannot decode: the words that could be read are still
returned, and the warning names the font that was in the way. Text drawn inside a form is included,
because the read follows the form's matrix and the resources it inherits (Chapter 8).

This is reading, not conversion: nothing is reflowed, and a two-column page comes back as blocks that
alternate between the columns. When a caller knows the layout, `BoundingBox.Left` is enough to sort the
blocks again before merging them.

## Write a cover page and put it in front of a document

A batch of statements needs one cover per document: a logo, the addressee and two lines of text. The
cover is authored in memory and merged as the first source, so no temporary file exists at any point.

```csharp
public async Task<string> AddCoverAsync(
    string statementPath,
    string logoPath,
    string fontPath,
    CancellationToken cancellationToken)
{
    // Both are read once per process in a service; a typeface is immutable and can be shared.
    var logo = await File.ReadAllBytesAsync(logoPath, cancellationToken);
    var body = PdfTypeface.FromTrueType(await File.ReadAllBytesAsync(fontPath, cancellationToken));

    var cover = PdfDocumentBuilder.Create(PdfAuthoringOptions.Default with
    {
        CreationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    })
        .WithMetadata(new PdfMetadata { Title = "Décompte 12345", Creator = "Billing" })
        .AddPage(PdfPageSize.A4, page => page
            .DrawImage(logo, x: 40, y: 30, width: 152, height: 87)
            .DrawText("Décomptes SPRL", 330, 180, PdfTypeface.Standard(StandardFont.Helvetica), 12)
            .DrawText("Rue de l'Église 1", 330, 194, PdfTypeface.Standard(StandardFont.Helvetica), 12)
            .DrawText("1000 Bruxelles", 330, 208, PdfTypeface.Standard(StandardFont.Helvetica), 12)
            .DrawText("Veuillez trouver ci-joint le décompte pour l'immeuble 12345", 150, 400, body, 12)
            .DrawText("pour la période du 01/01/2025 au 31/12/2025.", 150, 414, body, 12)
            .DrawText("Sending 1/3", 150, 550, body, 12))
        .Build("cover.pdf");

    var destination = Path.ChangeExtension(statementPath, ".with-cover.pdf");

    var plan = MergePlan.Create()
        .AddSource(PdfSource.Create(cover))
        .AddFile(statementPath);

    await merger.MergeToFileAsync(plan, destination, cancellationToken);
    return destination;
}
```

The coordinates are points from the top-left corner, and the `y` of `DrawText` is the baseline. The
`PdfTypeface.Standard` calls create the same typeface every time, which is cheap but needless: hoist one
instance next to the embedded face in a service. Keep the creation date fixed if the cover is compared
between runs, and leave it out when the real creation moment matters (Chapter 12).

The text round-trips: `ReadTextAsync` gives back `Veuillez trouver ci-joint le décompte…` with its
accents, because the embedded font carries a `ToUnicode` map. The standard font covers the address block
because it is Windows-1252 text; a character outside that encoding would have been refused when the cover
was written, and an embedded face is the answer for anything else.

### When the paragraph comes from a template

Two hand-placed lines are fine while the sentence is fixed. When the wording comes from a template, the
lines have to be computed, and Chapter 16 is the surface for it (added in `1.2`; `1.1.0` drew one line per
call). Put the first baseline where the hand-placed line was, so the cover does not have to be re-measured:

```csharp
// bodyText is the paragraph the template produced; body is the embedded face from above.
var style = new PdfTextStyle(body, 12, leading: 15);
var paragraph = PdfTextLayouter.Layout(bodyText, style, new PdfTextLayoutOptions(maxWidth: 295));

var metrics = body.Metrics;
var firstBaseline = metrics.Ascent * style.FontSize / metrics.UnitsPerEm;
var top = 400 - firstBaseline;          // the box corner that puts the first baseline back at 400
```

`295` is the A4 width minus a 150-point margin on each side. The block is drawn with one call, and the
next block is placed at `top + paragraph.Bounds.Height`:

```csharp
page.DrawTextLayout(paragraph, new PdfPoint(150, top));
page.DrawText("Sending 1/3", 150, top + paragraph.Bounds.Height + 20, body, 12);
```

`Bounds.Height` runs from the top of the box to the descender of the last line, so it is the space the
paragraph actually needs — not the number of lines times the leading. A template that overflows the column
throws at `Layout`, naming the word that did not fit, unless `BreakWords` or a `Clip`/`Continue` policy is
stated.
