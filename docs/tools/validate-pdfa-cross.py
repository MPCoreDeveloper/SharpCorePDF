"""Validates an archival document with more than one implementation, and reports where they agree.

Run with:  python docs/tools/validate-pdfa-cross.py --profile 1b archive-1b.pdf
           python docs/tools/validate-pdfa-cross.py --profile 2b --pdfbox preflight-app-3.0.8.jar archive-2b.pdf
           python docs/tools/validate-pdfa-cross.py --strict-tools --profile 3b archive-3b.pdf

Why this exists beside validate-pdfa.py: one validator is one implementation, and an implementation can be
wrong in a way nobody inside a repository that trusts it can see. veraPDF is the reference for PDF/A and
stays the authority; this tool adds two independent readings of the same bytes.

    veraPDF        the reference implementation, every profile this repository writes, asked through
                   validate-pdfa.py so the version guard and the verdict come from one place.
    PDFBox         Apache PDFBox preflight, which shares no code with veraPDF and none with this library.
                   PDFBox 3.0.8 implements PDF/A-1 only -- its Format enum has PDF_A1B and PDF_A1A -- so it
                   votes on `1b` and, for `2b`/`3b`, is asked only to `parse` the file. That is the honest
                   smaller claim: a different parser read the cross-reference table, the streams and the
                   filters. It is never reported as a conformance verdict.
    Ghostscript    `-sDEVICE=pdfwrite -dPDFA=<n>`, which makes a third implementation read the document and
                   write an archival one from what it understood. Like the PDFBox parse it is not a
                   verdict; it is evidence that the file is not only readable by the tool that wrote it.

A tool that is not installed is reported as `n/a` rather than silently skipped; --strict-tools turns that
into a failure, which is what a continuous-integration run passes. Nothing is downloaded and no document
leaves the machine.

Exit codes: 0 every check that ran agreed, 1 a check failed, 2 a required tool is missing or unusable.
"""

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

PROFILES = ("1b", "2b", "2u", "3b", "3u")
PDFBOX_MODES = {"1b": "1b", "2b": "parse", "2u": "parse", "3b": "parse", "3u": "parse"}
JAVA_SOURCE = Path(__file__).with_name("pdfa-preflight") / "Preflight.java"


def find_tool(explicit: str | None, names: tuple[str, ...], home_var: str | None = None) -> str | None:
    """Finds a tool: the path given, a home variable it is installed under, or PATH."""
    if explicit:
        return explicit if Path(explicit).exists() else None

    if home_var and os.environ.get(home_var):
        home = Path(os.environ[home_var])

        for name in names:
            candidate = home / name

            if candidate.exists():
                return str(candidate)

    for name in names:
        found = shutil.which(name)

        if found:
            return found

    return None


def find_pdfbox_jar(explicit: str | None) -> str | None:
    """Finds the preflight-app jar: the path given, PDFBOX_PREFLIGHT_JAR, or a copy beside the tree."""
    if explicit:
        return explicit if Path(explicit).exists() else None

    from_environment = os.environ.get("PDFBOX_PREFLIGHT_JAR")

    if from_environment and Path(from_environment).exists():
        return from_environment

    for candidate in sorted(Path.cwd().glob("preflight-app-*.jar")):
        return str(candidate)

    return None


def pdfbox_version(jar: str) -> str:
    """Reads the version out of the jar's own name, which is where the pin is stated."""
    name = Path(jar).stem

    return name.rsplit("-", 1)[-1] if "-" in name else "unknown"


def compile_pdfbox(jar: str) -> Path | None:
    """Compiles the small preflight runner against the jar, once per run."""
    javac = shutil.which("javac")

    if javac is None:
        return None

    classes = Path(tempfile.mkdtemp(prefix="pdfa-preflight-"))
    completed = subprocess.run(
        [javac, "-cp", jar, "-d", str(classes), str(JAVA_SOURCE)],
        capture_output=True,
        text=True,
        check=False,
    )

    if completed.returncode != 0:
        print(f"the preflight runner did not compile against {jar}:")
        print(completed.stdout + completed.stderr)

        return None

    return classes


def check_verapdf(
    file: Path, profile: str | None, launcher: str | None, required_version: str | None
) -> tuple[bool | None, str, str]:
    """Asks validate-pdfa.py, so the reference verdict has one implementation in this repository."""
    script = Path(__file__).with_name("validate-pdfa.py")
    arguments = [sys.executable, str(script), "--format", "xml", "--quiet"]

    if profile:
        arguments += ["--profile", profile]

    if launcher:
        arguments += ["--verapdf", launcher]

    if required_version:
        arguments += ["--require-version", required_version]

    arguments.append(str(file))

    completed = subprocess.run(arguments, capture_output=True, text=True, check=False)
    output = ((completed.stdout or "") + (completed.stderr or "")).strip()

    # Exit 2 is "cannot answer", which is not the same finding as a document that failed.
    if completed.returncode == 2:
        return None, "unavailable", output

    return completed.returncode == 0, "PASS" if completed.returncode == 0 else "FAIL", output


def check_pdfbox(
    file: Path, profile: str | None, jar: str, classes: Path
) -> tuple[bool | None, str, str]:
    """Runs the preflight runner in the mode the profile allows, and reads its one line of output."""
    java = shutil.which("java")

    if java is None:
        return None, "unavailable", "no java on PATH"

    mode = PDFBOX_MODES.get(profile or "", "parse")
    classpath = jar + os.pathsep + str(classes)
    completed = subprocess.run(
        [java, "-cp", classpath, "Preflight", mode, str(file)],
        capture_output=True,
        text=True,
        check=False,
    )

    # PDFBox writes font warnings to standard error; its own answer is the first line of standard output.
    lines = [line for line in (completed.stdout or "").splitlines() if line.strip()]
    answer = lines[0] if lines else ""
    passed = completed.returncode == 0 and answer.startswith(("PASS", "PARSED"))

    if mode == "parse":
        return passed, ("parsed" if passed else "did not parse"), "" if passed else answer

    return passed, ("PASS" if passed else "FAIL"), "" if passed else "\n".join(lines)


def check_ghostscript(
    file: Path, profile: str | None, executable: str
) -> tuple[bool | None, str, str]:
    """Rewrites the document as archival output, which is a reader test rather than a verdict."""
    part = {"1b": "1", "2b": "2", "2u": "2", "3b": "3", "3u": "3"}.get(profile or "", "2")

    with tempfile.TemporaryDirectory(prefix="pdfa-gs-") as folder:
        output = Path(folder) / "rewritten.pdf"
        completed = subprocess.run(
            [
                executable,
                "-q",
                "-dNOPAUSE",
                "-dBATCH",
                "-sDEVICE=pdfwrite",
                f"-dPDFA={part}",
                # Policy 1 keeps a feature the device cannot express instead of aborting: this is a
                # reader test, and a rewrite that stopped at the first unsupported feature would say
                # more about Ghostscript's colour model than about the document.
                "-dPDFACompatibilityPolicy=1",
                "-sColorConversionStrategy=UseDeviceIndependentColor",
                f"-sOutputFile={output}",
                str(file),
            ],
            capture_output=True,
            text=True,
            check=False,
        )

        passed = completed.returncode == 0 and output.exists() and output.stat().st_size > 0
        detail = "" if passed else ((completed.stdout or "") + (completed.stderr or "")).strip()

        return passed, ("rewritten" if passed else "refused"), detail


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

    if profile is not None and profile not in PROFILES:
        print(f"--profile needs one of: {', '.join(PROFILES)}")
        print(__doc__)
        sys.exit(2)

    launcher = take_option(arguments, "--verapdf")
    jar_argument = take_option(arguments, "--pdfbox")
    ghostscript_argument = take_option(arguments, "--ghostscript")
    required_version = take_option(arguments, "--require-version")
    strict_tools = "--strict-tools" in arguments

    if strict_tools:
        arguments.remove("--strict-tools")

    if not arguments:
        print(__doc__)
        sys.exit(2)

    vera = find_tool(launcher, ("verapdf.bat", "verapdf"), "VERAPDF_HOME")
    jar = find_pdfbox_jar(jar_argument)
    ghostscript = find_tool(ghostscript_argument, ("gs", "gswin64c", "gswin32c"))

    classes = compile_pdfbox(jar) if jar else None

    # A tool that is missing is named once, before the documents, so a green table cannot be mistaken for
    # a complete one.
    absent = [
        name
        for name, found in (
            ("veraPDF", vera),
            ("PDFBox preflight", jar if classes else None),
            ("Ghostscript", ghostscript),
        )
        if not found
    ]

    if absent:
        print(f"not installed, reported as n/a: {', '.join(absent)}")

    if jar:
        print(f"PDFBox preflight {pdfbox_version(jar)} (PDF/A-1 only; other profiles are parsed, not judged)")

    failures = 0

    for argument in arguments:
        file = Path(argument)

        print(f"== {file} ({profile or 'profile detected by veraPDF'})")

        if not file.exists():
            print("   not found")
            failures += 1
            continue

        checks = [
            ("veraPDF", *check_verapdf(file, profile, launcher, required_version)),
        ]

        if jar and classes:
            checks.append(("PDFBox", *check_pdfbox(file, profile, jar, classes)))

        if ghostscript:
            checks.append(("Ghostscript", *check_ghostscript(file, profile, ghostscript)))

        for name, passed, label, detail in checks:
            printed = "n/a" if passed is None else label
            print(f"   {name:<12} {printed}")

            if passed is False and detail:
                for line in detail.splitlines():
                    if line.strip():
                        print(f"   {'':<12} {line.strip()}")

            if passed is False:
                failures += 1

    if failures > 0:
        print(f"{failures} check(s) failed")

        sys.exit(1)

    if strict_tools and absent:
        print("--strict-tools was passed and not every tool was found")

        sys.exit(2)

    sys.exit(0)


if __name__ == "__main__":
    main()

