# Competitive scoreboard

**Purpose**: turn "come close to or beat PdfPig / PDFsharp" into measured evidence, not a slogan.
**Harness**: `benchmarks/SharpCorePDF.Competitive` (separate solution; never part of `SharpCorePDF.slnx`).
**Latest local report** (gitignored): `artifacts/competitive/report.md` and `results.json`.

## What this is, and is not

This scoreboard is **capability- and workload-specific**. It does not declare a single overall winner.
It never treats PdfPig or PDFsharp as ground truth. Correctness uses the repository's human-labelled
parity fixtures; independent validators (`qpdf` via `docs/tools/validate-pdf.py`) check captured writer
output. Competitor packages stay out of shipped packages and out of the unit-test project. Read
`benchmarks/SharpCorePDF.Competitive/THIRD-PARTY.md` before redistributing the harness: PdfPig is
Apache-2.0; PDFsharp 6.5 is AGPL-or-commercial and the harness selects the AGPL option only for that
isolated project.

Pinned subjects for the current methodology:

| Engine | Pin | Role |
| --- | --- | --- |
| SharpCorePDF | repository commit under test | product under measurement |
| PdfPig | NuGet `0.1.16` | reading and layout comparison; limited creation/merge |
| PDFsharp | NuGet `6.5.0` | creation and merge comparison; no text-extraction adapter |

## How to run

```powershell
dotnet restore .\benchmarks\SharpCorePDF.Competitive.slnx
dotnet run -c Release --project .\benchmarks\SharpCorePDF.Competitive -- `
  --output artifacts\competitive --warmups 2 --iterations 5 --capture

# Optional local corpus only — nothing is downloaded by the harness. Write the SHA-256 lock once
# after reviewing the folder, then run against it: a byte that changed is reported as LockMismatch and
# no engine opens that file.
dotnet run -c Release --project .\benchmarks\SharpCorePDF.Competitive -- `
  --skip-performance --robustness <folder> --robustness-lock <manifest.json> --write-robustness-lock
dotnet run -c Release --project .\benchmarks\SharpCorePDF.Competitive -- `
  --skip-performance --robustness <folder> --robustness-lock <manifest.json> --robustness-limit 50
```

Useful switches: `--skip-correctness`, `--skip-performance`, `--warmups`, `--iterations`,
`--capture` (one unmeasured create/merge PDF per writer), `--robustness <folder>`,
`--robustness-lock <manifest.json>`, `--write-robustness-lock`.
`--engine <name>` measures one adapter in a process of its own.

For a number that goes into release notes, run each engine in its own process under server GC, then
read the two reports side by side:

```powershell
$env:DOTNET_gcServer = '1'
dotnet run -c Release --project .\benchmarks\SharpCorePDF.Competitive -- --output artifacts\cite-sc  `
  --warmups 2 --iterations 5 --skip-correctness --engine SharpCorePDF
dotnet run -c Release --project .\benchmarks\SharpCorePDF.Competitive -- --output artifacts\cite-pig `
  --warmups 2 --iterations 5 --skip-correctness --engine PdfPig
```

## Scenarios

| Scenario | Engines | Work asserted |
| --- | --- | --- |
| Labelled extraction | SharpCorePDF, PdfPig | Characters, words, boxes and reading order against `tests/.../parity/labelled` |
| `text-200-all` | SharpCorePDF, PdfPig | All pages of a 200-page generated text document (cold open each sample) |
| `text-200-all-warm` | SharpCorePDF, PdfPig | Same document via a reusable open (`IPdfLayoutSession` / PdfPig document) |
| `text-200-pages-1-10` | SharpCorePDF, PdfPig | Pages 1–10 of the same document |
| `create-10x20-truetype` | SharpCorePDF, PdfPig, PDFsharp | Ten A4 pages, twenty lines, same supplied TrueType bytes and text |
| `merge-10x10` | SharpCorePDF, PdfPig, PDFsharp | Ten generated ten-page sources |
| `text-200-all-nearest-neighbour-alt` | PdfPig only | Named alternate word rule; its work counts differ from the default row by design |
| `text-200-all-blocks-docstrum` | SharpCorePDF, PdfPig | Block segmentation: each engine's Docstrum over the same document; work is blocks and the words inside them |
| External robustness | SharpCorePDF, PdfPig | Open + word extract on a caller-supplied, hash-lockable folder (opt-in) |

Performance rows report median/p95/min–max wall time, median allocated bytes, an approximate managed
peak from a 1 ms whole-process sampler, retained heap after a full collection, and the work summary
so a run that does less work cannot look "faster". Engines are interleaved and order is reversed on
alternate iterations. Warm-ups sit outside the measured samples.
## Reading the correctness numbers

- **Character recall** matches inked characters one-to-one by page, text and nearest geometry.
  Duplicate emissions cannot raise the score.
- **Word precision / recall / F1** match non-whitespace words the same way. Extra splits lower
  precision; missing words lower recall.
- **Box agreement (label convention)** compares character boxes to the label files baseline-box
  convention (`boxConvention` in each JSON). SharpCorePDF follows that convention on these fixtures;
  PdfPig reports ink glyph rectangles and scores 0% on this column even when it found every character.
- **Box overlap (IoU)** is the second, convention-free column: intersection-over-union of each matched
  character box against its label box, averaged. A different convention shrinks it; a missed character
  zeroes it. On the current labelled corpus PdfPig scores **40.83%** here against 0% on the label
  column — which is what separates a coordinate convention from a miss.
- **Reading order** is concordant matched-word pairs. The labelled `table` document is still scored
  here; the default order is column-aware, and the product's layout suite records the column-by-column
  table read as a characterisation. `PdfReadingOrderStrategy.RowBands` is the opt-in that reads a table
  row by row (asserted by `Table_page_reads_row_by_row_with_the_row_band_order`), so the limitation is a
  default, not a missing capability
  separately in `docs/parity.md`.

## Baseline run (local, 2026-11-05, post zero-copy + session + WordExtractor + font cache + scanner reuse)

Environment: .NET 10.0.14, Windows x64, 12 logical processors, workstation GC.
Warm-ups 2, measured iterations 10. **Note on noise:** this workstation shows large run-to-run variance
(PdfPig cold full-document has read 65–282 ms median across runs). Engines are interleaved inside a run,
so within-run medians are comparable; between-run absolute times are not. Allocation medians are the
stable leg of the comparison.

Engines: SharpCorePDF `1.4.0+a08cc3fd…` (tree under test), PdfPig `0.1.16+a7bb356…`, PDFsharp `6.5.0+9c879a4…`.

### Correctness (five labelled documents; unchanged shape)

| Engine | Character recall | Word F1 | Box agreement (label convention) | Box overlap (IoU) | Reading order |
| --- | ---: | ---: | ---: | ---: | ---: |
| SharpCorePDF | 100% | 90.20% | 100% | 100% | 100% |
| PdfPig | 100% | 90.20% | 0% (ink box vs baseline box) | 40.83% | 100% |

### Performance (median of 10, within-run)

| Scenario | SharpCorePDF | PdfPig | PDFsharp | Equal work? |
| --- | ---: | ---: | ---: | --- |
| text-200-all (cold) | 108 ms / 34 MB alloc | **74 ms** / 54 MB alloc | n/a | Yes: 200 pages, 16 400 words, 69 492 letters |
| text-200-all-warm | 68 ms / 34 MB | **57 ms** / 54 MB | n/a | Yes; open reused inside the sample |
| text-200-pages-1-10 | **1.1 ms** / 2.5 MB | 2.9 ms / 3.9 MB | n/a | Yes: 10 pages, 820 words |
| create-10x20-truetype | **5.5 ms** / 0.8 MB / 80 534 B out | 21 ms / 9.7 MB / 11 805 B out | 7.1 ms / 1.1 MB / 11 950 B out | Same input; output size differs by design |
| merge-10x10 | **2.6 ms** / 1.6 MB / 75 820 B / 100 pages | 30 ms / 22 MB / 80 715 B | 11 ms / 2.6 MB / 79 594 B / 100 pages | Same ten inputs |

### What the perf slices have done so far, measured

1. **Cold full-document extract ~4.3× faster than the smoke baseline** (467 → 108 ms median, equal work)
   from zero-copy open, a letter-lite `WordExtractor`, per-read font caching, a single-instance text
   scanner, one-byte width-table lookup, a shared per-read glyph list (no per-page `AddRange`) and a
   copy-free glyph result. **SharpCorePDF allocations on extract are ~36% below PdfPig** (34 MB vs 54 MB)
   — the stable metric.
2. **PdfPig still leads cold and warm full-document wall time** here (74/57 ms vs 108/68 ms) — warm is
   now within ~1.2×. Cross-run medians swing on this workstation (PdfPig cold has read 65–131 ms); the
   within-run ratio and the allocation column are the stable reads.
3. **Pages 1–10 cold leads consistently** (~1.1 ms vs ~2.9 ms) with equal work.
4. **Create and merge leads hold**, full-embed vs subset size caveat on create.
5. **Correctness unchanged** (tied on characters/words/order; box convention still unfair to PdfPig).

### Release-cite isolation (per-engine process, server GC)

Each engine in its own process with DOTNET_gcServer=1, 2 warm-ups / 5 iterations, run 2026-11-06:

| Scenario | SharpCorePDF | PdfPig |
| --- | ---: | ---: |
| text-200-all (cold) | **75.9 ms** / 35 MB | 201.7 ms / 55 MB |
| text-200-all-warm | 48.8 ms / 35 MB | 45.1 ms / 56 MB |
| text-200-all-blocks-docstrum | **74.2 ms** / 38 MB | 199.7 ms / 138 MB |

Server GC changes the picture enough that it is the mode a release note must name: warm extraction is
within a few milliseconds of parity here, cold extraction and block segmentation lead, and PdfPig is block
row allocates 3.6x what its word row does. Work counts remain equal on every row.

### Block segmentation (Docstrum, workflow GC, 1 warm-up / 3 iterations)

| Engine | Blocks | Words in blocks | Median ms | Allocated |
| --- | ---: | ---: | ---: | ---: |
| SharpCorePDF | 200 | 16 400 | **117.9** | 38 MB |
| PdfPig | 200 | 16 400 | 198.3 | 138 MB |

Equal segmentation output (200 blocks, 16 400 words) on both sides: this is a block-producing comparison,
not a word row, and PdfPig is Docstrum costs about 3.6x the memory of its own word path.

### Local robustness sample (synthetic, from repository assets)

The script benchmarks/SharpCorePDF.Competitive/scripts/sample-corpus.ps1 rebuilds a deterministic sample (13 PDFs: the labelled parity
fixtures plus any generated fixture or capture already under artifacts/) and pins it with a SHA-256 lock.
Latest run: **SharpCorePDF 13/13 completed, PdfPig 13/13 completed**, no errors or timeouts; median
per-file open and extract 15.3 ms vs 54.8 ms. This is a synthetic sample and is not an external corpus:
a genuinely external subset stays operator-supplied, hash-locked and uncommitted.

### Captured writer validation

Unmeasured captures under `artifacts/competitive/captures/` — re-check with `docs/tools/validate-pdf.py`
before a release cite.

## What the numbers say about "beating" the competitors

1. **PdfPig still leads full-document extraction wall time (cold and warm)** on the generated fixture.
   SharpCorePDF leads allocation economy on the same rows. Both are real findings, not slogans.
2. **SharpCorePDF leads the 10-page cold slice, create, and merge** on wall time and allocations here.
3. **Correctness on the labelled corpus remains tied** with PdfPig on characters, words and order.
   The dual box score now shows why the box columns disagreed: PdfPig overlaps the labels by 40.83% under
   a convention-free IoU while scoring 0% against the label baseline box.
4. **No external-corpus robustness row yet.** Do not claim real-world parser superiority until a
   hash-locked opt-in corpus is scored.
5. **PDFsharp's licence/framework reach and PdfPig's broader layout toolkit** remain product comparisons
   outside these scenarios (`docs/parity.md`).

## Next measurements to add

- A PdfPig Docstrum **block-level** row (its Docstrum is a page segmenter, not a word rule, so it needs
  a blocks scenario rather than a word row).
- ~~Remaining per-code decode cuts~~ **closed as exhausted (2026-11-06)**: caching the text state per show
  and testing the space without a string compare left allocation medians byte-identical (35 028 KiB) and
  moved cold extraction inside its own noise band (71.5-95.8 ms across runs, before: 75.9). The remaining
  cost is the per-glyph record itself, which is the product contract, not waste. What the attempt did
  surface is a real test gap: character spacing, word spacing and horizontal scaling had no assertion, so
  `PdfTextCollector` scored 41.9% under mutation. A spacing fixture and test now pin them (46.6%).
- A genuinely **external** corpus sample (Common Crawl / SAFEDOCS subset) for the hash-locked leg. The
  mechanism and a synthetic local sample (13/13 both engines) are in place; the external bytes stay
  operator-supplied, hash-locked and uncommitted.
- ~~A multi-cell table fixture~~ **landed (2026-11-06)**: three tests pin the row-band order on a real
  two-column, three-row page — the default segmentation merges tightly-led rows into one block per column
  (documented in the fixture), the raw segmentation keeps each string a block, and a touching row whose
  left edge contradicts the row above proves the band comparison is strict. `ReadingOrderer` mutation
  score 64.7% -> 67.7%; two survivors remain and are recorded as shape limits rather than gaps: a band
  whose blocks differ in height with a later block between the two bottoms (needs mixed font sizes in one
  band), and two blocks at identical x inside one band (the default segmentation merges those rows before
  the order ever sees them).

## Governance note

The original parity research preferred not to reference competitor packages inside the product suite.
That rule still holds for `src/` and `tests/`. This harness is the deliberate exception: a separate
restore boundary, lock file, third-party notice, and gitignored outputs. If redistributing a project
that links PDFsharp is unacceptable for a given organisation, run that adapter externally and keep
only methodology and numbers in this repository.