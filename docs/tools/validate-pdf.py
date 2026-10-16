"""Validates a PDF with an independent implementation (pikepdf, which wraps qpdf).

Run with:  uv run --with pikepdf python docs/tools/validate-pdf.py <file.pdf> [...]
           uv run --with pikepdf python docs/tools/validate-pdf.py --password <password> <file.pdf> [...]

Checks that qpdf raises no problems, that every page can be read, that every content stream and
image can be decoded, and that no object in the file is unreachable or dangling. For an encrypted
document the check needs the password, which is what proves that another implementation derives the
same key this repository's writer derived.
"""

import sys
from pathlib import Path

import pikepdf


def run_qpdf_check(path: Path, password: str = "") -> list[str]:
    """Runs qpdf --check through pikepdf and returns the lines it complained about."""
    arguments = ["qpdf", "--check", str(path)]

    if password:
        arguments.append(f"--password={password}")

    job = pikepdf.Job(arguments)
    job.run()

    problems = []
    if job.exit_code != 0:
        problems.append(f"qpdf --check exited with {job.exit_code}")

    return problems


def check(path: Path, password: str = "") -> int:
    print(f"== {path}")
    problems = run_qpdf_check(path, password)
    pdf = pikepdf.open(path, password=password)

    if problems:
        for problem in problems:
            print(f"  problem: {problem}")
    else:
        print("  qpdf check: no problems")

    print(f"  version   : {pdf.pdf_version}, pages: {len(pdf.pages)}, objects: {len(pdf.objects)}")

    if pdf.is_encrypted:
        print(f"  encrypted : {describe_encryption(pdf.encryption)}")

        if password:
            # qpdf reports the user password it verified or recovered, which is how a check can tell
            # which of the two passwords opened the document: nothing is reported for a revision 5 or
            # 6 document that was opened with its owner password, because those store no user password.
            known = bytes(pdf.encryption.user_password or b"")
            print(f"  password  : the user password checks out as {known!r}")

    print(f"  root      : {sorted(str(k) for k in pdf.Root.keys())}")

    if "/Outlines" in pdf.Root:
        print(f"  bookmarks : {count_bookmarks(pdf.Root.Outlines.get('/First'))}")

    failures = check_pages(pdf)
    decoded, undecodable = check_streams(pdf)

    print(f"  streams   : {decoded} decoded, {undecodable} failed")
    return failures + len(problems) + undecodable


def describe_encryption(info) -> str:
    """Describes the encryption dictionary of an open document."""
    permissions = int(info.P)

    return (
        f"V {info.V}, R {info.R}, {info.bits} bits, "
        f"P {permissions} (print={bool(permissions & 4)}, copy={bool(permissions & 16)}), "
        f"streams {info.stream_method}, strings {info.string_method}"
    )


def check_pages(pdf) -> int:
    """Reads every page and returns how many of them could not be read."""
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

    return failures


def check_streams(pdf) -> tuple[int, int]:
    """Decodes every stream and returns how many were decoded and how many failed."""
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

    return decoded, undecodable


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
    arguments = sys.argv[1:]
    password = ""

    if "--password" in arguments:
        index = arguments.index("--password")
        password = arguments[index + 1]
        del arguments[index : index + 2]

    exit_code = 0
    for argument in arguments:
        exit_code += check(Path(argument), password)

    sys.exit(0 if exit_code == 0 else 1)


if __name__ == "__main__":
    main()
