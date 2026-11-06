# ADR-003: The per-letter read model stays a record graph until 2.0

- **Status**: Accepted — 2026-11-06
- **Date**: 2026-11-06
- **Related**: the allocation pass recorded in [`docs/competitive-scoreboard.md`](../competitive-scoreboard.md)
  and [`docs/mutation-testing.md`](../mutation-testing.md); `PdfGlyph`, `PdfLetter`, `PdfRectangle`, `PdfWord`

## Context

A 200-page extraction allocates **29 557 KiB** after the allocation pass — about 426 B per letter — and the
per-letter records are the largest part of it: `PdfGlyph` (a record class, ~80 B), `PdfLetter` (a record
class, ~96 B) and one `PdfRectangle` (a record class, ~48 B) per letter, plus the arrays each `PdfWord` owns.
Almost none of it is retained (0.6 MiB after a collection), so the cost is allocation and collection rather
than residency.

The proposal was to turn `PdfGlyph`, `PdfLetter` and `PdfRectangle` into `readonly record struct`s.

## What was measured

| Variant | Allocations (200 pages) | Warm extraction |
| --- | ---: | ---: |
| record classes (shipped) | **33 183 KiB** | 58.8 ms |
| `PdfGlyph` + `PdfLetter` as structs, exact capacity estimate | 44 852 KiB | 38.6 / 52.9 ms (two runs) |
| `PdfGlyph` + `PdfLetter` as structs, 12.5 % capacity slack | 34 985 KiB | 39.5 ms |
| `PdfRectangle` as a struct as well | did not build for measurement without rewriting fifteen nullable call sites | — |

## Decision

**The types stay record classes in 1.x.** The conversion is rejected on evidence, not postponed by taste.

The reason is structural. `PdfWord` owns its own `PdfGlyph[]` and `PdfLetter[]`, so a struct glyph is stored
twice — once in the page's glyph list and once in the word's array — where a class instance is referenced
from both places. That copy is the allocation the struct was meant to remove, and it is why the measured
allocations rose rather than fell. A conversion can only pay off after a word's collections become **slices
over the page's one array**, which is an API redesign rather than a change of type kind.

It is also a breaking change: a record class exposes `<Clone>$`, an `Equals(T?)` with a nullable parameter,
`==`/`!=` operators and reference semantics that `PdfRectangle?` relies on in fifteen public members.

## Consequences

- The allocation column stays dominated by the read model, which is the product contract; the pass that
  lowered it — a word's text built once at its final length, the glyph list sized from the first page — is
  recorded in the competitive scoreboard.
- `PdfGlyph.BoundingBox` is computed on every access and allocates a `PdfRectangle` each time. Caching it is
  the only improvement available without a type change, and it was measured as a win on the negative-width
  path only, so it was not taken.
- A 2.0 candidate is written down rather than remembered: **`PdfWord.Glyphs`/`Letters` as
  `ReadOnlyMemory<T>` slices over the page's array, with `PdfGlyph`/`PdfLetter`/`PdfRectangle` as structs.**
  It must be measured on the same harness before adoption, and it will need the `PublicAPI` removal entries
  that the rejected conversion already showed how to write.
