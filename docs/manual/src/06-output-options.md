# 6. Output Options

`MergeOutputOptions` describes the document you want, as opposed to the pages you want. It is an
immutable record with a dozen settings, all optional, and it is attached to a plan with `WithOutput`:

```csharp
var now = new DateTimeOffset(2025, 12, 31, 12, 0, 0, TimeSpan.Zero);

var plan = MergePlan.Create()
    .AddFile("cover.pdf")
    .AddFile("body.pdf")
    .WithOutput(options => options with
    {
        MetadataPolicy = MetadataPolicy.Merge,
        MetadataOverride = new PdfMetadata { Title = "Annual report 2025" },
        ModificationDate = now,
    });
```

| Setting | Type | Default | Effect |
| --- | --- | --- | --- |
| `TargetVersion` | `PdfVersion` | `V1_7` | The version written to the file header |
| `PrimarySourceId` | `PdfSourceId?` | `null` (first source) | Which source leads document-level features |
| `MetadataPolicy` | `MetadataPolicy` | `FromPrimarySource` | Where document information comes from |
| `MetadataOverride` | `PdfMetadata?` | `null` | Fields that replace whatever the policy produced |
| `ModificationDate` | `DateTimeOffset?` | `null` (now) | The modification date, and the basis of the file identifier |
| `OutlinePolicy` | `OutlinePolicy` | `Preserve` | Whether bookmarks are merged or dropped |
| `PageLabelPolicy` | `PageLabelPolicy` | `Drop` | Whether page labels are copied or dropped |
| `PreserveAnnotations` | `bool` | `true` | Whether page annotations are copied |
| `PreserveAcroForm` | `bool` | `true` | Whether the primary source's interactive form is copied |
| `PreserveEmbeddedFiles` | `bool` | `true` | Whether the primary source's attachments are copied |
| `Encryption` | `PdfEncryptionOptions?` | `null` | Whether the result is encrypted |
| `Producer` | `string` | `"SharpCorePDF <version>"` | The value of the `/Producer` entry |

The defaults follow one principle: **lose as little as possible, without inventing anything.** Pages,
annotations and bookmarks are merged from every source. Features that cannot meaningfully exist twice
in one document — document information, the interactive form, attachments, page labels — are taken
from one source, the primary one. Nothing is encrypted unless you ask for it.

`MergeOutputOptions.Validate()` returns a list of problems rather than throwing, and
`MergePlan.Validate()` includes those problems in its own report, so a bad combination surfaces before
a file is opened. The checks cover an unsupported target version, undefined policy values, a blank
producer and encryption settings that the target version cannot express (Chapter 7).

## Target version

The engine writes a classic cross-reference table, which requires PDF 1.4 or newer; `V1_4` through
`V2_0` are accepted and anything outside that range is rejected. `V1_7` is the default because it is
the most widely supported version that still uses the traditional file structure.

The target version is a claim, not a conversion. The library does not rewrite content to match an
older version and does not verify that copied objects only use features of that version. If your
sources use transparency, layers or other features introduced after 1.4 and you write a 1.4 file, the
file may open in lenient viewers and fail in strict ones. When in doubt, keep the default, or set the
target to the highest version among your sources.

## Choosing the primary source

`PrimarySourceId` names the source that leads document-level features. By default it is the first
source in the plan, but any source may be promoted:

```csharp
var body = PdfSource.Create(new PdfFileSource("body.pdf"), displayName: "Body");
var cover = PdfSource.Create(new PdfFileSource("cover.pdf"), displayName: "Cover");

var plan = MergePlan.Create()
    .AddSource(cover)
    .AddSource(body)
    .WithPrimarySource(body.Id);   // metadata, form and attachments come from body.pdf

plan.EnsureValid();
```

`WithPrimarySource` is the shortcut on the plan; `PrimarySourceId` in the output options is the same
setting, useful when the options are built elsewhere. A primary source must exist in the plan and must
be identified by `PdfSourceId`, not by file path. The call made last wins: `WithPrimarySource` changes
the current output options, and a later `WithOutput` can replace the value again.

The primary source is only consulted for features that cannot be merged. Its pages are not treated
specially: page order still comes from the plan, and its bookmarks are still merged with the rest. Every
source in the plan must contribute at least one page, so a primary source whose selection resolves to no
pages is rejected with `MergePlanValidationException` before anything is written; the primary is always a
source that actually contributes pages.

## Document information

`MetadataPolicy` has three values:

| Value | Result |
| --- | --- |
| `FromPrimarySource` | The primary source's document information, copied including custom entries |
| `Merge` | The primary source's values, with empty fields filled from the other sources in plan order |
| `None` | No document information, apart from the producer and modification date |

`FromPrimarySource` is the default because a merge is usually judged by its pages, and the metadata of
the document that leads the merge is the most defensible choice. `Merge` is useful when the first file
is a bare cover with no title but a later file has one. `None` produces the smallest, least revealing
dictionary.

`MetadataOverride` is applied after the policy and replaces only the fields you set:

```csharp
MetadataOverride = new PdfMetadata
{
    Title = "Annual report 2025",
    Author = "Finance",
    Keywords = "annual, report, 2025",
    CustomProperties = new Dictionary<string, string>
    {
        ["department"] = "finance",
    },
},
```

An empty string is a value, not an absence: it replaces the field with an empty string. `null` leaves
the field as the policy produced it. Custom properties are merged by key, with the override winning.
`Producer` and `ModificationDate` are the two fields `MetadataOverride` cannot set: the producer is
always `MergeOutputOptions.Producer`, and the modification date is always
`MergeOutputOptions.ModificationDate` (or the current UTC time when it is `null`). Setting either one
in the override has no effect; set the output option instead.

`ModificationDate` describes the merge run, not the source document, so it is never taken from the
policy or from `MetadataOverride`. Set it on the output options to fix the date for a whole plan.

A PDF can carry a second kind of metadata: an XMP packet, a small XML document stored in a stream
rather than in the information dictionary. When the metadata policy is not `None`, the primary
source's XMP packet is copied unchanged. The library does not regenerate it, so an XMP reader may see
the primary source's title even after `MetadataOverride` changes the title in the information
dictionary. If consistent XMP matters to your consumers, strip or rewrite it downstream.

## Bookmarks, labels and annotations

These settings are independent, and turning one off does not turn off the others.

```csharp
.WithOutput(options => options with
{
    OutlinePolicy = OutlinePolicy.Drop,       // no bookmarks at all
    PageLabelPolicy = PageLabelPolicy.FromPrimarySource,
    PreserveAnnotations = false,              // no links, highlights, stamps or widgets
    PreserveAcroForm = false,                 // no interactive form
    PreserveEmbeddedFiles = false,            // no attachments
})
```

**Bookmarks.** `Preserve` merges the outline trees of every source into one tree whose top-level items
follow source order. Bookmarks are remapped to their page's new position; a bookmark that points at a
page outside the source's selection is pointed at the first selected page instead. `Drop` writes no
outline at all and is the right choice when source bookmarks are meaningless in the new document.

**Page labels.** Page labels are the displayed page numbers — roman numerals for a preface, "A-1" for
an appendix. They are tied to absolute page indices, so they misalign as soon as sources are reordered
or partially selected, which is why `Drop` is the default. `FromPrimarySource` copies the primary
source's labels and applies them from the first output page, which is only accurate when the primary
source starts at page 1 of the result.

**Annotations.** `PreserveAnnotations` controls every annotation on the merged pages: link annotations,
highlights, notes, stamps, and the widget annotations that draw form fields. Turning it off is common
for print-oriented output. Bookmarks are not annotations and are unaffected; the cost is that internal
links stop working.

**Forms.** `PreserveAcroForm` copies the interactive form of the primary source only. Form fields from
the other sources are not added to the form, but their widget annotations remain on the pages when
annotations are preserved, so they stay visible without being fillable. Merging two independent forms
into one is not attempted: field names would collide and the result would be ambiguous.

**Attachments.** `PreserveEmbeddedFiles` copies the embedded files of the primary source. Attachments
from the other sources are not merged; if several sources carry attachments, choose the source with
the complete set as the primary, or attach files to the result afterwards.

## Reproducible output

`ModificationDate` does two jobs. It is written as the modification date, and it is the input from
which the document identifier is derived. Supply a fixed value and, for unencrypted output, the same
inputs produce the same bytes on every run. Leave it `null` and the current UTC time is used, so every
run differs.

Reproducibility has limits worth stating plainly. Encrypted output uses fresh random material, so two
encrypted merges of the same input are not byte-identical even with a fixed date (Chapter 7). The
producer string contains the assembly version, so upgrading the library changes the bytes. And
reproducible bytes are not a signature: they show that two files were produced the same way, not that
the content is trustworthy.

```csharp
var options = MergeOutputOptions.Default with
{
    ModificationDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
    Producer = "Contoso Report Pipeline",
};
```
