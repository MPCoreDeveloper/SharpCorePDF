"""Build the two corpus shapes the external sample does not contain: a rasterised scan of a real
document, and a form whose XFA payload is broken.

Run once from the repository root, with the corpus already present:

    uv run --with pikepdf --with pymupdf python docs/tools/make_corpus_shapes.py

The outputs are written beside the corpus in artifacts/ and stay uncommitted, like the rest of it. They
are derived documents rather than captured ones, which is why their names say so.
"""

from pathlib import Path

import pikepdf
import pymupdf


CORPUS = Path("artifacts/robustness-external")
SCAN = CORPUS / "scan-rasterised-f1040.pdf"
BROKEN_XFA = CORPUS / "xfa-broken-i9.pdf"


def rasterise(source: Path, destination: Path) -> None:
    """Render page 1 of a document at 150 dpi and keep only that image in a new PDF."""
    with pymupdf.open(source) as document:
        page = document[0]
        width, height = page.rect.width, page.rect.height
        image = page.get_pixmap(dpi=150).tobytes("png")

    with pymupdf.open() as output:
        target = output.new_page(width=width, height=height)
        target.insert_image(target.rect, stream=image)
        output.save(destination, garbage=4, deflate=True)


def break_xfa(source: Path, destination: Path) -> None:
    """Give a form an /XFA entry whose payload is not XML, which is what a damaged XFA form looks like."""
    with pikepdf.open(source) as document:
        root = document.Root
        if "/AcroForm" not in root:
            raise SystemExit(f"{source} carries no AcroForm to break")

        payload = b"<< this is not an XFA packet >>"
        stream = document.make_stream(payload)
        root["/AcroForm"]["/XFA"] = document.make_indirect(stream)

        document.save(destination)


if __name__ == "__main__":
    rasterise(CORPUS / "irs-f1040.pdf", SCAN)
    break_xfa(CORPUS / "uscis-i9.pdf", BROKEN_XFA)

    for path in (SCAN, BROKEN_XFA):
        print(f"{path.name}: {path.stat().st_size} bytes")
