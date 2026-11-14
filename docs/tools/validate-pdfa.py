"""Validates the PDF/A conformance of a document with an independent implementation (veraPDF).

Run with:  python docs/tools/validate-pdfa.py archive-2b.pdf --profile 2b
           python docs/tools/validate-pdfa.py --verapdf C:\\verapdf\\verapdf.bat archive-1b.pdf archive-3b.pdf
           python docs/tools/validate-pdfa.py --format xml --report-dir artifacts/pdfa archive-2b.pdf
           python docs/tools/validate-pdfa.py --require-version 1.30.4 --profile 2b archive-2b.pdf

The library writes the structures a profile requires; it does not claim conformance. This tool is the
claim: it runs veraPDF, the reference validator, over the file and reports PASS or FAIL together with the
rules that failed. A file this library wrote is only an archival master once this says so.

--profile decides the flavour veraPDF checks against (1b, 2b, 2u, 3b or 3u). Without it veraPDF detects the
flavour the document declares, which is what a receiving archive usually does as well.

--format decides how the verdict is reached. `text` keeps veraPDF's own report and reads its PASS line,
which is what a person runs by hand. `xml` reads the machine-readable report and decides from the
`isCompliant` attribute of the validation report, so a rule name that happens to spell PASS cannot pass for
a verdict; it also gives a run something to diff. --report-dir keeps that report, one file per document,
so a continuous-integration job can upload it as evidence.

--require-version states the smallest veraPDF this repository accepts, for example 1.30.4. An older
validator, or one whose version cannot be read, is a run that cannot answer the question: it exits 2
rather than reporting a pass from a rule set the repository no longer ships against.

veraPDF is found on PATH, through VERAPDF_HOME, or with --verapdf. It needs a Java runtime. Nothing here
is downloaded, and no document leaves the machine.

Run with --help to read this text again.
"""

import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path

FLAVOURS = ("1b", "2b", "2u", "3b", "3u")
FORMATS = ("text", "xml")
VERSION_PATTERN = re.compile(r"veraPDF\s+(\d+(?:\.\d+)+)")


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


def run(launcher: str, arguments: list[str]) -> tuple[int, str, str]:
    """Runs the launcher and returns its exit code, its standard output and its standard error."""
    command = [launcher, *arguments]

    # A .bat launcher is a shell script: Windows runs it through cmd, not as an executable image.
    if launcher.lower().endswith((".bat", ".cmd")):
        command = ["cmd", "/c", *command]

    try:
        completed = subprocess.run(command, capture_output=True, text=True, check=False)
    except OSError as error:
        raise RuntimeError(f"could not run veraPDF: {error}") from error

    return completed.returncode, completed.stdout or "", completed.stderr or ""


def read_version(launcher: str) -> str | None:
    """Reads the version veraPDF reports, or None when it cannot be read."""
    try:
        _, output, error = run(launcher, ["--version"])
    except RuntimeError:
        return None

    match = VERSION_PATTERN.search(output + error)

    return match.group(1) if match else None


def as_numbers(version: str) -> tuple[int, ...]:
    """Turns a dotted version into comparable numbers, padded so 1.30 and 1.30.0 agree."""
    numbers = tuple(int(part) for part in version.split(".") if part.isdigit())

    return numbers + (0,) * (3 - len(numbers))


def is_compliant(output_format: str, exit_code: int, output: str) -> bool:
    """Decides conformance from the machine-readable report, or from veraPDF's own text."""
    if output_format == "text":
        return exit_code == 0 and "PASS" in output

    try:
        root = ElementTree.fromstring(output)
    except ElementTree.ParseError:
        return False

    reports = root.findall(".//validationReport")

    if reports:
        return all(report.get("isCompliant") == "true" for report in reports)

    summary = root.find(".//validationReports")

    return (
        summary is not None
        and summary.get("compliant") == "1"
        and summary.get("nonCompliant") == "0"
    )


def keep_report(folder: str, path: Path, profile: str | None, output_format: str, output: str) -> Path:
    """Writes the raw report beside the run, so a CI job can upload it as evidence."""
    directory = Path(folder)
    directory.mkdir(parents=True, exist_ok=True)

    suffix = "xml" if output_format == "xml" else "txt"
    name = f"{path.stem}.{profile or 'detected'}.{suffix}"
    target = directory / name

    target.write_text(output, encoding="utf-8")

    return target


def describe(output_format: str, output: str) -> list[str]:
    """Summarises a machine-readable report: its statement, its counts and the rules that failed."""
    if output_format != "xml":
        return []

    try:
        root = ElementTree.fromstring(output)
    except ElementTree.ParseError:
        return []

    lines = []

    for report in root.findall(".//validationReport"):
        if report.get("statement"):
            lines.append(f"statement: {report.get('statement')}")

        details = report.find("details")

        if details is not None:
            lines.append(
                f"rules    : {details.get('passedRules')} passed, {details.get('failedRules')} failed"
            )

        for rule in report.findall(".//rule[@status='failed']"):
            clause = rule.get("clause") or "?"
            number = rule.get("testNumber") or "?"
            description = rule.get("description")

            # veraPDF sometimes states the reason on the check rather than on the rule.
            if not description:
                error = rule.find(".//error")
                description = error.get("message") if error is not None else None

            lines.append(f"rule     : {clause}-{number}: {description or 'no description'}")

    return lines


def check(
    launcher: str,
    version: str | None,
    path: Path,
    profile: str | None,
    quiet: bool,
    output_format: str,
    report_dir: str | None,
) -> int:
    """Validates one document and returns the number of failures it contributes."""
    if not path.exists():
        print(f"== {path}: not found")
        return 1

    arguments = ["--format", "xml"] if output_format == "xml" else ["--format", "text", "--verbose"]

    if profile:
        arguments += ["--flavour", profile]

    arguments.append(str(path))

    try:
        exit_code, output, error = run(launcher, arguments)
    except RuntimeError as failure:
        print(f"== {path}: {failure}")
        return 1

    combined = output + error
    passed = is_compliant(output_format, exit_code, output if output_format == "xml" else combined)
    summary = describe(output_format, output)

    print(f"== {path}")
    print(f"  profile  : {profile or 'detected by veraPDF'}")
    print(f"  veraPDF  : {version or 'unknown version'}")
    print(f"  result   : {'PASS' if passed else 'FAIL'}")

    if report_dir:
        kept = keep_report(report_dir, path, profile, output_format, output or combined)
        print(f"  report   : {kept}")

    # The machine-readable report says why in a line or two; the text report is the verdict itself, so
    # it is printed as veraPDF wrote it.
    detail = summary or [line.strip() for line in combined.splitlines() if line.strip()]

    if not passed or not quiet:
        for line in detail:
            print(f"  {line}")

    return 0 if passed else 1


def take_option(arguments: list[str], name: str) -> str | None:
    """Removes an option and its value from the arguments and returns the value."""
    if name not in arguments:
        return None

    index = arguments.index(name)

    if index + 1 >= len(arguments):
        print(f"{name} needs a value")
        print(__doc__)
        sys.exit(2)

    value = arguments[index + 1]
    del arguments[index : index + 2]

    return value


def main():
    arguments = sys.argv[1:]

    if any(argument in ("--help", "-h") for argument in arguments):
        print(__doc__)
        sys.exit(0)

    profile = take_option(arguments, "--profile")

    if profile is not None and profile not in FLAVOURS:
        print(f"--profile needs one of: {', '.join(FLAVOURS)}")
        print(__doc__)
        sys.exit(2)

    output_format = take_option(arguments, "--format") or "text"

    if output_format not in FORMATS:
        print(f"--format needs one of: {', '.join(FORMATS)}")
        print(__doc__)
        sys.exit(2)

    required_version = take_option(arguments, "--require-version")
    report_dir = take_option(arguments, "--report-dir")
    launcher_argument = take_option(arguments, "--verapdf")
    quiet = "--quiet" in arguments

    if quiet:
        arguments.remove("--quiet")

    if not arguments:
        print(__doc__)
        sys.exit(2)

    launcher = find_verapdf(launcher_argument)

    if launcher is None:
        print("veraPDF was not found: put it on PATH, set VERAPDF_HOME, or pass --verapdf <path>.")
        sys.exit(2)

    version = read_version(launcher)

    # A validator older than the one this repository ships against answers a question nobody asked,
    # so the run stops at the version rather than reporting a pass from an older rule set.
    if required_version is not None and (
        version is None or as_numbers(version) < as_numbers(required_version)
    ):
        found = "the version cannot be read" if version is None else f"this is {version}"

        print(f"veraPDF {required_version} or newer is required, but {found}.")
        sys.exit(2)

    failures = 0

    for argument in arguments:
        failures += check(
            launcher, version, Path(argument), profile, quiet, output_format, report_dir
        )

    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
