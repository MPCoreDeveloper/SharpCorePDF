"""Checks documents against the Arlington PDF Model through veraPDF's Arlington service.

Run with:  docker run -d -p 8080:8080 verapdf/arlington:v1.30.4
           python docs/tools/validate-arlington.py [--url http://localhost:8080] [--report-dir <dir>]
               [--accept <rule>]... [--strict] <file.pdf | dir>...

The Arlington PDF Model (PDF Association) is the machine-readable form of ISO 32000-2: every object, every
key it may hold, the type and the values each key may take, and the PDF version that introduced it. qpdf
checks that a file parses and veraPDF's PDF/A profiles check what archiving needs; neither checks that a
dictionary only holds keys its type defines, with the right types, for the version in the header. That is
the class of mistake a writer makes when it emits a PDF 2.0 key in a 1.7 file or a name where an integer
belongs, and other readers silently forgive until one does not.

The profile is chosen from the header version (arlington1.4 ... arlington2.0), from the ids the service
lists at /api/profiles/ids. The answer is read from veraPDF's machine-readable XML report -- the
`isCompliant` attribute validate-pdfa.py reads too -- and every failed rule is listed with its clause and
test number. --accept <clause>-<test> names a rule whose failure is known and accepted; it is printed but
not counted. Without --strict the tool reports and exits 0, which is how a first run establishes what the
writer currently does; --strict makes any unaccepted failed rule fail the run.

No network is used beyond the local service, and no document leaves the machine.
"""

import argparse
import json
import re
import sys
import urllib.request
import uuid
import xml.etree.ElementTree as ElementTree
from pathlib import Path


def request(url: str, data: bytes | None = None, headers: dict | None = None) -> bytes:
    with urllib.request.urlopen(urllib.request.Request(url, data=data, headers=headers or {}), timeout=300) as response:
        return response.read()


def profile_ids(base: str) -> list[str]:
    return [str(item) for item in json.loads(request(f"{base}/api/profiles/ids"))]


def header_version(path: Path) -> str:
    match = re.search(rb"%PDF-(\d\.\d)", path.read_bytes()[:1024])
    return match.group(1).decode() if match else "2.0"


def validate(base: str, profile: str, path: Path) -> bytes:
    boundary = uuid.uuid4().hex
    body = (
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{path.name}\"\r\n"
        "Content-Type: application/pdf\r\n\r\n").encode() + path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()

    return request(
        f"{base}/api/validate/{profile}",
        data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}", "Accept": "application/xml"})


def read_report(payload: bytes) -> tuple[bool, list[str]]:
    """Returns the verdict and the failed rules, from the XML report (or a JSON one, if that is what came back)."""
    try:
        root = ElementTree.fromstring(payload)
    except ElementTree.ParseError:
        document = json.loads(payload)
        text = json.dumps(document)
        compliant = re.search(r'"(?:isCompliant|compliant)"\s*:\s*(true|false)', text)

        if compliant is None:
            raise ValueError("the service answered with a report this tool does not recognise") from None

        return compliant.group(1) == "true", re.findall(r'"clause"\s*:\s*"([^"]+)"', text)

    reports = [element for element in root.iter() if element.tag.endswith("validationReport")]

    if not reports:
        raise ValueError("the XML report holds no validationReport element")

    failed = []

    for rule in root.iter():
        if rule.tag.endswith("rule") and rule.get("status") == "failed":
            identifier = f"{rule.get('clause', '?')}-{rule.get('testNumber', '?')}"
            description = next((child.text for child in rule if child.tag.endswith("description")), "") or ""
            failed.append(f"{identifier}: {description.strip()}")

    return all(report.get("isCompliant") == "true" for report in reports), failed


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="+")
    parser.add_argument("--url", default="http://localhost:8080")
    parser.add_argument("--report-dir", type=Path)
    parser.add_argument("--accept", action="append", default=[])
    parser.add_argument("--exclude", action="append", default=[], help="a file name to leave out, e.g. an encrypted one")
    parser.add_argument("--strict", action="store_true")
    options = parser.parse_args()

    base = options.url.rstrip("/")
    ids = profile_ids(base)
    print(f"Arlington service at {base}, profiles: {', '.join(sorted(i for i in ids if 'arlington' in i.lower()))}")

    files = []

    for argument in options.paths:
        path = Path(argument)
        files.extend(sorted(path.glob("*.pdf")) if path.is_dir() else [path])

    unaccepted = 0

    for path in files:
        if path.name in options.exclude:
            continue

        version = header_version(path)
        profile = next((i for i in ids if i.lower() == f"arlington{version}"), None)

        if profile is None:
            print(f"== {path.name}: no Arlington profile for PDF {version}")
            unaccepted += 1
            continue

        payload = validate(base, profile, path)

        if options.report_dir:
            options.report_dir.mkdir(parents=True, exist_ok=True)
            (options.report_dir / f"{path.stem}.arlington.xml").write_bytes(payload)

        compliant, failed = read_report(payload)
        accepted = [rule for rule in failed if rule.split(":")[0] in options.accept]
        counted = [rule for rule in failed if rule not in accepted]
        unaccepted += len(counted)

        print(f"== {path.name} ({profile}): " + ("conforms" if compliant else f"{len(failed)} failed rule(s)"))

        for rule in counted:
            print(f"    {rule}")

        for rule in accepted:
            print(f"    (accepted) {rule}")

    print()
    print("Arlington: " + ("no unaccepted failure" if unaccepted == 0 else f"{unaccepted} unaccepted failure(s)"))
    sys.exit(1 if options.strict and unaccepted else 0)


if __name__ == "__main__":
    main()
