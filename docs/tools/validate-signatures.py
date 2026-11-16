"""Validates the signatures of a PDF with an independent implementation (pyHanko).

Run with:  uv run --with pyhanko python docs/tools/validate-signatures.py <file.pdf> [...]
           uv run --with pyhanko python docs/tools/validate-signatures.py --trust-signer root.pem <file.pdf>
           uv run --with pyhanko python docs/tools/validate-signatures.py --require-trusted <file.pdf> [...]
           python docs/tools/validate-signatures.py --manifest <corpus-dir>/manifest.json [--known-defect <file>:<field>]...

Reports, for every signature, the two answers this library keeps apart: the integrity — that the CMS
parses and the message digest it carries matches the bytes the byte range covers — and the trust — that
the chain leads to an anchor the caller named. The tool counts a failure for a signature whose integrity
does not hold, and, with --require-trusted, for one whose chain leads nowhere.

Nothing here trusts a machine store on its own, because that is not what the library does either: an
anchor is supplied with --trust-signer, as a PEM or DER certificate, and no network is used.

--manifest reads the signed documents of the validation corpus (`dotnet run --project
tools/SharpCorePDF.Validation -- corpus <dir>`) and checks more than integrity: every signature must be
intact and trusted against the corpus's own signer, and pyHanko's coverage of each field must be the one the
manifest states — ENTIRE_FILE for the newest signature, ENTIRE_REVISION for one an append left behind.
pyHanko only answers ENTIRE_REVISION when the signature dictionary was not written again after signing, so the
check is also the one that notices an update that rewrote the signed objects. The modification level pyHanko's
difference analysis assigns is printed beside it. --known-defect <file>:<field> expects that one field to
miss its coverage and fails the run if it no longer does, so the flag goes with the fix.

Run with --help to read this text again.
"""

import logging
import sys
from pathlib import Path

from pyhanko.pdf_utils.reader import PdfFileReader
from pyhanko.sign.validation import validate_pdf_signature


def load_anchors(paths: list[str]) -> list:
    """Reads PEM or DER certificates and returns them in the form pyHanko wants."""
    from asn1crypto import pem, x509

    anchors = []
    for path in paths:
        data = Path(path).read_bytes()

        if pem.detect(data):
            _, _, data = pem.unarmor(data)

        anchors.append(x509.Certificate.load(data))

    return anchors


def trust_context(paths: list[str]):
    """Builds a validation context over the anchors given, or returns nothing when there are none."""
    if not paths:
        return None

    from pyhanko_certvalidator import ValidationContext

    return ValidationContext(trust_roots=load_anchors(paths), allow_fetching=False)


def describe_signer(status) -> str:
    """Names the signer the way a certificate reads, or says that there is none."""
    certificate = getattr(status, "signing_cert", None)

    if certificate is None:
        return "no certificate"

    try:
        return certificate.subject.human_friendly
    except Exception:  # noqa: BLE001 - a certificate that cannot be printed is still a certificate
        return repr(certificate)


def check(path: Path, context, require_trusted: bool) -> int:
    print(f"== {path}")

    try:
        with path.open("rb") as handle:
            reader = PdfFileReader(handle, strict=False)
            signatures = list(reader.embedded_signatures)
            failures = 0

            if not signatures:
                print("  signatures: none")
                return 0 if context is None else 1

            for index, embedded in enumerate(signatures, start=1):
                status = validate_pdf_signature(embedded, signer_validation_context=context)
                intact = bool(getattr(status, "intact", False))
                trusted = bool(getattr(status, "trusted", False))
                coverage = getattr(embedded, "coverage", None)
                level = getattr(coverage, "name", None) or str(coverage)
                field = getattr(embedded, "field_name", "?")

                print(f"  signature {index}: field '{field}', signer {describe_signer(status)}")
                print(f"    signed at : {getattr(status, 'signer_reported_dt', None)}")
                print(f"    integrity : {'intact' if intact else 'BROKEN'} (coverage {level})")

                if context is None:
                    print("    trust     : not checked (no anchor supplied)")
                else:
                    print(f"    trust     : {'trusted' if trusted else 'untrusted'}")

                for problem in getattr(status, "problems", []) or []:
                    print(f"    problem   : {problem}")

                if not intact or (require_trusted and not trusted):
                    failures += 1
    except Exception as error:  # noqa: BLE001 - the point of the check is to fail loudly
        print(f"  FAILED {type(error).__name__}: {error}")
        return 1

    return failures


def check_manifest(manifest_path: Path, known: set[str]) -> int:
    """Checks every signed document the corpus lists against the coverage the manifest states."""
    import json

    corpus = manifest_path.parent
    signatures = json.loads(manifest_path.read_text(encoding="utf-8"))["signatures"]
    context = trust_context([str(corpus / signatures["anchor"])])
    failures = 0
    seen = set()

    for document in signatures["files"]:
        print(f"== {document['file']}")
        expected = {item["field"]: item["coverage"] for item in document["signatures"]}

        with (corpus / document["file"]).open("rb") as handle:
            reader = PdfFileReader(handle, strict=False)
            found = {}

            for embedded in reader.embedded_signatures:
                status = validate_pdf_signature(embedded, signer_validation_context=context)
                coverage = getattr(embedded.coverage, "name", str(embedded.coverage))
                level = getattr(getattr(status, "modification_level", None), "name", None)
                found[embedded.field_name] = coverage

                print(f"  {embedded.field_name}: intact {status.intact}, trusted {status.trusted}, "
                      f"coverage {coverage}, modification level after signing {level}")

                if not status.intact or not status.trusted:
                    failures += 1
                    print("    FAILED: the signature has to be intact and trusted against the corpus signer")

        for field, coverage in expected.items():
            key = f"{document['file']}:{field}"
            seen.add(key)
            actual = found.get(field)

            if key in known:
                if actual == coverage:
                    failures += 1
                    print(f"    FAILED: {key} is listed as a known defect but now has coverage {coverage}; remove the flag")
                else:
                    print(f"    known defect: {field} has coverage {actual}, the manifest expects {coverage}")
            elif actual != coverage:
                failures += 1
                print(f"    FAILED: {field} has coverage {actual}, the manifest expects {coverage}")

    for key in sorted(known - seen):
        failures += 1
        print(f"FAILED: --known-defect {key} names no signature the manifest lists")

    return failures


def main():
    arguments = sys.argv[1:]
    anchors = []
    require_trusted = False
    known = set()

    while "--known-defect" in arguments:
        index = arguments.index("--known-defect")
        known.add(arguments[index + 1])
        del arguments[index : index + 2]

    if "--manifest" in arguments:
        logging.getLogger("pyhanko").setLevel(logging.CRITICAL)
        index = arguments.index("--manifest")
        failures = check_manifest(Path(arguments[index + 1]), known)
        print()
        print("signatures: " + ("every coverage agrees" if failures == 0 else f"{failures} failure(s)"))
        sys.exit(0 if failures == 0 else 1)

    # pyHanko reports an untrusted chain by logging the exception it caught, and this tool prints the same
    # fact as a result: the traceback would only say it twice, once of them mid-sentence.
    logging.getLogger("pyhanko").setLevel(logging.CRITICAL)

    if any(argument in ("--help", "-h") for argument in arguments):
        print(__doc__)
        sys.exit(0)

    while "--trust-signer" in arguments:
        index = arguments.index("--trust-signer")

        if index + 1 >= len(arguments):
            print("--trust-signer needs the path of a certificate")
            print(__doc__)
            sys.exit(2)

        anchors.append(arguments[index + 1])
        del arguments[index : index + 2]

    if "--require-trusted" in arguments:
        require_trusted = True
        arguments.remove("--require-trusted")

    if not arguments:
        print(__doc__)
        sys.exit(2)

    context = trust_context(anchors)
    failures = 0

    for argument in arguments:
        failures += check(Path(argument), context, require_trusted)

    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
