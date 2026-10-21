# Contract: Inline Payload Budget

**Options type**: `PdfEngineOptions` (`SharpCorePDF.Infrastructure`) | **Property**: `MaxInlineContentBytes` | **Default**: `256 * 1024` (256 KiB)

Three surfaces return a payload inside a read model: image samples, image masks and embedded files. Each
of them could be hundreds of megabytes, and a read that hands back a whole document's worth of bytes
would make the library unusable for the indexing and reporting workloads it is meant for. One budget,
applied identically at all three, is how the library stays predictable: a read never silently allocates
a payload larger than the configured ceiling.

One budget rather than three, because a caller who has to reason about "small enough to inline" should
not have to reason about it per surface, and because a mask is not meaningfully different from the
image it belongs to.

## The rule

For every payload-bearing value (`PdfImageInfo`, `PdfImageMask`, `PdfEmbeddedFileInfo`):

```text
payloadLength = ByteLength                 // the length of the payload the API would return:
                                           // decoded bytes for a supported filter,
                                           // stored bytes for a pass-through filter

if payloadLength <= MaxInlineContentBytes:
    Data        = the bytes
    IsBuffered  = true
else:
    Data        = null
    IsBuffered  = false
    ByteLength  = payloadLength            // still truthful
```

- The comparison is `<=`: a payload exactly at the budget is buffered. A budget of `0` therefore
  buffers only a zero-length payload.
- The budget applies to the payload the API conceives of, not to the compressed bytes in the file. A
  1 MiB Flate stream that inflates to 3 MiB is withheld at a 256 KiB budget even though its stored form
  fits.
- A `DCTDecode` payload is the stored JPEG bytes, since nothing is decoded; a 400 KiB JPEG is withheld
  at a 256 KiB budget exactly as a 400 KiB raw payload would be.
- Nothing about the entry changes apart from `Data` and `IsBuffered`: geometry, filter, decodability
  and page list are the same whether or not the payload was captured.

## Detection, not inference

A caller never has to guess why `Data` is null:

| Observation | Meaning |
| --- | --- |
| `Data is not null`, `IsBuffered` | The payload is in hand, `Data.Value.Length == ByteLength` |
| `Data is null`, `IsBuffered == false`, `ByteLength > 0` | Withheld by the budget; fetch it with the copy route |
| `Data is null`, `ByteLength == 0`, kind is `ColorKey` | A colour key mask: it has no payload at all |
| The entry is absent from the result | The object does not exist or could not be read; a warning says which |

`IsBuffered` exists precisely so the third case is distinguishable from the second. It is not derivable
from `Data` alone, because a zero-length payload is buffered with an empty `Data`.

## No warning

The budget never raises a warning. `Data == null` with a truthful `ByteLength` is the contract, not a
defect, and a warning would turn a normal condition into noise in every log that reads the warnings of a
large document. Warnings are reserved for damage: an undecodable filter, an unreadable stream, a
missing object.

The consequence is deliberate and worth stating plainly: **a document whose images are all above the
budget produces a clean result with no bytes in it.** That is not a failure, and callers that treat
"no warnings" as "everything is in the result" will be surprised once — which is why the `IsBuffered`
flag and the copy route are part of the same contract.

## Where the ceiling is enforced

The budget is read from the injected `PdfEngineOptions` once per read and passed down as an `int` cap.
It is applied:

- after a filter chain has produced its final payload, so the length compared is the length returned;
- during a Flate decode, which stops capturing once the cap is crossed and keeps counting the length, so
  a decompression bomb is measured rather than materialised;
- to a pass-through payload by checking the stored length directly.

Two honest caveats about peak memory, both bounded and neither affecting correctness:

1. A Flate stream **with** a predictor, and the ASCII hex and ASCII85 filters, are decoded before the
   cap is applied, because the transform needs the whole input. Their peak is the decoded size even when
   the payload is then withheld. These are rare in the images this library is pointed at; the common
   Flate and DCT cases are bounded.
2. A multi-filter chain that crosses the budget mid-chain stops and reports the payload as undecodable
   (`IsDecodable == false`, `ImageFilterUnsupported`), because a later filter cannot run on bytes that
   were not kept. This is reported, never guessed at.

## What a caller does

```csharp
var images = await reader.ReadImagesAsync(source, PageRange.All);

foreach (var image in images.Items.Where(i => !i.IsBuffered && i.ByteLength > 0))
{
    await reader.SaveImageDataAsync(source, image, $"images/{image.Id.Replace(' ', '-')}.bin");
}
```

| Situation | Right call |
| --- | --- |
| `IsBuffered` is set | Use `Data`; nothing else to do |
| `IsBuffered` is clear, bytes wanted | `CopyImageDataAsync` / `CopyEmbeddedFileDataAsync` into a stream of your choosing, or the `Read*DataAsync` / `Save*` extensions |
| `IsBuffered` is clear, bytes not wanted | Use `ByteLength` to size a destination or to report a total; do not fetch |
| Most payloads are withheld and you would rather have them | Raise `MaxInlineContentBytes`; there is no per-document override |
| A colour key mask | `ColorKey` is the answer; there is nothing to fetch |

Raising the budget trades memory for convenience. At 256 KiB a thousand-image report holds at most a
quarter of a gigabyte of samples in the worst case, which is the scale the default is chosen for.

## The copy route

`CopyImageDataAsync`, `CopyImageMaskDataAsync` and `CopyEmbeddedFileDataAsync` are the bounded half of the
same contract:

- They produce **exactly** the bytes the read would have put in `Data` — same decode, same pass-through
  decision, same result. A test that compares the two is the way to prove the rule holds.
- They work for buffered payloads too, so a caller never needs two code paths.
- They stream through a pooled scratch buffer: the working set is the chunk, not the payload.
- They flush the destination and leave it open; they never dispose it.
- They re-open the document and re-walk it to find the same object. The handle is an address, not a
  cached buffer, so it stays valid across calls and does not pin memory.

## A mask is copied too

`CopyImageMaskDataAsync` completes the set. A soft (`/SMask`) or explicit (`/Mask` stream) mask whose
payload is above the budget is fully described — kind, dimensions, bits per component, colour space,
filter, decodability and truthful `ByteLength` — and its bytes are fetched through the same pooled
buffer as the image's, so raising `MaxInlineContentBytes` is no longer the only way to obtain them.

A mask is not an object: one attached directly to its image dictionary has no object number, so the
route resolves it by the pair `(ImageId, Kind)` the record carries. That pair also makes the handle
stable across calls and independent of the walk that produced it. A colour key mask is a list of sample
ranges with no payload, so asking for its bytes is an `ArgumentException` rather than an empty write —
the alternative would report success while writing nothing. The route re-walks the document exactly as
`CopyImageDataAsync` does, and refuses a mask whose `(ImageId, Kind)` the document does not contain.

## Conformance checklist

1. A payload of exactly `MaxInlineContentBytes` is buffered; one byte more is withheld.
2. `ByteLength` is identical in both states, and equals what the copy route writes.
3. `MaxInlineContentBytes = 0` buffers nothing except a zero-length payload.
4. A payload inflated to more than the budget is withheld even when its stored form is small.
5. `Data` and `IsBuffered` never disagree, and `IsBuffered` is false only when `Data` is null.
6. No payload-related warning is raised by a payload being withheld.
7. A colour key mask reports `ByteLength == 0`, `Data == null`, `IsBuffered == false` and its
   `ColorKey` values.
8. The copy route and the inline payload agree byte for byte, for a Flate image, a DCT image and an
   unsupported filter.
9. `PdfEngineOptions.Validate()` reports a negative budget.
10. A mask above the budget is withheld with a truthful `ByteLength`, and `CopyImageMaskDataAsync` writes
    exactly those bytes; a colour key mask is refused rather than written empty.

## Non-goals

- No per-document or per-call budget override.
- No streaming *out* of a read model: `Data` is either the whole payload or absent, never a prefix.
- No automatic spilling of large payloads to temporary files.
- No exception, no warning and no truncated array when the budget bites.
