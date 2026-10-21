# Contract: `IPdfPageContentReader`

**Assembly**: `SharpCorePDF.Application` | **Namespace**: `SharpCorePDF.Application.Ports` | **File**: `src/SharpCorePDF.Application/Ports/IPdfPageContentReader.cs`
**Implementation**: `PdfPageContentReader` in `SharpCorePDF.Infrastructure` (partial across `Images`, `Pages`, `Metadata` files)

The engine-side twin of `IPdfContentReader`. It differs in exactly one respect: it takes a `Stream` and
a `sourceName` instead of an `IPdfContentSource`, so the engine never depends on the domain's source
abstraction and never decides I/O policy. `PdfContentReaderService` (Application) is the adapter: it
opens the source, hands the stream over, and keeps the `try/finally` that the engine must not own.

The port exists so the engine can be tested with a plain `MemoryStream` and so a second consumer (a
verifier, a future writer's read-back check) can reuse the engine without going through the domain
service.

## Surface

Same ten capabilities as the domain port, in the same order, with `(string sourceName, Stream content,
…)` in place of `(IPdfContentSource source, …)`:

| Member | Extra arguments | Returns |
| --- | --- | --- |
| `ReadImagesAsync` | `PageRange range` | `Task<PdfReadResult<PdfImageInfo>>` |
| `CopyImageDataAsync` | `PdfImageInfo image, Stream destination` | `Task` |
| `CopyImageMaskDataAsync` | `PdfImageMask mask, Stream destination` | `Task` |
| `ReadAnnotationsAsync` | `PageRange range` | `Task<PdfReadResult<PdfAnnotationInfo>>` |
| `ReadFormFieldsAsync` | — | `Task<PdfReadResult<PdfFormFieldInfo>>` |
| `ReadMetadataAsync` | — | `Task<PdfXmpResult>` |
| `ReadEmbeddedFilesAsync` | — | `Task<PdfReadResult<PdfEmbeddedFileInfo>>` |
| `CopyEmbeddedFileDataAsync` | `PdfEmbeddedFileInfo file, Stream destination` | `Task` |
| `ReadOutlineAsync` | — | `Task<PdfReadResult<PdfOutlineItem>>` |
| `ReadPageDetailsAsync` | `PageRange range` | `IAsyncEnumerable<PdfPageDetail>` |

Every member ends with `string? password = null, CancellationToken cancellationToken = default`.
There are no extension-block conveniences here: those are domain ergonomics and belong one layer up.

## Stream contract

| Aspect | Guarantee |
| --- | --- |
| Readability | `content` must be readable and positioned at the start of the document. The adapter guarantees both; a caller that uses the port directly must too |
| Seeking | The implementation may seek within `content`; the adapter always supplies a seekable stream |
| Start position | The document is read from the stream's current position, so a caller may embed a PDF in a larger stream by seeking to its first byte |
| End position | Unspecified after the call; a caller that needs the stream again must seek back itself |
| Ownership | The port never disposes, truncates or writes to `content` |
| Lifetime during enumeration | `ReadPageDetailsAsync` reads the stream for as long as the enumeration is alive |
| Destination | A `Copy*` destination must be writable (`ArgumentException` otherwise); it is flushed and left open |
| Concurrency | One stream may not be read by two calls at once. The implementation is stateless, so two streams may be read concurrently by one instance |

`sourceName` is used only in diagnostics — the message of a `PdfWarning` and an exception message. It
is never parsed and never used to open anything. `ArgumentException` is raised when it is null, empty
or whitespace, because a warning with no source name is not useful in a log.

## Postconditions shared with the domain port

The engine half must satisfy everything the domain contract promises, because the adapter is a pass-
through: ordering, one-based pages, warnings instead of exceptions for damage, empty results for a
document that cannot be opened, `PdfSourceFormatException` only for content that is not a PDF,
truthful `ByteLength`, `IsDecodable` alongside a naming `Filter`, and the inline budget rule. If the
two ever diverge, the domain contract wins and the engine is wrong.

## Implementation obligations

These are obligations of `PdfPageContentReader`, not of the interface — but they are what makes the
port usable at scale, so they are the parts a code review checks.

1. **One document per call.** The cross-reference table, object cache and page tree are opened once
   per call and shared by every object that call touches. `ReadPageDetailsAsync` opens them once and
   keeps them open across the whole enumeration, not once per page.
2. **Named destinations indexed once.** The `/Dests` name tree is read when a member first needs it,
   and every link or bookmark in that call resolves against the same index. Resolving a name is a
   dictionary lookup, never a scan of the tree.
3. **Laziness.** `ReadPageDetailsAsync` yields a page only after the page has been read, and does not
   read the next page until the consumer asks. A caller that stops after three pages has not paid for
   the rest of the range.
4. **Bounded copies.** `Copy*` stream through a pooled scratch buffer; they never materialise the
   payload to measure or to write it.
5. **Budget from options.** `MaxInlineContentBytes` comes from the injected `PdfEngineOptions`
   (default 256 KiB) and is applied after the true payload length is known.
6. **Encryption by the same path as inspection.** `PdfFileReader` opens the document and applies the
   password; the content reader has no separate decryption branch and no handler of its own.
7. **No writes.** Nothing is written, renamed, deleted or locked beyond the read share.
8. **No rendering.** Image samples are decoded to the bytes the document stores; nothing is
   rasterised, resampled, colour-converted or flattened. Text and vector content is not interpreted.
9. **Cancellation.** The token is checked before a document is opened and between pages; nested loops
   over objects within a page do not need a check per object, but a page must not be able to swallow a
   cancellation entirely.
10. **No domain dependency creep.** The port takes domain read models and value objects (it must — they
    are the result), but it must not take `IPdfContentSource`, must not know about DI lifetimes, and
    must not cache anything across calls.

## Registration

`AddSharpCorePDF()` registers the whole pair, independently of inspection and merging:

```csharp
services.TryAddSingleton(sp => new PdfPageContentReader(sp.GetRequiredService<PdfEngineOptions>()));
services.TryAddSingleton<IPdfPageContentReader>(sp => sp.GetRequiredService<PdfPageContentReader>());
services.TryAddSingleton<IPdfContentReader, PdfContentReaderService>();
```

`TryAdd` means a host that already supplies its own reader keeps it. Both are singletons because both
are stateless; neither holds a stream between calls, so a long-lived instance is not a long-lived
file lock.

## Conformance checklist

1. A `MemoryStream` over a valid PDF and the same bytes on disk produce equal results.
2. A stream positioned at a non-zero offset that holds a complete PDF at that offset reads correctly.
3. The stream is not disposed and not truncated by any member, including after an exception.
4. A `Copy*` to a read-only stream raises `ArgumentException` naming the parameter.
5. `ReadPageDetailsAsync` over a ten-page range reads pages only as they are pulled, and a consumer
   that breaks after one page leaves the document closed.
6. An encrypted document with a wrong password raises nothing: each collection member returns an
   empty result plus the encryption warning, and `ReadPageDetailsAsync` returns the pages it can find,
   each with empty collections and `ContentStreamUnreadable`.
7. Garbage input raises `PdfSourceFormatException`, not a raw parsing exception.
8. The same call twice yields equal records.
