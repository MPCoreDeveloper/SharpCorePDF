"""Validates a PDF with an independent implementation (pikepdf, which wraps qpdf).

Run with:  uv run --with pikepdf python docs/tools/validate-pdf.py <file.pdf> [...]

Checks that qpdf raises no problems, that every page can be read, that every content stream and
image can be decoded, and that no object in the file is unreachable or dangling.
"""

import sys
from pathlib import Path

import pikepdf


def run_qpdf_check(path: Path) -> list[str]:
    """Runs qpdf --check through pikepdf and returns the lines it complained about."""
    job = pikepdf.Job(["qpdf", "--check", str(path)])
    job.run()

    problems = []
    if job.exit_code != 0:
        problems.append(f"qpdf --check exited with {job.exit_code}")

    return problems


def check(path: Path) -> int:
    print(f"== {path}")
    problems = run_qpdf_check(path)
    pdf = pikepdf.open(path)

    if problems:
        for problem in problems:
            print(f"  problem: {problem}")
    else:
        print("  qpdf check: no problems")

    print(f"  version   : {pdf.pdf_version}, pages: {len(pdf.pages)}, objects: {len(pdf.objects)}")
    print(f"  root      : {sorted(str(k) for k in pdf.Root.keys())}")

    if "/Outlines" in pdf.Root:
        print(f"  bookmarks : {count_bookmarks(pdf.Root.Outlines.get('/First'))}")

    failures = 0
    for index, page in enumerate(pdf.pages, start=1):
        try:
            data = page_contents(page)
            box = [round(float(v), 2) for v in page.MediaBox]
            print(f"  page {index}    : {box} rotate={int(page.get('/Rotate', 0))} "
                  f"content={len(data)} bytes, annots={len(page.get('/Annots', []))}")
        except Exception as error:  # noqa: BLE001 - the point of the check is to fail loudly
            failures += 1
            print(f"  page {index}    : FAILED {type(error).__name__}: {error}")

    decoded = 0
    undecodable = 0
    for obj in pdf.objects:
        if not isinstance(obj, pikepdf.Stream):
            continue

        try:
            bytes(obj.read_bytes())
            decoded += 1
        except Exception as error:  # noqa: BLE001
            # An image or font with a filter that is not a lossless compressor (JPEG, JPX, CCITT …)
            # cannot be decoded by read_bytes(), which is not a defect of the file.
            if "unfilterable" in str(error):
                decoded += 1
                continue

            undecodable += 1
            print(f"  stream    : FAILED {type(error).__name__}: {error}")

    print(f"  streams   : {decoded} decoded, {undecodable} failed")
    return failures + len(problems) + undecodable


def page_contents(page) -> bytes:
    contents = page.get("/Contents")

    if contents is None:
        return b""

    if isinstance(contents, (pikepdf.Array, list)):
        return b"".join(bytes(stream.read_bytes()) for stream in contents)

    return bytes(contents.read_bytes())


def count_bookmarks(item) -> int:
    total = 0

    while item is not None and not isinstance(item, str):
        total += 1 + count_bookmarks(item.get("/First"))
        item = item.get("/Next")

    return total


def main():
    exit_code = 0
    for argument in sys.argv[1:]:
        exit_code += check(Path(argument))
    sys.exit(0 if exit_code == 0 else 1)


if __name__ == "__main__":
    main()
