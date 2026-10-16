"""Creates the small PDF fixtures used by the SharpCorePDF tests.

Run with:  uv run --with pikepdf python docs/tools/make-fixtures.py tests/SharpCorePDF.Tests/Assets

The fixtures are checked in; this script exists so they can be regenerated and so it is clear
exactly what they contain. Two shapes are deliberately covered:

* a document written with object streams and cross-reference streams (PDF 1.5 style), and
* a document with an outline, a page label tree, an annotation and a rotated page.
"""

import sys
from pathlib import Path

import pikepdf
from pikepdf import Array, Dictionary, Name, Pdf, String

A4 = [0, 0, 595.276, 841.89]
LETTER = [0, 0, 612, 792]

# The document information key every fixture sets, named once because a repeated literal is a spelling
# that can drift.
DOCINFO_TITLE = "/Title"


def blank_page(pdf: Pdf, box, rotate=0):
    page = pdf.add_blank_page(page_size=(box[2], box[3]))
    page.MediaBox = Array([box[0], box[1], box[2], box[3]])
    if rotate:
        page.Rotate = rotate
    page.Contents = pdf.make_stream(b"BT /F1 12 Tf 72 720 Td (page) Tj ET\n")
    return page


def make_modern(path: Path):
    """Two pages, written with object streams and a cross-reference stream."""
    pdf = Pdf.new()
    blank_page(pdf, A4)
    blank_page(pdf, A4, rotate=90)
    pdf.docinfo[DOCINFO_TITLE] = String("Modern sample")
    pdf.docinfo["/Author"] = String("SharpCorePDF")
    pdf.save(path, object_stream_mode=pikepdf.ObjectStreamMode.generate)
    print(f"wrote {path}")


def make_outlined(path: Path):
    """Three pages with an outline, page labels, an annotation and one rotated page."""
    pdf = Pdf.new()
    first = blank_page(pdf, LETTER)
    second = blank_page(pdf, LETTER)
    third = blank_page(pdf, LETTER, rotate=180)

    # A text annotation that points back at its page, as real-world files do.
    annotation = pdf.make_indirect(
        Dictionary(
            Type=Name.Annot,
            Subtype=Name.Text,
            Rect=Array([10, 10, 30, 30]),
            Contents=String("note"),
            P=first.obj,
        )
    )
    first.Annots = Array([annotation])

    # Bookmarks: two top level items, the second one with a child.
    child = pdf.make_indirect(
        Dictionary(
            Title=String("Child"),
            Dest=Array([second.obj, Name.XYZ, 0, 792, 0]),
        )
    )
    second_item = pdf.make_indirect(
        Dictionary(
            Title=String("Second"),
            Dest=Array([third.obj, Name.Fit]),
            First=child,
            Last=child,
            Count=1,
        )
    )
    child.Parent = second_item

    first_item = pdf.make_indirect(
        Dictionary(
            Title=String("First"),
            Dest=Array([first.obj, Name.Fit]),
            Next=second_item,
        )
    )
    second_item.Prev = first_item

    outlines = pdf.make_indirect(
        Dictionary(
            Type=Name.Outlines,
            First=first_item,
            Last=second_item,
            Count=3,
        )
    )
    first_item.Parent = outlines
    second_item.Parent = outlines
    pdf.Root.Outlines = outlines

    pdf.Root.PageLabels = pdf.make_indirect(
        Dictionary(
            Nums=Array([0, Dictionary(S=Name.r), 1, Dictionary(S=Name.D, St=1)]),
        )
    )

    pdf.docinfo[DOCINFO_TITLE] = String("Outlined sample")
    pdf.docinfo["/Producer"] = String("pikepdf")
    pdf.save(path)
    print(f"wrote {path}")


def make_encrypted(path: Path):
    """One page, encrypted so a user password is needed to read the page tree."""
    pdf = Pdf.new()
    blank_page(pdf, A4)
    pdf.docinfo[DOCINFO_TITLE] = String("Encrypted sample")
    pdf.save(path, encryption=pikepdf.Encryption(owner="owner-password", user="user-password", R=6))
    print(f"wrote {path}")


def main():
    target = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    target.mkdir(parents=True, exist_ok=True)
    make_modern(target / "Modern.pdf")
    make_outlined(target / "Outlined.pdf")
    make_encrypted(target / "Encrypted.pdf")


if __name__ == "__main__":
    main()
