"""Renders every page with two engines that share no code, PDFium and MuPDF, and compares the pictures.

Run with:  python docs/tools/render-compare.py [--report-dir <dir>] [--dpi 96] [--exclude <name>] <file.pdf | dir>...
           (after: uv pip install -r docs/tools/validation-requirements.txt)

qpdf proves a file parses; it does not prove two viewers paint the same page. An overlay drawn outside its
q/Q frame, a transparency group one engine composites and the other ignores, an appearance stream with a
wrong /BBox or /Matrix, a clipping path that one engine reads differently, a font one engine cannot load:
each of those passes every structural check and still shows a different page in Chrome (PDFium) than in a
MuPDF-based viewer.

The engines rasterise edges differently by design, so a plain pixel difference is mostly anti-aliasing: on
the example output it reaches 1.4% of a page while a page that lost all its annotations differs by only 0.6%.
Two measures are used instead, each blind to anti-aliasing and each sensitive to one kind of loss:

  solid regions  the pixels that differ by more than 48 levels of 255, after the mask is eroded by a 5 x 5
                 minimum filter: edge noise is one or two pixels wide and vanishes, a missing or displaced
                 shape survives. A page fails above 200 such pixels.
  darkness       the total ink of each rendering (the sum of 255 minus every pixel). Thin text dies in the
                 erosion but not here. A page fails when the two totals differ by more than 20% and by more
                 than 1000 fully black pixels' worth, the second floor keeping near-empty pages out of it.

Calibrated on the examples' 37 pages and the corpus at 96 dpi: agreeing pages stay at 0 solid pixels and at
most 9.3% darkness (translucent highlights are composited slightly differently); a page with its annotations
dropped, or a watermark missing, differs by 50% darkness. A page also fails when exactly one engine paints
nothing. With --report-dir, both renderings and the difference image of every failing page are written there.

No golden image is stored: the engines are compared with each other, so a release of either one is noticed
as a disagreement rather than silently moving a baseline.
"""

import argparse
import sys
from pathlib import Path

DIFFERENCE = 48


def render_pdfium(path: Path, dpi: int) -> list:
    import pypdfium2 as pdfium

    document = pdfium.PdfDocument(str(path))

    # PDFium draws form widgets only once the form environment exists, which is what Chrome sets up too.
    document.init_forms()

    try:
        return [page.render(scale=dpi / 72, may_draw_forms=True).to_pil().convert("L") for page in document]
    finally:
        document.close()


def render_mupdf(path: Path, dpi: int) -> list:
    import pymupdf
    from PIL import Image

    images = []

    with pymupdf.open(str(path)) as document:
        for page in document:
            pixmap = page.get_pixmap(dpi=dpi, colorspace=pymupdf.csGRAY, annots=True)
            images.append(Image.frombytes("L", (pixmap.width, pixmap.height), pixmap.samples))

    return images


SOLID_PIXELS = 200
DARKNESS_SHARE = 0.20
DARKNESS_FLOOR = 1000 * 255


def compare(first, second) -> tuple[int, float, float, bool]:
    """Returns the solid differing pixels, the darkness share, the darkness gap and whether one side is blank."""
    import numpy as np
    from PIL import Image, ImageFilter

    if first.size != second.size:
        second = second.resize(first.size)

    a = np.asarray(first, dtype=np.int32)
    b = np.asarray(second, dtype=np.int32)

    mask = Image.fromarray(((np.abs(a - b) > DIFFERENCE) * 255).astype(np.uint8))
    solid = int((np.asarray(mask.filter(ImageFilter.MinFilter(5))) > 0).sum())

    ink_a, ink_b = float((255 - a).sum()), float((255 - b).sum())
    gap = abs(ink_a - ink_b)
    share = gap / max(ink_a, ink_b, 1.0)
    blank_a, blank_b = bool((a > 250).all()), bool((b > 250).all())

    return solid, share, gap, blank_a != blank_b


def check(path: Path, dpi: int, report: Path | None) -> int:
    print(f"== {path.name}")

    try:
        pdfium_pages = render_pdfium(path, dpi)
        mupdf_pages = render_mupdf(path, dpi)
    except Exception as error:  # noqa: BLE001 - an engine that cannot open the file is the finding
        print(f"  FAILED to render: {type(error).__name__}: {error}")
        return 1

    if len(pdfium_pages) != len(mupdf_pages):
        print(f"  FAILED: PDFium renders {len(pdfium_pages)} page(s), MuPDF {len(mupdf_pages)}")
        return 1

    failures = 0

    for number, (first, second) in enumerate(zip(pdfium_pages, mupdf_pages), start=1):
        solid, share, gap, one_blank = compare(first, second)
        failed = one_blank or solid > SOLID_PIXELS or (share > DARKNESS_SHARE and gap > DARKNESS_FLOOR)
        verdict = "FAILED" if failed else "agree"
        note = ", one engine painted nothing" if one_blank else ""
        print(f"  page {number}: {solid} solid differing pixel(s), darkness differs by {share:.1%}{note} - {verdict}")

        if failed:
            failures += 1

            if report is not None:
                from PIL import ImageChops

                report.mkdir(parents=True, exist_ok=True)
                stem = f"{path.stem}-p{number}"
                first.save(report / f"{stem}-pdfium.png")
                second.resize(first.size).save(report / f"{stem}-mupdf.png")
                ImageChops.difference(first, second.resize(first.size)).save(report / f"{stem}-difference.png")

    return failures


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="+")
    parser.add_argument("--dpi", type=int, default=96)
    parser.add_argument("--report-dir", type=Path)
    parser.add_argument("--exclude", action="append", default=[], help="a file name to leave out, e.g. an encrypted one")
    options = parser.parse_args()

    files = []

    for argument in options.paths:
        path = Path(argument)
        files.extend(sorted(path.glob("*.pdf")) if path.is_dir() else [path])

    failures = 0

    for path in files:
        if path.name not in options.exclude:
            failures += check(path, options.dpi, options.report_dir)

    print()
    print("rendering: " + ("PDFium and MuPDF agree on every page" if failures == 0 else f"{failures} page(s) disagree"))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
