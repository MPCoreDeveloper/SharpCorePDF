"""Checks what has to stay true of an incrementally updated document, with qpdf as the reader.

Run with:  python docs/tools/validate-incremental.py <corpus-dir> [--known-defect <id>]...
           (after: uv pip install -r docs/tools/validation-requirements.txt)

For every incrementally updated document the validation corpus lists:

  prefix        the bytes of the source are the first bytes of the result, unchanged (ISO 32000-1 §7.5.6);
  revisions     the file holds as many revisions as were written, and the bytes up to each %%EOF open in qpdf
                without a problem and show the page rotations that revision gave them;
  id-first      the first element of the trailer /ID is the source's, in every revision (§14.4: it "shall not
                change when the file is incrementally updated");
  appends-only  an update writes only what it changed: it does not redeclare an object of an earlier
                revision it left alone, which is what keeps a signature over that revision meaningful to a
                validator that looks at what changed after signing (pyHanko's difference analysis, Acrobat).

--known-defect <id> names a check that is expected to fail because of a defect that is recorded and not yet
fixed. It is strict: the run fails if a listed check passes, so the flag is removed with the fix rather than
forgotten. The ids are the names above.
"""

import json
import re
import sys
from pathlib import Path


def revisions(data: bytes) -> list[bytes]:
    """Cuts a document behind each %%EOF and the end of line that follows it."""
    cuts = []

    for match in re.finditer(rb"%%EOF(\r\n|\r|\n)?", data):
        cuts.append(data[: match.end()])

    return cuts


def objects(data: bytes) -> set[int]:
    return {int(number) for number in re.findall(rb"(?:^|[\r\n\s])(\d+)\s+\d+\s+obj\b", data)}


def check(corpus: Path, entry: dict) -> dict[str, list[str]]:
    import pikepdf

    data = (corpus / entry["file"]).read_bytes()
    source = (corpus / entry["source"]).read_bytes()
    results: dict[str, list[str]] = {"prefix": [], "revisions": [], "id-first": [], "appends-only": []}

    if not data.startswith(source):
        results["prefix"].append("the result does not begin with the bytes of the source")

    cuts = revisions(data)

    if len(cuts) != entry["revisions"]:
        results["revisions"].append(f"{len(cuts)} revision(s), expected {entry['revisions']}")

    source_id = bytes.fromhex(entry["sourceId"])

    for index, cut in enumerate(cuts):
        name = f"revision {index + 1}"
        job_path = corpus / f".revision-{index + 1}.pdf"
        job_path.write_bytes(cut)

        try:
            job = pikepdf.Job(["qpdf", "--check", str(job_path)])
            job.run()

            if job.exit_code != 0:
                results["revisions"].append(f"{name}: qpdf --check exited with {job.exit_code}")

            with pikepdf.open(job_path) as pdf:
                rotations = [int(page.obj.get("/Rotate", 0)) for page in pdf.pages]

                if index < len(entry["rotations"]) and rotations != entry["rotations"][index]:
                    results["revisions"].append(f"{name}: rotations {rotations}, expected {entry['rotations'][index]}")

                identifier = bytes(pdf.trailer.ID[0]) if "/ID" in pdf.trailer else b""

                if identifier != source_id:
                    results["id-first"].append(f"{name}: /ID[0] is {identifier.hex().upper()}, the source's is {entry['sourceId']}")
        finally:
            job_path.unlink(missing_ok=True)

        if index > 0:
            earlier = objects(cuts[index - 1])
            appended = objects(cut[len(cuts[index - 1]):])
            redeclared = sorted(earlier & appended)
            fresh = sorted(appended - earlier)

            # Changing a page means redeclaring an object the earlier revision has: the page itself, or the
            # page tree that points at its replacement. A revision that redeclares nothing and adds objects
            # under numbers never used before has not updated the document; it has appended a second copy
            # of it, and every object a signature covered now has a twin the signature never saw.
            if not redeclared and len(fresh) > 2:
                results["appends-only"].append(
                    f"{name}: redeclares none of the {len(earlier)} earlier object(s) and appends {len(fresh)} "
                    f"under new numbers ({fresh[0]}-{fresh[-1]}): a copy of the document, not an update of it")

    return results


def main() -> None:
    arguments = sys.argv[1:]
    known = set()

    while "--known-defect" in arguments:
        index = arguments.index("--known-defect")
        known.add(arguments[index + 1])
        del arguments[index : index + 2]

    if len(arguments) != 1 or arguments[0] in ("-h", "--help"):
        print(__doc__)
        sys.exit(2)

    corpus = Path(arguments[0])
    manifest = json.loads((corpus / "manifest.json").read_text(encoding="utf-8"))
    failures = 0

    for entry in manifest["incremental"]:
        print(f"== {entry['file']}  ({entry['revisions']} revisions over {entry['source']})")

        for name, problems in check(corpus, entry).items():
            if name in known:
                if problems:
                    print(f"  {name:13}: known defect, still reproduces")
                    for problem in problems:
                        print(f"    {problem}")
                else:
                    failures += 1
                    print(f"  {name:13}: FAILED: listed as a known defect but it passes; remove --known-defect {name}")
                continue

            failures += len(problems)
            print(f"  {name:13}: " + ("holds" if not problems else "FAILED"))

            for problem in problems:
                print(f"    {problem}")

    print()
    print("incremental invariants: " + ("hold" if failures == 0 else f"{failures} failure(s)"))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
