"""Measures the library's PDF/A conversion against the veraPDF test corpus, with veraPDF as the judge.

Run with:  python docs/tools/validate-preflight-corpus.py --corpus <veraPDF-corpus> --probe <SharpCorePDF.Validation.dll>
               [--verapdf <launcher>] [--profiles 1b,2b,2u,3b,4] [--limit N] [--report <file.json>]
               [--max-false-success N] [--work <dir>]

The corpus (https://github.com/veraPDF/veraPDF-corpus) holds, per profile, small documents that each pass or
fail one clause of ISO 19005, and says which in the file name (`-pass-` or `-fail-`). The library is not a
PDF/A validator and does not claim to be one (constitution, Principle VII); what it claims is that when the
writer converts a document for a profile and reports the result as written, the result conforms, and that
when it cannot make the result conform it refuses instead. Both halves are measurable here:

  false success  the writer reported IsWritten, and veraPDF -- the reference implementation, pinned in
                 compliance.yml -- says the written file does not conform. This is the defect class that
                 matters: a caller archives a document on the strength of a claim that is false.
  refused        the writer named unsatisfied requirements instead of writing. On a `-fail-` input this is
                 the honest answer; on a `-pass-` input it is a refusal of a document that already conformed,
                 which is reported as `over-refusal` -- a usability cost, not a conformance one.
  fault / crash  the engine broke on the input (see corpus-robustness.py for what each means).

Without --verapdf the written results are not judged, and only refusals and faults are counted.
--max-false-success makes the run fail above that many false successes; it defaults to reporting only, so a
first run establishes the baseline the gate is then set from. Faults and crashes always fail the run.
"""

import argparse
import importlib.util
import json
import subprocess
import sys
import tempfile
from pathlib import Path

FOLDERS = {"1b": "PDF_A-1b", "2b": "PDF_A-2b", "2u": "PDF_A-2u", "3b": "PDF_A-3b", "4": "PDF_A-4"}


def load_validate_pdfa():
    """Imports validate-pdfa.py beside this file, so veraPDF is asked one way only."""
    spec = importlib.util.spec_from_file_location("validate_pdfa", Path(__file__).with_name("validate-pdfa.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def convert(dll: str, source: Path, profile: str, destination: Path, timeout: int) -> tuple[str, dict]:
    try:
        completed = subprocess.run(
            ["dotnet", dll, "probe", str(source), "--profile", profile, "--convert", str(destination), "--preflight-only"],
            capture_output=True, text=True, errors="replace", timeout=timeout, check=False)
    except subprocess.TimeoutExpired:
        return "timeout", {}

    archival = {}

    for line in completed.stdout.splitlines():
        if line.startswith("archival: "):
            archival = json.loads(line[len("archival: "):])

    outcome = {0: "read", 2: "refused-input", 3: "fault"}.get(completed.returncode, "crash")
    return outcome, archival


def judge(validate_pdfa, launcher: str, path: Path, profile: str) -> bool:
    code, output, _ = validate_pdfa.run(launcher, ["--format", "xml", "--flavour", profile, str(path)])
    return validate_pdfa.is_compliant("xml", code, output)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--probe", required=True)
    parser.add_argument("--verapdf")
    parser.add_argument("--profiles", default="1b,2b,2u,3b,4")
    parser.add_argument("--limit", type=int, default=0, help="at most this many files per profile, 0 for all")
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--max-false-success", type=int, default=-1)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--work", type=Path)
    options = parser.parse_args()

    validate_pdfa = load_validate_pdfa()
    launcher = validate_pdfa.find_verapdf(options.verapdf) if options.verapdf else None

    if options.verapdf and launcher is None:
        print(f"veraPDF was named but not found: {options.verapdf}")
        sys.exit(2)

    work = options.work or Path(tempfile.mkdtemp(prefix="scpdf-preflight-"))
    work.mkdir(parents=True, exist_ok=True)
    results = []
    totals: dict[str, dict[str, int]] = {}
    broken = 0

    for profile in options.profiles.split(","):
        folder = options.corpus / FOLDERS[profile]
        files = sorted(path for path in folder.rglob("*.pdf") if "-pass-" in path.name or "-fail-" in path.name)

        if options.limit:
            files = files[: options.limit]

        tally = totals.setdefault(profile, {})

        for index, source in enumerate(files):
            expected = "pass" if "-pass-" in source.name else "fail"
            destination = work / f"{profile}-{index:05d}.pdf"
            outcome, archival = convert(options.probe, source, profile, destination, options.timeout)

            if outcome in ("fault", "crash", "timeout"):
                verdict = outcome
                broken += 1
            elif outcome == "refused-input" or not archival.get("written"):
                verdict = "over-refusal" if expected == "pass" else "refused"
            elif launcher is None:
                verdict = "written (not judged)"
            else:
                verdict = "conforms" if judge(validate_pdfa, launcher, destination, profile) else "false success"

            tally[verdict] = tally.get(verdict, 0) + 1
            results.append({
                "profile": profile,
                "file": str(source.relative_to(options.corpus)),
                "expected": expected,
                "verdict": verdict,
                "unsatisfied": archival.get("unsatisfied", []),
            })

            if verdict in ("false success", "fault", "crash", "timeout"):
                print(f"{verdict:14} {profile:3} {source.relative_to(options.corpus)}")

            destination.unlink(missing_ok=True)

    print()
    for profile, tally in totals.items():
        print(f"PDF/A-{profile}: " + ", ".join(f"{count} {verdict}" for verdict, count in sorted(tally.items())))

    if options.report:
        options.report.parent.mkdir(parents=True, exist_ok=True)
        options.report.write_text(json.dumps({"totals": totals, "results": results}, indent=2), encoding="utf-8")

    false_success = sum(tally.get("false success", 0) for tally in totals.values())
    failed = broken > 0 or (options.max_false_success >= 0 and false_success > options.max_false_success)

    print(f"false successes: {false_success}, faults/crashes/timeouts: {broken}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
