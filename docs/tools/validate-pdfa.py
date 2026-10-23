"""Validates the PDF/A conformance of a document with an independent implementation (veraPDF).

Run with:  python docs/tools/validate-pdfa.py archive-2b.pdf --profile 2b
           python docs/tools/validate-pdfa.py --verapdf C:\\verapdf\\verapdf.bat archive-1b.pdf archive-3b.pdf
           python docs/tools/validate-pdfa.py --profile 2b --quiet archive-2b.pdf

The library writes the structures a profile requires; it does not claim conformance. This tool is the
claim: it runs veraPDF, the reference validator, over the file and reports PASS or FAIL together with the
rules that failed. A file this library wrote is only an archival master once this says so.

--profile decides the flavour veraPDF checks against (1b, 2b or 3b). Without it veraPDF detects the
flavour the document declares, which is what a receiving archive usually does as well.

veraPDF is found on PATH, through VERAPDF_HOME, or with --verapdf. It needs a Java runtime. Nothing here
is downloaded, and no document leaves the machine.

Run with --help to read this text again.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path

FLAVOURS = ("1b", "2b", "3b")


def find_verapdf(explicit: str | None) -> str | None:
    """Finds the veraPDF launcher: the path given, VERAPDF_HOME, or PATH."""
    if explicit:
        return explicit if Path(explicit).exists() else None

    home = os.environ.get("VERAPDF_HOME")

    if home:
        for name in ("verapdf.bat", "verapdf"):
            candidate = Path(home) / name

            if candidate.exists():
                return str(candidate)

    return shutil.which("verapdf") or shutil.which("verapdf.bat")


def build_command(launcher: str, path: Path, profile: str | None) -> list[str]:
    """Builds the command line, wrapping a batch file the way Windows expects."""
    arguments = ["--format", "text", "--verbose"]

    if profile:
        arguments += ["--flavour", profile]

    arguments.append(str(path))
    command = [launcher, *arguments]

    # A .bat launcher is a shell script: Windows runs it through cmd, not as an executable image.
    if launcher.lower().endswith((".bat", ".cmd")):
        return ["cmd", "/c", *command]

    return command


def check(launcher: str, path: Path, profile: str | None, quiet: bool) -> int:
    """Validates one document and returns the number of failures it contributes."""
    if not path.exists():
        print(f"== {path}: not found")
        return 1

    try:
        completed = subprocess.run(
            build_command(launcher, path, profile),
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as error:
        print(f"== {path}: could not run veraPDF: {error}")
        return 1

    output = (completed.stdout or "") + (completed.stderr or "")
    passed = completed.returncode == 0 and "PASS" in output

    print(f"== {path}")
    print(f"  profile  : {profile or 'detected by veraPDF'}")
    print(f"  result   : {'PASS' if passed else 'FAIL'}")

    if not passed:
        for line in output.splitlines():
            if line.strip():
                print(f"  {line.strip()}")

        return 1

    if not quiet:
        for line in output.splitlines():
            if line.strip():
                print(f"  {line.strip()}")

    return 0


def main():
    arguments = sys.argv[1:]
    profile = None
    launcher_argument = None
    quiet = False

    if any(argument in ("--help", "-h") for argument in arguments):
        print(__doc__)
        sys.exit(0)

    while "--profile" in arguments:
        index = arguments.index("--profile")

        if index + 1 >= len(arguments) or arguments[index + 1] not in FLAVOURS:
            print(f"--profile needs one of: {', '.join(FLAVOURS)}")
            print(__doc__)
            sys.exit(2)

        profile = arguments[index + 1]
        del arguments[index : index + 2]

    if "--verapdf" in arguments:
        index = arguments.index("--verapdf")

        if index + 1 >= len(arguments):
            print("--verapdf needs the path of the veraPDF launcher")
            print(__doc__)
            sys.exit(2)

        launcher_argument = arguments[index + 1]
        del arguments[index : index + 2]

    if "--quiet" in arguments:
        quiet = True
        arguments.remove("--quiet")

    if not arguments:
        print(__doc__)
        sys.exit(2)

    launcher = find_verapdf(launcher_argument)

    if launcher is None:
        print("veraPDF was not found: put it on PATH, set VERAPDF_HOME, or pass --verapdf <path>.")
        sys.exit(2)

    failures = 0

    for argument in arguments:
        failures += check(launcher, Path(argument), profile, quiet)

    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
