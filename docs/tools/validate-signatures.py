"""Validates the signatures of a PDF with an independent implementation (pyHanko).

Run with:  uv run --with pyhanko python docs/tools/validate-signatures.py <file.pdf> [...]
           uv run --with pyhanko python docs/tools/validate-signatures.py --trust-signer root.pem <file.pdf>
           uv run --with pyhanko python docs/tools/validate-signatures.py --require-trusted <file.pdf> [...]

Reports, for every signature, the two answers this library keeps apart: the integrity — that the CMS
parses and the message digest it carries matches the bytes the byte range covers — and the trust — that
the chain leads to an anchor the caller named. The tool counts a failure for a signature whose integrity
does not hold, and, with --require-trusted, for one whose chain leads nowhere.

Nothing here trusts a machine store on its own, because that is not what the library does either: an
anchor is supplied with --trust-signer, as a PEM or DER certificate, and no network is used.

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


def main():
    arguments = sys.argv[1:]
    anchors = []
    require_trusted = False

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
