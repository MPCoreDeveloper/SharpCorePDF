"""Checks that three independent text extractors read back the text the library wrote.

Run with:  python docs/tools/validate-text.py <corpus-dir>
           (after: uv pip install -r docs/tools/validation-requirements.txt)

The corpus is the directory `dotnet run --project tools/SharpCorePDF.Validation -- corpus <dir>` writes. Its
manifest lists, for every document, the lines each page was drawn with; the encrypted documents are listed
with their user password and are read with it, so the check also proves another implementation decrypts the
content streams the writer encrypted, not only that it accepts the password.

qpdf --check proves a file is well formed. It does not prove that a reader who extracts the text gets the
text that was drawn: that depends on the font's encoding or its ToUnicode map, on string escaping and on the
spacing the writer chose. pypdf, pdfminer.six and MuPDF share no code with each other or with this library.

A line counts as read when it appears in the page's extracted text after runs of whitespace are collapsed.
A line an extractor returns only with its spaces lost (or doubled) is reported as a spacing difference and
counted as a failure, because a reader that copies the text gets the wrong words.
"""

import json
import logging
import re
import sys
import unicodedata
from pathlib import Path


def normalise(text: str) -> str:
    return re.sub(r"\s+", " ", unicodedata.normalize("NFC", text)).strip()


def squeeze(text: str) -> str:
    return re.sub(r"\s+", "", unicodedata.normalize("NFC", text))


def extract_pypdf(path: Path, password: str) -> list[str]:
    from pypdf import PdfReader

    reader = PdfReader(str(path))

    if reader.is_encrypted:
        reader.decrypt(password)

    return [page.extract_text() or "" for page in reader.pages]


def extract_pdfminer(path: Path, password: str) -> list[str]:
    from pdfminer.high_level import extract_pages
    from pdfminer.layout import LTTextContainer

    pages = []

    for layout in extract_pages(str(path), password=password):
        pages.append("\n".join(element.get_text() for element in layout if isinstance(element, LTTextContainer)))

    return pages


def extract_mupdf(path: Path, password: str) -> list[str]:
    import pymupdf

    with pymupdf.open(str(path)) as document:
        if document.needs_pass and not document.authenticate(password):
            raise RuntimeError("MuPDF refused the password")

        return [page.get_text() for page in document]


EXTRACTORS = {
    "pypdf": extract_pypdf,
    "pdfminer.six": extract_pdfminer,
    "MuPDF": extract_mupdf,
}


def check(corpus: Path, entry: dict, password: str) -> int:
    path = corpus / entry["file"]
    expected = entry["pages"]
    failures = 0

    print(f"== {entry['file']}")

    for name, extract in EXTRACTORS.items():
        try:
            pages = extract(path, password)
        except Exception as error:  # noqa: BLE001 - a reader that cannot open the file is the finding
            print(f"  {name:13}: FAILED to read: {type(error).__name__}: {error}")
            failures += 1
            continue

        if len(pages) != len(expected):
            print(f"  {name:13}: FAILED: {len(pages)} page(s), expected {len(expected)}")
            failures += 1
            continue

        missing = []

        for number, (text, lines) in enumerate(zip(pages, expected), start=1):
            flat = normalise(text)
            squeezed = squeeze(text)

            for line in lines:
                if normalise(line) in flat:
                    continue

                kind = "spacing differs" if squeeze(line) in squeezed else "missing"
                missing.append(f"page {number}: {kind}: {line!r}")

        if missing:
            failures += len(missing)
            print(f"  {name:13}: FAILED")

            for item in missing:
                print(f"    {item}")
        else:
            print(f"  {name:13}: every line read back ({sum(len(lines) for lines in expected)} lines)")

    return failures


def main() -> None:
    if len(sys.argv) != 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        sys.exit(2)

    # pdfminer reports, correctly, that a print-only document asks not to be extracted; the check reads it
    # anyway because it is checking the bytes, not honouring the permission.
    logging.getLogger("pdfminer").setLevel(logging.ERROR)

    corpus = Path(sys.argv[1])
    manifest = json.loads((corpus / "manifest.json").read_text(encoding="utf-8"))
    failures = 0

    for entry in manifest["text"]:
        failures += check(corpus, entry, "")

    for entry in manifest["encryption"]:
        # The owner-only document has an empty user password, which is also how a reader opens it.
        failures += check(corpus, entry, entry["userPassword"])

    print()
    print("text round trip: " + ("every extractor read every line" if failures == 0 else f"{failures} failure(s)"))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
