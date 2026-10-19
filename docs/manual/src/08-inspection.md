# 8. Inspection

`IPdfInspector` answers "what is in this file?" without changing it and without producing a merge. It
is the read side of the library, and it uses the same reader as the merger, so what you inspect is what
you will merge.

```csharp
var info = await inspector.InspectAsync(
    new PdfFileSource("report.pdf"),
    password: null,
    cancellationToken: cancellationToken);
```

The method takes any `IPdfContentSource`, not just a file, so the same call works for a byte array or a
stream. There is no inspection overload that takes a path directly; wrap the path in a `PdfFileSource`,
which also supplies the display name used in messages.

## The document model

`PdfDocumentInfo` is an immutable record. The properties you are most likely to use:

| Property | Type | Meaning |
| --- | --- | --- |
| `SourceName` | `string` | The display name of the source |
| `Version` | `PdfVersion` | The version from the document header |
| `PageCount` | `int` | Number of pages, or `0` when the page tree is unreadable |
| `IsReadable` | `bool` | Whether the structure could be read at all |
| `IsEncrypted` | `bool` | Whether the document declares encryption |
| `RequiresPassword` | `bool` | Whether a password is what stands in the way |
| `SecurityHandlerRevision` | `int?` | The revision of the security handler, or `null` |
| `Metadata` | `PdfMetadata` | Title, author, subject, keywords, dates and custom entries |
| `Pages` | `IReadOnlyList<PdfPageInfo>` | One entry per page, in document order |
| `HasOutlines` | `bool` | Whether the document has bookmarks |
| `OutlineItemCount` | `int` | Bookmarks, counting nested items |
| `EmbeddedFileCount` | `int` | Number of attachments |
| `HasAcroForm` | `bool` | Whether the document has an interactive form |
| `SourceLengthBytes` | `long?` | Size of the source when known |
| `Warnings` | `IReadOnlyList<PdfWarning>` | Non-fatal remarks about the document |

Two computed properties save a loop: `FirstPageSize` is the first page's size or `null` for an empty
document, and `HasUniformPageSize` is `true` when every page has the same size — convenient for
deciding whether a merge will produce a uniform document.

`PdfPageInfo` describes one page:

| Property | Type | Meaning |
| --- | --- | --- |
| `Number` | `int` | The one-based page number |
| `Size` | `PdfPageSize` | The size as stored, before rotation |
| `Rotation` | `PageRotation` | How a viewer rotates the page before displaying it |
| `HasContent` | `bool` | Whether the page has a content stream |
| `AnnotationCount` | `int` | Number of annotations on the page |
| `DisplaySize` | `PdfPageSize` | `Size` with the rotation applied |

`PdfPageSize` is a value object with `WidthPoints` and `HeightPoints` as the source of truth, plus the
millimetre and inch conversions, `IsLandscape`, `IsPortrait` and the predefined `A4`, `A5`, `A3`,
`Letter` and `Legal` sizes. Use `DisplaySize`, not `Size`, when you care about what a reader sees: a
landscape page is frequently stored as a portrait page plus a 90-degree rotation.

```csharp
Console.WriteLine($"{info.SourceName}: PDF {info.Version}, {info.PageCount} page(s)");

if (info.FirstPageSize is { } size)
{
    Console.WriteLine($"First page: {size.WidthMillimeters:0} x {size.HeightMillimeters:0} mm");
}

if (!info.HasUniformPageSize)
{
    var sizes = info.Pages
        .Select(page => page.DisplaySize)
        .Distinct()
        .Select(size => size.ToString());

    Console.WriteLine($"Mixed page sizes: {string.Join(", ", sizes)}");
}

Console.WriteLine(info.HasOutlines
    ? $"{info.OutlineItemCount} bookmark(s)"
    : "No bookmarks");
```

## What inspection is not

Inspection reads structure, not content. It does not extract text, render pages, count words or check
that a font is embedded. `HasContent` says only that a content stream exists, not that it draws
anything visible. `AnnotationCount` counts annotations, not the form fields they belong to.

It is also not free. The inspector parses the document, including its page tree, so the cost is close
to reading the same document for a merge. Calling it on every request to display a page count doubles
the work; if you need both, merge and read the count from `MergeResult.PageCount` instead, or cache the
`PdfDocumentInfo` returned by one inspection.

## Encrypted and damaged documents

Inspection has a different failure contract from merging, and the difference is deliberate.

- An **encrypted document whose encryption stayed closed** does not throw. You get a
  `PdfDocumentInfo` with `IsEncrypted = true`, `IsReadable = false`, `RequiresPassword` telling you
  whether a password would help, and a warning explaining what happened. `PageCount` is zero and
  `Pages` is empty, but `SourceName`, `SecurityHandlerRevision` and `Warnings` are populated.
- A **malformed document that is not a readable PDF** throws `PdfSourceFormatException`. There is no
  partial result to return, because the object structure could not be established.
- **A document that is readable but damaged** does not throw. The reader repairs what it can — rebuilds
  a broken cross-reference table, skips an unreadable page-tree branch, assumes A4 for a page without a
  media box — and reports each repair as a warning.

```csharp
var info = await inspector.InspectAsync(new PdfFileSource(path), password);

if (!info.IsReadable)
{
    Console.WriteLine(info.RequiresPassword
        ? "The document is encrypted; supply the user or owner password."
        : "The document is encrypted with a security handler this library does not implement.");
}
```

`RequiresPassword` is about the *user* password. A document that opens without a password but is
restricted against editing has `IsEncrypted = true`, `IsReadable = true` and
`RequiresPassword = false`; its restrictions do not block inspection or merging. `PdfDocumentInfo`
does not expose the permission flags themselves — inspect the encryption dictionary with another tool
if you need to display them.

## Warnings

`PdfWarning` pairs a stable `Kind` with a human-readable `Message`. The kind is the contract; the text
may be reworded in any release, so match on `Kind` and log `Message`. The set of kinds is open, which
means a `switch` over `PdfWarningKind` must keep a `default` arm.

The kinds fall into four groups:

| Group | Examples | What it means |
| --- | --- | --- |
| Structure repaired | `CrossReferenceRebuilt`, `CrossReferenceEntryMismatch`, `ObjectStreamTruncated` | The reader recovered; the document may be damaged |
| Page tree | `PageTreeMissing`, `PageTreeCycle`, `PageSizeAssumed` | Pages were skipped, cut off, or described with a default size |
| Encryption | `EncryptionOpened`, `EncryptionHandlerUnsupported`, `CryptFilterUndefined` | The encryption was understood, opened, or could not be used |
| Merge decisions | `NamedDestinationsDropped`, `OutlinesDroppedByPolicy`, `BookmarksTruncated` | Content was intentionally not carried over |

`EncryptionOpened` is a warning even though it describes success: it tells a caller that the document
was protected and is not simply unprotected. `OutputVerified` is likewise a success signal, reported
only when `PdfEngineOptions.VerifyOutput` is enabled (Chapter 10).

Warnings from a source are folded into `MergeResult.Warnings`, so a caller who only looks at the merge
result still learns that a source had to be repaired. Identical warnings are not repeated; because the
message names the source, two damaged sources normally produce two warnings rather than one.

A practical handling policy:

```csharp
foreach (var warning in result.Warnings)
{
    logger.LogWarning("{Kind}: {Message}", warning.Kind, warning.Message);
}
```

Treat structural warnings as reasons to keep the source but investigate it, and treat merge-decision
warnings as expected when the plan deliberately drops something. Do not fail a merge because warnings
exist: if a condition was fatal, the merge would have thrown.

## Using inspection as a preflight

The inspector is most useful immediately before a merge:

1. Inspect each source with its password.
2. Reject documents that are unreadable, and ask for a password when `RequiresPassword` is true.
3. Resolve page selections against `PageCount` so a bad range is a validation message rather than an
   exception halfway through writing (Chapter 5).
4. Warn the user about repairs, encryption and unusual page sizes.
5. Merge only the sources that passed.

This mirrors the order the merger itself uses — open, read structure, resolve selections, write — but
gives you a place to interject a message, a prompt or a policy decision. It does not make the merge
safer in the sense of catching every error: a file can change between inspection and merge, and the
merger validates again. The gain is a better conversation with the user, not a substitute for handling
`PdfException` (Chapter 9).

