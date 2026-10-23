# ADR-002: Release 1.1.0 with its new-code coverage recorded at 77.76%

- **Status**: Proposed — the release owner signs this before `v1.1.0` is tagged
- **Date**: 2026-10-23
- **Related**: T103 and T106 in [`specs/003-pdf-platform-parity/tasks.md`](../../specs/003-pdf-platform-parity/tasks.md);
  G9 in [`docs/release-checklist.md`](../release-checklist.md); [`docs/tools/coverage-on-new-code.py`](../tools/coverage-on-new-code.py)

## Context

The release gate requires **90% coverage on new code** (`docs/release-checklist.md` G9, measured locally by
`docs/tools/coverage-on-new-code.py`; the SonarCloud quality gate, which does not carry this number, runs
`Sonar way`'s own 80). T103 asks the tree that question locally, before a service is consulted: one
instrumented Release run on `net11.0`, merged to a Visual Studio coverage XML, intersected
with the lines `git diff --unified=0 v1.0.0 -- src` adds, by `docs/tools/coverage-on-new-code.py`.

The answer is **6,842 of 8,799 executable added lines covered — 77.76%**, against a floor of 90%. It is not a
measurement artefact: two independent runs of the same tree agreed byte for byte on the earlier figure, and
the number moved only when tests were added (6,718 → 6,817 → 6,841 as 743 → 748 tests, 76.36% → 77.76%). The
final line is the one the quality-gate fix in this release added, and it is covered, so the percentage does not
move. The 1,957 uncovered lines are not spread thin; they sit in the parsers and validators 1.1 introduced:

| File | Covered | Added lines | Uncovered |
| --- | ---: | ---: | ---: |
| `TrueTypeFont` | 139 | 233 | 94 |
| `PdfTextScanner` | 173 | 266 | 93 |
| `SignatureValidator` | 141 | 226 | 85 |
| `PdfARequirements` | 113 | 197 | 84 |
| `PdfContentScanner` | 256 | 330 | 74 |
| `PdfPageContentReader.Images` | 215 | 286 | 71 |
| `PdfImageFilters` | 202 | 270 | 68 |
| `PdfTextCollector` | 172 | 239 | 67 |

What the suite covers is behaviour: the writer, the editor, the reader, the signer and the PDF/A route, with a
named test per parity row and an external command (pyHanko, qpdf, veraPDF 1.30.3, pypdf, PyMuPDF) run against
the output. What it does not cover is the refusal and malformed-input branch of each parser — cmap formats 0,
6 and 12 and broken table offsets, inline images that are unterminated or use abbreviated entries, a `/Contents`
that is not a CMS structure, a PDF/A requirement that cannot be met. Reaching those needs a hand-built
malformed document per branch, which is a story of its own rather than an afternoon's work. Writing tests whose
only effect is to execute a line would move the number and not the evidence, which is the failure mode the
gate exists to prevent.

T103 did add the five tests that pay for themselves: the inline-image abbreviations a producer actually writes
(computed sample count, indexed colour space, image mask with a decode array, an ASCIIHex payload whose length
the header does not state, and an image with no `EI`), and the signing pair reached through
`AddSharpCorePDF()` — sign, then validate, then the three arguments the signer refuses. That is 123 lines and
5 tests; it is not 1,957.

## Decision

1. **`1.1.0` is released with 77.76% recorded.** The number appears in this record, in G9 and in the release
   notes. T103's checkbox is closed by the *signed* record rather than by the threshold being met: the
   exception is measured and named, not waived, and `tasks.md` points here for the number.
2. **The floor is not lowered.** 90% remains the number 1.2 is measured against, and this record is not a new
   baseline for it.
3. **No test is added to move the number.** A test that asserts nothing about behaviour does not count toward
   it, now or later; the gap closes by testing the refusal branches with hand-built inputs.
4. **The work that closes it is a 1.2 task** covering the files in the table above, in that order. 1.2's T103
   re-runs the same command, and this record is superseded by the result.

## Consequences

- The release states a number a consumer can read, instead of a number a reader would have to reproduce to
  discover. Nothing in the shipped behaviour changes.
- SonarCloud's own `new_coverage` is a *different* measurement over a different denominator: its floor is 80, it
  reported **83.5%** for this release's new code, and `publish.yml` reads that verdict rather than this figure.
  The two can disagree without either being wrong; G9 explains why, and this record does not claim the service
  will report 78%.
- 1.2 starts with a debt that is named, ordered and testable rather than an unexplained dip in a dashboard.

## Alternatives considered

1. **Write the uncovered lines' worth of tests before the tag.** Rejected: the ~1,950 lines are 8 parsers'
   error branches; a week of hand-built malformed fixtures, written under release pressure, produces tests
   that are as likely to encode the current bug as the current intent.
2. **Lower the floor to 78%.** Rejected: a floor that follows the measurement is not a floor, and the next
   release would inherit 78% as its target.
3. **Hold 1.1.0 until the coverage exists.** Rejected: holding adds no coverage — the work is the same work —
   and it delays the behaviour the release does evidence (authoring, editing, layout reading, signatures,
   PDF/A) behind a number that this record already states.
4. **Publish the number without a record.** Rejected: a release that quietly misses its own gate teaches the
   next release that the gate is advisory.

## Approval

| Role | Name | Date |
| --- | --- | --- |
| Release owner | | |

Until this table is signed, the status stays **Proposed**, T103 stays open with the decision named, and T106
does not run: no tag is cut on an unsigned waiver.
