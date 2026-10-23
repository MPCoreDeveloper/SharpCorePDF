"""Repeats the cover-page evidence of Gate 0 (C1–C3) and prints a markdown transcript.

Run with:  uv run --with pikepdf --with pypdf --with pymupdf python docs/tools/verify-cover.py
           ... python docs/tools/verify-cover.py --output artifacts\c5-transcript.md
           ... python docs/tools/verify-cover.py <cover.pdf> <merged.pdf>

The first form runs the `author-page` example in `Release`, reads the two files it writes and checks
them; the second does the same and also writes the transcript to a UTF-8 file, which is what a pull
request wants — a PowerShell pipe re-decodes the output and mangles `×` and `—`; the third form checks
files that already exist, which is what a release-commit re-run needs.

Three independent implementations are asked the three questions the gate asks:

* **C1** — `validate-pdf.py` (pikepdf 10, which runs `qpdf --check`): no problems, every page readable,
  every stream decodable, nothing unreachable.
* **C2** — `pypdf` with `strict=True`: the A4 MediaBox, the accented line unchanged, and the image
  XObject as 152 × 87 with an `/SMask`.
* **C3** — PyMuPDF at 72 dpi: the logo's colour inside its box and white outside it, so the page is not
  merely *declared* to carry an image.

Every expectation below is a value recorded on 2026-10-22 in `docs/measurements.md` §8. The tool exits
non-zero if one of them changes, so "the evidence still holds" is a command and not a memory. It writes
nothing to the repository: the transcript goes to standard output, ready to paste into the pull request
that `T104` asks for.
"""

from __future__ import annotations

import datetime
import importlib.util
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

import pikepdf
import pymupdf
import pypdf

ROOT = Path(__file__).resolve().parents[2]
EXAMPLES = ROOT / "examples" / "SharpCorePDF.Examples"

# What the gate recorded, and therefore what a re-run has to reproduce.
PAGE_WIDTH = 595.276
PAGE_HEIGHT = 841.890
PAGE_TOLERANCE = 0.01
COVER_PAGES = 1
MERGED_PAGES = 3
IMAGE_WIDTH = 152
IMAGE_HEIGHT = 87
ACCENTED_LINE = "Référence : é è à ç ô ï ' / |"
LOGO_COLOUR = (31, 58, 95)
WHITE = (255, 255, 255)
LOGO_POINTS = ((100, 45), (150, 60))
BLANK_POINTS = ((50, 45), (30, 200))

# What the example prints, and what the transcript quotes from it.
TIMING = re.compile(r"ok \((\d+) ms\)")
WRITTEN = re.compile(r"^\s+wrote\s+(?P<path>.+\.pdf)\s*$", re.MULTILINE)
MERGED = re.compile(r"^\s+merged\s+(?P<path>.+\.pdf)\s*$", re.MULTILINE)
BYTES = re.compile(r"^\s+bytes\s+(?P<bytes>\d+)\s*$", re.MULTILINE)


@dataclass
class ExampleRun:
    """What the `author-page` example said about the two files it wrote."""

    cover: Path
    merged: Path
    cover_bytes: int
    milliseconds: int


@dataclass
class PdfFacts:
    """What the three readers said about one file."""

    path: Path
    pages: int = 0
    objects: int = 0
    streams: int = 0
    undecodable: int = 0
    problems: list[str] = field(default_factory=list)
    media_box: tuple[float, float] = (0.0, 0.0)
    images: list[tuple[str, int, int, bool]] = field(default_factory=list)
    text: list[str] = field(default_factory=list)
    pixmap: tuple[int, int] = (0, 0)
    pixels: dict[tuple[int, int], tuple[int, int, int]] = field(default_factory=dict)


def run_example() -> ExampleRun:
    """Runs the example and returns the paths it wrote, or fails loudly."""
    command = ["dotnet", "run", "-c", "Release", "--project", str(EXAMPLES), "--", "author-page"]
    completed = subprocess.run(
        command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace"
    )

    if completed.returncode != 0:
        print(completed.stdout)
        print(completed.stderr, file=sys.stderr)
        raise SystemExit(f"the example exited with {completed.returncode}")

    written = WRITTEN.search(completed.stdout)
    merged = MERGED.search(completed.stdout)

    if written is None or merged is None:
        raise SystemExit("the example did not report the two files it wrote")

    timing = TIMING.search(completed.stdout)
    size = BYTES.search(completed.stdout)

    return ExampleRun(
        cover=Path(written.group("path").strip()),
        merged=Path(merged.group("path").strip()),
        cover_bytes=int(size.group("bytes")) if size else 0,
        milliseconds=int(timing.group(1)) if timing else 0,
    )


def load_validator():
    """Loads `validate-pdf.py`, so C1 here is the same code path as C1 by hand."""
    spec = importlib.util.spec_from_file_location(
        "validate_pdf", ROOT / "docs" / "tools" / "validate-pdf.py"
    )
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)

    return module


def read_facts(path: Path, validator) -> PdfFacts:
    """Asks pikepdf/qpdf, pypdf and PyMuPDF the questions C1, C2 and C3 ask."""
    facts = PdfFacts(path=path)
    document = pikepdf.open(path)

    facts.problems = validator.run_qpdf_check(path)
    facts.pages = len(document.pages)
    facts.objects = len(document.objects)
    facts.streams, facts.undecodable = validator.check_streams(document)
    page_media_box = document.pages[0].MediaBox
    facts.media_box = (round(float(page_media_box[2]), 3), round(float(page_media_box[3]), 3))

    page = pypdf.PdfReader(str(path), strict=True).pages[0]
    resources = page.get("/Resources") or {}

    for name, reference in (resources.get("/XObject") or {}).items():
        image = reference.get_object()

        if image.get("/Subtype") == "/Image":
            facts.images.append(
                (str(name), int(image["/Width"]), int(image["/Height"]), "/SMask" in image)
            )

    facts.text = page.extract_text().splitlines()

    pixmap = pymupdf.open(path)[0].get_pixmap(dpi=72)
    facts.pixmap = (pixmap.width, pixmap.height)

    for point in LOGO_POINTS + BLANK_POINTS:
        facts.pixels[point] = tuple(pixmap.pixel(*point))

    return facts


def compare(facts: PdfFacts, expected_pages: int) -> list[str]:
    """Returns the expectations the file breaks, in the gate's own terms."""
    failures = list(facts.problems)
    width, height = facts.media_box

    if facts.undecodable:
        failures.append(f"{facts.path.name}: {facts.undecodable} stream(s) did not decode")

    if facts.pages != expected_pages:
        failures.append(f"{facts.path.name}: {facts.pages} page(s), not {expected_pages}")

    if abs(width - PAGE_WIDTH) > PAGE_TOLERANCE or abs(height - PAGE_HEIGHT) > PAGE_TOLERANCE:
        failures.append(
            f"{facts.path.name}: page 1 is {width} × {height} pt, not {PAGE_WIDTH} × {PAGE_HEIGHT}"
        )

    masked = [entry for entry in facts.images if entry[1:] == (IMAGE_WIDTH, IMAGE_HEIGHT, True)]

    if not masked:
        failures.append(f"{facts.path.name}: no {IMAGE_WIDTH} × {IMAGE_HEIGHT} image with /SMask")

    if ACCENTED_LINE not in facts.text:
        failures.append(f"{facts.path.name}: the accented line is not intact")

    for point in LOGO_POINTS:
        if facts.pixels[point] != LOGO_COLOUR:
            failures.append(
                f"{facts.path.name}: pixel {point} is {facts.pixels[point]}, not {LOGO_COLOUR}"
            )

    for point in BLANK_POINTS:
        if facts.pixels[point] != WHITE:
            failures.append(f"{facts.path.name}: pixel {point} is {facts.pixels[point]}, not white")

    return failures


def describe(facts: PdfFacts) -> list[str]:
    """Returns the transcript lines for one file."""
    width, height = facts.media_box
    pixmap_width, pixmap_height = facts.pixmap
    images = ", ".join(
        f"{name} {image_width} × {image_height}{' with /SMask' if mask else ''}"
        for name, image_width, image_height, mask in facts.images
    )
    pixels = ", ".join(f"{point} = {facts.pixels[point]}" for point in LOGO_POINTS + BLANK_POINTS)

    return [
        f"* `{facts.path.name}`",
        f"  * qpdf check: {'no problems' if not facts.problems else '; '.join(facts.problems)}",
        f"  * pages: {facts.pages}, objects: {facts.objects}, "
        f"streams: {facts.streams} decoded, {facts.undecodable} failed",
        f"  * page 1 MediaBox: {width} × {height} pt",
        f"  * image XObject: {images or 'none'}",
        f"  * page 1 text: {len(facts.text)} lines, accented line intact: "
        f"{ACCENTED_LINE in facts.text}",
        f"  * pixmap at 72 dpi: {pixmap_width} × {pixmap_height}; {pixels}",
    ]


def main() -> None:
    sys.stdout.reconfigure(encoding="utf-8")
    arguments = sys.argv[1:]
    output = None

    if "--output" in arguments:
        index = arguments.index("--output")
        output = Path(arguments[index + 1])
        del arguments[index : index + 2]

    if arguments:
        run = None
        cover, merged = Path(arguments[0]), Path(arguments[1])
    else:
        run = run_example()
        cover, merged = run.cover, run.merged

    validator = load_validator()
    cover_facts = read_facts(cover, validator)
    merged_facts = read_facts(merged, validator)
    failures = compare(cover_facts, COVER_PAGES) + compare(merged_facts, MERGED_PAGES)

    sdk = subprocess.run(["dotnet", "--version"], capture_output=True, text=True).stdout.strip()
    lines = [
        f"## C1–C3 transcript — cover page, {datetime.date.today().isoformat()}",
        "",
        "```powershell",
        "uv run --with pikepdf --with pypdf --with pymupdf python docs/tools/verify-cover.py",
        "",
        "dotnet run -c Release --project .\\examples\\SharpCorePDF.Examples -- author-page",
        "uv run --with pikepdf python docs/tools/validate-pdf.py <cover.pdf> <merged.pdf>",
        "uv run --with pypdf python -c \"from pypdf import PdfReader; ...\"",
        "uv run --with pymupdf python -c \"import pymupdf; ...\"",
        "```",
        "",
        f"Versions: .NET SDK {sdk}; Python {sys.version.split()[0]}; "
        f"pikepdf {pikepdf.__version__} (qpdf inside); pypdf {pypdf.__version__}; "
        f"PyMuPDF {pymupdf.__version__}.",
        "",
    ]

    if run is not None:
        lines += [
            f"The example reported `ok ({run.milliseconds} ms)` for the build, the merge and the "
            f"read-back, and wrote an {run.cover_bytes:,}-byte `{run.cover.name}` and a "
            f"{MERGED_PAGES}-page `{run.merged.name}`.",
        ]
    else:
        lines += ["Both files were checked as they are; the example was not run."]

    lines += [""]
    lines += [line for facts in (cover_facts, merged_facts) for line in describe(facts)]
    lines += [
        "",
        f"**Verdict**: {'pass' if not failures else 'FAIL'} — "
        f"{len(failures)} expectation(s) broken.",
    ]
    lines += [f"* {failure}" for failure in failures]
    transcript = "\n".join(lines)

    print(transcript)

    if output is not None:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(transcript + "\n", encoding="utf-8")
        print(f"\nTranscript written to {output}.")

    sys.exit(0 if not failures else 1)


if __name__ == "__main__":
    main()
