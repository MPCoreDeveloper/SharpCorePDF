"""Opens every encrypted document of the validation corpus with three independent implementations.

Run with:  python docs/tools/validate-encryption.py <corpus-dir>
           (after: uv pip install -r docs/tools/validation-requirements.txt)
           node docs/tools/validate-encryption-pdfjs.mjs <corpus-dir> <path to pdfjs-dist/legacy/build/pdf.mjs>
           is the fourth reader, pdf.js, which needs Node.

For each algorithm the writer offers (RC4 40 and 128 bit, AES 128 and 256 bit), each permission set, and a
document that only has an owner password, the check asks qpdf (through pikepdf), MuPDF and pypdf:

* whether the user password opens the document as the user, the owner password opens it as the owner, and a
  wrong password opens nothing — which proves the other reader derives the same keys from the same
  passwords, since each password is checked against an entry the writer computed;
* whether the encryption dictionary states the version, revision and key length the algorithm calls for;
* whether the permissions it reads are the ones that were asked for;
* and whether qpdf --check, given the password, finds nothing wrong with the decrypted document.

Text decryption is checked separately, by validate-text.py, on the same files.
"""

import json
import sys
from pathlib import Path

WRONG_PASSWORD = "not-the-password"


def check_qpdf(path: Path, entry: dict) -> list[str]:
    import pikepdf

    problems = []
    user, owner = entry["userPassword"], entry["ownerPassword"]

    with pikepdf.open(path, password=user) as pdf:
        encryption = pdf.encryption

        if (encryption.V, encryption.R, encryption.bits) != (entry["version"], entry["revision"], entry["keyBits"]):
            problems.append(
                f"states V {encryption.V} R {encryption.R} {encryption.bits} bits, expected "
                f"V {entry['version']} R {entry['revision']} {entry['keyBits']} bits")

        allow = pdf.allow
        found = {
            "print": allow.print_lowres,
            "modify": allow.modify_other,
            "copy": allow.extract,
            "annotate": allow.modify_annotation,
        }

        for name, wanted in entry["permissions"].items():
            if found[name] != wanted:
                problems.append(f"permission '{name}' reads {found[name]}, expected {wanted}")

    with pikepdf.open(path, password=owner):
        pass

    if user:
        for attempt in ("", WRONG_PASSWORD):
            try:
                with pikepdf.open(path, password=attempt):
                    problems.append(f"opened with the password {attempt!r}")
            except pikepdf.PasswordError:
                pass

    job = pikepdf.Job(["qpdf", "--check", str(path), f"--password={user or owner}"])
    job.run()

    if job.exit_code != 0:
        problems.append(f"qpdf --check exited with {job.exit_code}")

    return problems


def check_mupdf(path: Path, entry: dict) -> list[str]:
    import pymupdf

    problems = []
    user, owner = entry["userPassword"], entry["ownerPassword"]

    # MuPDF answers authenticate() with a bit set: 2 for the user password, 4 for the owner password.
    with pymupdf.open(str(path)) as document:
        if user:
            if not document.needs_pass:
                problems.append("opened without a password")

            if document.authenticate(WRONG_PASSWORD) != 0:
                problems.append("accepted a wrong password")

            if not document.authenticate(user) & 2:
                problems.append("did not accept the user password as the user password")
        elif document.needs_pass:
            problems.append("asks for a password the document does not have")

        permissions = document.permissions
        found = {
            "print": bool(permissions & pymupdf.PDF_PERM_PRINT),
            "modify": bool(permissions & pymupdf.PDF_PERM_MODIFY),
            "copy": bool(permissions & pymupdf.PDF_PERM_COPY),
            "annotate": bool(permissions & pymupdf.PDF_PERM_ANNOTATE),
        }

        for name, wanted in entry["permissions"].items():
            if found[name] != wanted:
                problems.append(f"permission '{name}' reads {found[name]}, expected {wanted}")

    with pymupdf.open(str(path)) as document:
        if not document.authenticate(owner) & 4:
            problems.append("did not accept the owner password as the owner password")

    return problems


def check_pypdf(path: Path, entry: dict) -> list[str]:
    from pypdf import PasswordType, PdfReader

    problems = []

    # A wrong password is refused even when the user password is empty: pypdf only opens such a document as
    # the user when it is handed the empty password, which is what a reader that asks for nothing passes.
    for label, password, expected in (("user password", entry["userPassword"], PasswordType.USER_PASSWORD),
                                      ("owner password", entry["ownerPassword"], PasswordType.OWNER_PASSWORD),
                                      ("wrong password", WRONG_PASSWORD, PasswordType.NOT_DECRYPTED)):
        answer = PdfReader(str(path)).decrypt(password)

        if answer != expected:
            problems.append(f"answered {answer.name} for the {label}, expected {expected.name}")

    return problems


READERS = {"qpdf": check_qpdf, "MuPDF": check_mupdf, "pypdf": check_pypdf}


def main() -> None:
    if len(sys.argv) != 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        sys.exit(2)

    corpus = Path(sys.argv[1])
    manifest = json.loads((corpus / "manifest.json").read_text(encoding="utf-8"))
    failures = 0

    for entry in manifest["encryption"]:
        print(f"== {entry['file']}  ({entry['algorithm']}, V {entry['version']} R {entry['revision']})")

        for name, check in READERS.items():
            try:
                problems = check(corpus / entry["file"], entry)
            except Exception as error:  # noqa: BLE001 - a reader that cannot open the file is the finding
                problems = [f"{type(error).__name__}: {error}"]

            failures += len(problems)
            print(f"  {name:6}: " + ("passwords, revision and permissions agree" if not problems else "FAILED"))

            for problem in problems:
                print(f"    {problem}")

    print()
    print("encryption matrix: " + ("every reader agrees" if failures == 0 else f"{failures} failure(s)"))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
