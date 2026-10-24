"""Repeats the `creator-parity` evidence of T038 and prints a markdown transcript.

Run with:  uv run --with pikepdf --with pypdf --with pymupdf python docs/tools/verify-creator.py
           ... python docs/tools/verify-creator.py <creator-parity.pdf>
           ... python docs/tools/verify-creator.py --output artifacts\\creator-transcript.md

The first form runs the `creator-parity` example in `Release`, reads the file it writes and checks it;
the second checks a file that already exists, which is what a re-run on a release commit needs. The
transcript goes to standard output as UTF-8 (pass `--output` for a file: a PowerShell pipe re-decodes
the output and mangles `×` and `—`), so the pull request that T104 asks for can quote it.

Four independent implementations are asked what the document is:

* **qpdf**, through `pikepdf.Job` — no syntax problems.
* **pypdf** with `strict=True` — the accented line on page 1 unchanged, page 2's own text, and the text
  of the page the builder imported from another document.
* **PyMuPDF** — the bookmark tree with its nesting and its external entry, the XMP packet, the two image
  XObjects with their filters and colour spaces, and a rendered pixel inside the profiled logo.
* **pikepdf** again — the `/ICCBased` stream an `iCCP` chunk became, compared with the profile inside the
  PNG the example drew, byte for byte.

Every expectation is a value recorded on 2026-10-23 in `docs/measurements.md` §13. The tool exits
non-zero when one of them changes, so "the evidence still holds" is a command and not a memory.
"""

from __future__ import annotations

import argparse
import hashlib
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

import pikepdf
import pymupdf
import pypdf

ROOT = Path(__file__).resolve().parents[2]
EXAMPLE = ROOT / "examples" / "SharpCorePDF.Examples" / "SharpCorePDF.Examples.csproj"
PROFILED_PNG = ROOT / "examples" / "SharpCorePDF.Examples" / "Assets" / "Logo-icc.png"
DOCUMENT = Path(tempfile.gettempdir()) / "SharpCorePDF.Examples" / "output" / "creator-parity.pdf"


def profile_of_png(path: Path) -> bytes:
    """Reads the profile an `iCCP` chunk carries, decompressed."""
    data = path.read_bytes()
    position = 8

    while position + 12 <= len(data):
        length, kind = struct.unpack(">I4s", data[position : position + 8])

        if kind == b"iCCP":
            body = data[position + 8 : position + 8 + length]
            end = body.index(b"\0")
            assert body[end + 1] == 0, "the chunk states a compression method PNG does not define"
            return zlib.decompress(body[end + 2 :])

        position += 12 + length

    raise AssertionError(f"{path} carries no iCCP chunk")


class Check:
    """Collects the checks, prints them and decides the exit code."""

    def __init__(self) -> None:
        self.failures: list[str] = []

    def that(self, label: str, actual: object, expected: object) -> None:
        ok = actual == expected
        print(f"| {label} | `{actual}` | `{expected}` | {'ok' if ok else '**FAILED**'} |")

        if not ok:
            self.failures.append(f"{label}: {actual!r} != {expected!r}")


def check(document: Path) -> int:
    """Checks one authored document against the recorded readings."""
    print(f"## {document.name}\n")
    print(f"`{document}`\n")

    job = pikepdf.Job(["qpdf", "--check", str(document)])
    job.run()
    qpdf = "no problems" if job.exit_code == 0 else f"exit {job.exit_code}"

    reader = pypdf.PdfReader(str(document), strict=True)
    pdf = pikepdf.open(document)
    view = pymupdf.open(document)

    images = [
        (image[8], image[5], image[2], image[3])
        for page in view
        for image in page.get_images(full=True)
    ]

    # The image of page 2 is the profiled PNG; its colour space names a stream, which pikepdf resolves.
    profiles = []
    for page in pdf.pages:
        for image in (page.Resources.get("/XObject") or {}).values():
            space = image.get("/ColorSpace")
            if isinstance(space, pikepdf.Array) and space[0] == "/ICCBased":
                profiles.append(
                    (int(space[1].get("/N")), str(space[1].get("/Alternate")), space[1].read_bytes())
                )

    checks = Check()
    print("| What | Read | Expected | Verdict |")
    print("| --- | --- | --- | --- |")
    checks.that("qpdf --check", qpdf, "no problems")
    checks.that("pages", view.page_count, 3)
    checks.that(
        "page boxes",
        [tuple(round(value) for value in page.rect) for page in view],
        [(0, 0, 595, 842), (0, 0, 420, 595), (0, 0, 595, 842)],
    )
    checks.that(
        "bookmarks",
        [entry[1:3] for entry in view.get_toc()],
        [
            ["Couverture", 1],
            ["Sommaire", 2],
            ["Totaux", 2],
            ["Décompte du fournisseur", 3],
            ["SharpCorePDF", -1],
        ],
    )
    checks.that("xmp packet is present", len(view.get_xml_metadata()) > 0, True)
    checks.that("metadata title", view.metadata.get("title"), "Décompte 12345")
    checks.that("page 1 line", reader.pages[0].extract_text().splitlines()[0], "Décomptes SPRL")
    checks.that("page 2 line", reader.pages[1].extract_text().splitlines()[0], "Sommaire")
    checks.that(
        "page 3 is the imported page",
        reader.pages[2].extract_text().splitlines()[0],
        "Quarterly summary - page 1 of 2",
    )
    checks.that(
        "images",
        images,
        [("DCTDecode", "DeviceRGB", 320, 320), ("FlateDecode", "ICCBased", 152, 87)],
    )
    checks.that("profiles", [(entry[0], entry[1]) for entry in profiles], [(3, "/DeviceRGB")])

    # Compared by length and digest, so the transcript stays readable and still proves byte identity.
    expected = profile_of_png(PROFILED_PNG)
    found = profiles[0][2] if profiles else b""
    checks.that(
        "profile length and digest",
        (len(found), hashlib.sha256(found).hexdigest()),
        (len(expected), hashlib.sha256(expected).hexdigest()),
    )

    pixmap = view[1].get_pixmap(dpi=72)
    checks.that("page 2 render", (pixmap.width, pixmap.height), (420, 596))
    checks.that("pixel inside the profiled logo", pixmap.pixel(80, 170), (17, 31, 57))

    print()

    if checks.failures:
        print(f"{len(checks.failures)} check(s) changed:")
        for failure in checks.failures:
            print(f"* {failure}")
        return 1

    print("All checks hold.")
    return 0


def run(document: Path | None) -> int:
    """Builds the artifact when it was not supplied, then checks it."""
    if document is None:
        print("building the artifact: creator-parity\n", flush=True)
        subprocess.run(
            ["dotnet", "run", "-c", "Release", "--project", str(EXAMPLE), "--", "creator-parity"],
            check=True,
        )
        document = DOCUMENT

    if not document.exists():
        print(f"{document} does not exist; run the example first", file=sys.stderr)
        return 2

    return check(document)


def main() -> int:
    """Reads the arguments, optionally tees the transcript to a file, and runs the checks."""
    parser = argparse.ArgumentParser(description="Repeats the creator-parity evidence of T038.")
    parser.add_argument("document", nargs="?", type=Path, help="a creator-parity.pdf to check")
    parser.add_argument("--output", type=Path, help="write the transcript here instead of stdout")

    arguments = parser.parse_args()

    if not arguments.output:
        return run(arguments.document)

    with arguments.output.open("w", encoding="utf-8") as transcript:
        original = sys.stdout
        sys.stdout = transcript

        try:
            code = run(arguments.document)
        finally:
            sys.stdout = original

    print(f"transcript: {arguments.output}")
    return code


if __name__ == "__main__":
    sys.exit(main())
