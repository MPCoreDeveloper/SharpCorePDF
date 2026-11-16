"""Reads a corpus of documents nobody here wrote, one process per file, and sorts what happened.

Run with:  python docs/tools/corpus-robustness.py --probe <SharpCorePDF.Validation.dll> [options] <file.pdf | dir>...
           python docs/tools/corpus-robustness.py --synthesize <dir>

Each file is handed to `SharpCorePDF.Validation probe`, which reads it through every read use case and writes
it back through a merge and an edit, in a process of its own with a time limit and a hard limit on the
managed heap (DOTNET_GCHeapHardLimit, so a decompression bomb becomes an OutOfMemoryException the probe
reports instead of a machine that swaps). The outcome of each file is one of:

  read      the engine read it and wrote it back readably
  refused   the engine refused it with a PdfException -- the contract's answer to a damaged document
  fault     any other exception: a defect in the engine, whatever the file looks like
  crash     the process died without answering (a stack overflow, a runtime abort)
  timeout   the process did not finish in --timeout seconds

The run fails on a fault, a crash or a timeout. --known <file-name> expects that one file to misbehave
because of a defect that is recorded and not yet fixed; it is strict, so the run also fails when a listed
file starts to behave, and the flag goes with the fix.

--synthesize <dir> writes documents built to reach the failure classes a code review named, so the runner
can be shown to see each class and a fix can be shown to close it:

  length-chain.pdf    60,000 streams whose /Length is the next stream: unbounded recursion while resolving
  flate-bomb.pdf      a 1 MB content stream that inflates past 1 GiB: no limit on decoded size
  objstm-count.pdf    an object stream that declares 2^31-1 objects: an allocation sized by /N
  jpeg-selector.pdf   a JPEG scan that selects Huffman table 4 of 0-3: an unchecked table index

--report <file.json> writes every outcome, with the first line of what the probe said, for the run's artefacts.
"""

import argparse
import json
import os
import subprocess
import sys
import time
import zlib
from pathlib import Path

EXIT_READ, EXIT_REFUSED, EXIT_FAULT = 0, 2, 3


def probe(dll: str, path: Path, timeout: int, heap_mb: int) -> tuple[str, str, float]:
    environment = dict(os.environ)
    environment["DOTNET_GCHeapHardLimit"] = hex(heap_mb * 1024 * 1024)
    started = time.monotonic()

    try:
        completed = subprocess.run(
            ["dotnet", dll, "probe", str(path)],
            capture_output=True, text=True, errors="replace", timeout=timeout, env=environment, check=False)
    except subprocess.TimeoutExpired:
        return "timeout", f"no answer within {timeout} s", time.monotonic() - started

    elapsed = time.monotonic() - started
    lines = [line for line in (completed.stdout + "\n" + completed.stderr).splitlines() if line.strip()]
    said = next((line for line in lines if line.startswith("outcome:")), lines[0] if lines else "")

    outcome = {EXIT_READ: "read", EXIT_REFUSED: "refused", EXIT_FAULT: "fault"}.get(completed.returncode, "crash")

    if outcome == "crash":
        died = next((line for line in lines if "overflow" in line.lower() or "fatal" in line.lower()), "")
        said = f"exit code {completed.returncode}" + (f": {died}" if died else "")

    return outcome, said, elapsed


def synthesize(directory: Path) -> None:
    directory.mkdir(parents=True, exist_ok=True)

    def document(objects: list[bytes], root: int = 1) -> bytes:
        output = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
        offsets = []

        for number, body in enumerate(objects, start=1):
            offsets.append(len(output))
            output += b"%d 0 obj\n" % number + body + b"\nendobj\n"

        xref = len(output)
        output += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objects) + 1)
        output += b"".join(b"%010d 00000 n \n" % offset for offset in offsets)
        output += b"trailer\n<< /Size %d /Root %d 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (len(objects) + 1, root, xref)
        return bytes(output)

    page = b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>"
    head = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>", page]

    # Every stream's /Length is the next object, which is itself a stream, so resolving one length
    # resolves the next before it can finish: the recursion is as deep as the chain is long.
    chain = 60_000
    streams = [b"<< /Length %d 0 R >>\nstream\nq Q\nendstream" % (number + 1) for number in range(4, 4 + chain)]
    streams.append(b"4")
    (directory / "length-chain.pdf").write_bytes(document(head + streams))

    # Zeros deflate about a thousand to one, so a megabyte of stream is a gigabyte of content.
    compressor = zlib.compressobj(9)
    block = bytes(64 * 1024 * 1024)
    bomb = b"".join(compressor.compress(block) for _ in range(17)) + compressor.flush()
    (directory / "flate-bomb.pdf").write_bytes(document(head + [
        b"<< /Length %d /Filter /FlateDecode >>\nstream\n" % len(bomb) + bomb + b"\nendstream"]))

    # The page lives in an object stream that claims 2^31 - 1 objects in a header of one pair. Only a
    # cross-reference stream can say an object is compressed, so this one is written with one.
    inner = zlib.compress(b"3 0 " + page)
    output = bytearray(b"%PDF-1.7\n%\xe2\xe3\xcf\xd3\n")
    offsets = {}

    for number, body in ((1, head[0]), (2, head[1]), (4, b"<< /Length 3 >>\nstream\nq Q\nendstream"),
                         (5, b"<< /Type /ObjStm /N 2147483647 /First 4 /Length %d /Filter /FlateDecode >>\nstream\n"
                          % len(inner) + inner + b"\nendstream")):
        offsets[number] = len(output)
        output += b"%d 0 obj\n" % number + body + b"\nendobj\n"

    offsets[6] = len(output)
    entries = [(0, 0, 65535), (1, offsets[1], 0), (1, offsets[2], 0), (2, 5, 0),
               (1, offsets[4], 0), (1, offsets[5], 0), (1, offsets[6], 0)]
    table = b"".join(kind.to_bytes(1, "big") + field.to_bytes(4, "big") + index.to_bytes(2, "big")
                     for kind, field, index in entries)
    output += (b"6 0 obj\n<< /Type /XRef /Size 7 /W [1 4 2] /Root 1 0 R /Length %d >>\nstream\n" % len(table)
               + table + b"\nendstream\nendobj\nstartxref\n%d\n%%%%EOF\n" % offsets[6])
    (directory / "objstm-count.pdf").write_bytes(bytes(output))

    # A baseline JPEG whose scan header selects DC and AC table 4; only tables 0 to 3 exist.
    jpeg = bytes.fromhex(
        "ffd8ffdb004300" + "01" * 64 +
        "ffc0000b080008000801011100"
        "ffc4001f0000010501010101010100000000000000000102030405060708090a0b"
        "ffda0008010144003f00" + "00" * 16 + "ffd9")
    image = (b"<< /Type /XObject /Subtype /Image /Width 8 /Height 8 /BitsPerComponent 8 /ColorSpace /DeviceGray "
             b"/Filter /DCTDecode /Length %d >>\nstream\n" % len(jpeg) + jpeg + b"\nendstream")
    content = b"q 100 0 0 100 50 50 cm /Im0 Do Q"
    (directory / "jpeg-selector.pdf").write_bytes(document([
        head[0], head[1],
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>",
        b"<< /Length %d >>\nstream\n" % len(content) + content + b"\nendstream",
        image,
    ]))

    print(f"wrote 4 hostile documents to {directory}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="*")
    parser.add_argument("--probe", help="the path of SharpCorePDF.Validation.dll")
    parser.add_argument("--timeout", type=int, default=60)
    parser.add_argument("--heap-limit-mb", type=int, default=1024)
    parser.add_argument("--known", action="append", default=[], help="a file name expected to misbehave")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--synthesize", type=Path)
    options = parser.parse_args()

    if options.synthesize:
        synthesize(options.synthesize)
        return

    if not options.probe or not options.paths:
        parser.print_help()
        sys.exit(2)

    files = []

    for argument in options.paths:
        path = Path(argument)
        files.extend(sorted(p for p in path.rglob("*") if p.is_file() and p.suffix.lower() == ".pdf") if path.is_dir() else [path])

    results = []
    counts: dict[str, int] = {}
    failures = 0
    known = set(options.known)
    seen_known = set()

    for path in files:
        outcome, said, elapsed = probe(options.probe, path, options.timeout, options.heap_limit_mb)
        counts[outcome] = counts.get(outcome, 0) + 1
        results.append({"file": str(path), "outcome": outcome, "detail": said, "seconds": round(elapsed, 2)})
        bad = outcome in ("fault", "crash", "timeout")

        if path.name in known:
            seen_known.add(path.name)

            if bad:
                print(f"known   {outcome:8} {path}  {said}")
            else:
                failures += 1
                print(f"FAILED  listed as known but {outcome}: {path}; remove --known {path.name}")
        elif bad:
            failures += 1
            print(f"FAILED  {outcome:8} {path}  {said}")

    for name in sorted(known - seen_known):
        failures += 1
        print(f"FAILED  --known {name} names no file of the corpus")

    if options.report:
        options.report.parent.mkdir(parents=True, exist_ok=True)
        options.report.write_text(json.dumps({"counts": counts, "results": results}, indent=2), encoding="utf-8")

    print()
    print(f"{len(files)} file(s): " + ", ".join(f"{count} {outcome}" for outcome, count in sorted(counts.items())))
    print("robustness: " + ("no fault, crash or timeout" if failures == 0 else f"{failures} failure(s)"))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
