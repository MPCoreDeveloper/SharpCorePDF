"""Creates the small PDF fixtures used by the SharpCorePDF tests.

Run with:  uv run --with pikepdf python docs/tools/make-fixtures.py tests/SharpCorePDF.Tests/Assets

Pass one or more fixture names — modern, outlined, encrypted, encrypted-revisions,
encrypted-non-ascii, encrypted-clear-metadata — to regenerate only those; with no name every fixture
is rewritten.

The fixtures are checked in; this script exists so they can be regenerated and so it is clear
exactly what they contain. These shapes are deliberately covered:

* a document written with object streams and cross-reference streams (PDF 1.5 style),
* a document with an outline, a page label tree, an annotation and a rotated page,
* one encrypted document per revision of the standard security handler, including the two revisions
  whose user password is empty and which therefore open without a password,
* one document of revision 4 whose user password is not ASCII, which is where the encoding of a
  password of the legacy revisions shows: the producer and the engine both hash its Latin-1 bytes,
* one document of revision 4 whose metadata stream is stored in the clear, because it clears
  EncryptMetadata: that one stream of an encrypted document must be read as it stands, so decrypting
  it is the mistake a fixture can catch.
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

# The two titles of the fixture whose metadata stream is stored in the clear: the XMP one is in the file
# as it stands and the document information one is encrypted, so the pair tells a test which of the two
# the reader had to decrypt.
CLEAR_METADATA_XMP_TITLE = "Cleartext metadata sample"
CLEAR_METADATA_DOCINFO_TITLE = "Encrypted sample clear metadata"


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


def make_encrypted_revision(
    path: Path, revision: int, aes: bool, user: str, owner: str, title: str
):
    """One page, encrypted with one specific revision of the standard security handler.

    Every revision derives its file key differently — 2 and 3 hash the password with MD5, 4 adds the
    crypt filters, 5 and 6 replace the whole scheme with SHA-256 — so one fixture per revision is what
    proves the reader implements all of them rather than only the newest.
    """
    pdf = Pdf.new()
    blank_page(pdf, A4)
    pdf.docinfo[DOCINFO_TITLE] = String(title)

    # Only revision 4 and later can state whether the metadata stream is encrypted; asking an older
    # revision for it is what pikepdf rejects.
    pdf.save(
        path,
        encryption=pikepdf.Encryption(
            owner=owner, user=user, R=revision, aes=aes, metadata=revision >= 4
        ),
    )
    print(f"wrote {path}")


def make_encrypted_revisions(target: Path):
    """One fixture per revision of the standard security handler.

    The empty user password of two of them is the shape a document has when it is only protected
    against editing: it opens without a password at all.
    """
    make_encrypted_revision(
        target / "EncryptedRc4.pdf", 2, False, "", "owner-password", "Encrypted sample R2"
    )
    make_encrypted_revision(
        target / "EncryptedRc4Large.pdf", 3, False, "user-password", "owner-password", "Encrypted sample R3"
    )
    make_encrypted_revision(
        target / "EncryptedAes128.pdf", 4, True, "user-password", "owner-password", "Encrypted sample R4"
    )
    make_encrypted_revision(
        target / "EncryptedAes256R5.pdf", 5, True, "", "owner-password", "Encrypted sample R5"
    )


def make_encrypted_non_ascii_password(path: Path):
    """One page whose user password is not ASCII, which only the legacy revisions make interesting.

    Revisions 2 to 4 predate any agreement on how the characters of a password become bytes. Adobe,
    qpdf and pypdf all hash the Latin-1 bytes of the text, which is the reading the engine reads and
    writes; a password whose text has no Latin-1 form is hashed as UTF-8 instead, and there is no
    fixture for it because no tool here can write one.
    """
    pdf = Pdf.new()
    blank_page(pdf, A4)
    pdf.docinfo[DOCINFO_TITLE] = String("Encrypted sample non-ASCII")

    pdf.save(
        path,
        encryption=pikepdf.Encryption(
            owner="owner-password",
            user="ünïcode-pässword",
            R=4,
            aes=True,
            metadata=True,
        ),
    )
    print(f"wrote {path}")


def make_encrypted_clear_metadata(path: Path):
    """One page whose metadata stream is stored in the clear, which only revision 4 can state.

    Clearing EncryptMetadata is what lets a tool read the metadata of a document it cannot otherwise
    open, so a reader has to leave that one stream alone: decrypting it turns the XMP into rubbish,
    which is a defect no other assertion of the suite would notice. The metadata pikepdf writes is
    uncompressed, so the XMP text is visible in the file exactly as the specification intends.
    """
    pdf = Pdf.new()
    blank_page(pdf, A4)

    with pdf.open_metadata() as metadata:
        metadata["dc:title"] = CLEAR_METADATA_XMP_TITLE
        metadata["dc:creator"] = ["SharpCorePDF"]

    # Storing XMP also updates the document information dictionary, so the title that has to stay
    # encrypted is set afterwards.
    pdf.docinfo[DOCINFO_TITLE] = String(CLEAR_METADATA_DOCINFO_TITLE)

    pdf.save(
        path,
        encryption=pikepdf.Encryption(
            owner="owner-password", user="user-password", R=4, aes=True, metadata=False
        ),
    )
    print(f"wrote {path}")


def main():
    target = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    target.mkdir(parents=True, exist_ok=True)

    # Naming a fixture regenerates only that one, so adding a fixture never rewrites the bytes of the
    # ones that are already checked in.
    makers = {
        "modern": lambda: make_modern(target / "Modern.pdf"),
        "outlined": lambda: make_outlined(target / "Outlined.pdf"),
        "encrypted": lambda: make_encrypted(target / "Encrypted.pdf"),
        "encrypted-revisions": lambda: make_encrypted_revisions(target),
        "encrypted-non-ascii": lambda: make_encrypted_non_ascii_password(
            target / "EncryptedNonAscii.pdf"
        ),
        "encrypted-clear-metadata": lambda: make_encrypted_clear_metadata(
            target / "EncryptedClearMetadata.pdf"
        ),
    }

    requested = sys.argv[2:]

    if not requested:
        requested = list(makers)

    for name in requested:
        makers[name]()


if __name__ == "__main__":
    main()
