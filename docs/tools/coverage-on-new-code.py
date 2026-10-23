"""Measures how much of the code a release added the suite actually executes (T103).

Run with:  python docs/tools/coverage-on-new-code.py artifacts/coverage/net11.0.xml
           python docs/tools/coverage-on-new-code.py artifacts/coverage/net11.0.xml --since v1.0.0 --threshold 90

The report is the Visual Studio coverage XML that `dotnet-coverage merge -f xml` writes, which is the same
file `ci.yml` hands to SonarCloud. It is produced by one instrumented run of the suite:

    ./.tools/dotnet-coverage collect -f coverage -o artifacts/coverage/net11.0.coverage ^
        "dotnet test ./tests/SharpCorePDF.Tests/SharpCorePDF.Tests.csproj -c Release -p:TargetFramework=net11.0"
    ./.tools/dotnet-coverage merge -f xml -o artifacts/coverage/net11.0.xml artifacts/coverage/net11.0.coverage

"New code" is what the diff adds, not what a file happens to be called: every line `git diff --unified=0`
reports as added to `src/` since `--since` (default `v1.0.0`, the last tagged release) is new. The
denominator is the added lines the report instruments — a comment, a blank line or a signature brace is not
executable and neither helps nor hurts — and the numerator is the added lines the suite visited. Only the
four shipped assemblies count; `SharpCorePDF.Tests` is in the same report and is skipped.

Two numbers are printed: the whole shipped surface, straight from the report's own module totals, and the
new code as defined above. The second is the gate: it exits 0 at or above `--threshold` (default 90), 1
below it, and 2 when the report or the diff cannot be read.

The tool only reads files and runs `git diff` in the working directory. It needs no third-party package.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path

SHIPPED = (
    "SharpCorePDF.dll",
    "SharpCorePDF.Domain.dll",
    "SharpCorePDF.Application.dll",
    "SharpCorePDF.Infrastructure.dll",
    "SharpCorePDF.DependencyInjection.dll",
)

HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")


def read_report(path: Path) -> tuple[dict[str, dict[int, bool]], dict[str, tuple[int, int]]]:
    """Returns the visited line per instrumented source line, and the module totals."""
    visited: dict[str, dict[int, bool]] = {}
    totals: dict[str, tuple[int, int]] = {}

    try:
        root = ElementTree.parse(path).getroot()
    except (OSError, ElementTree.ParseError) as error:
        raise SystemExit(f"coverage-on-new-code: cannot read the report {path}: {error}")

    for module in root.findall("./modules/module"):
        name = module.get("name", "")

        if name not in SHIPPED:
            continue

        covered = int(module.get("lines_covered", "0"))
        uncovered = int(module.get("lines_not_covered", "0"))
        totals[name] = (covered, covered + uncovered)

        sources = {
            source.get("id"): source.get("path", "")
            for source in module.findall("./source_files/source_file")
        }

        for hit in module.findall(".//range"):
            file = sources.get(hit.get("source_id"))

            if not file:
                continue

            key = normalise(file)
            lines = visited.setdefault(key, {})
            yes = hit.get("covered") == "yes"

            for line in range(int(hit.get("start_line", "0")), int(hit.get("end_line", "0")) + 1):
                lines[line] = lines.get(line, False) or yes

    if not totals:
        raise SystemExit("coverage-on-new-code: the report holds none of the shipped assemblies")

    return visited, totals


def normalise(path: str) -> str:
    """Returns one key per file, whatever separator or case the report used."""
    return str(Path(path).resolve()).replace("\\", "/").lower()


def added_lines(since: str, folder: str) -> dict[str, set[int]]:
    """Returns the lines `git diff --unified=0` reports as added under folder since the given ref."""
    try:
        diff = subprocess.run(
            ["git", "diff", "--unified=0", "--no-color", "--no-ext-diff", since, "--", folder],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        ).stdout
    except FileNotFoundError:
        raise SystemExit("coverage-on-new-code: git is not on PATH")
    except subprocess.CalledProcessError as error:
        raise SystemExit(f"coverage-on-new-code: git diff {since} -- {folder} failed: {error.stderr.strip()}")

    added: dict[str, set[int]] = {}
    current: set[int] = set()

    for text in diff.splitlines():
        if text.startswith("+++ "):
            target = text[4:].strip()

            if target.startswith("b/"):
                target = target[2:]

            current = added.setdefault(normalise(target), set())
        elif (hunk := HUNK.match(text)) is not None:
            start = int(hunk.group(1))
            count = int(hunk.group(2) or "1")
            current.update(range(start, start + count))

    return added


def main(argv: list[str]) -> int:
    """Prints the two measurements and returns the gate's verdict."""
    parser = argparse.ArgumentParser(
        description="Measures coverage on the code added since a ref, from a Visual Studio coverage XML.",
    )
    parser.add_argument("report", type=Path, help="the coverage XML that dotnet-coverage merge -f xml wrote")
    parser.add_argument("--since", default="v1.0.0", help="the ref the new code starts after (default: v1.0.0)")
    parser.add_argument("--folder", default="src", help="the tree the diff is taken over (default: src)")
    parser.add_argument("--threshold", type=float, default=90.0, help="the new-code gate in percent (default: 90)")
    parser.add_argument("--detail", action="store_true", help="one line per file with new code, not just the total")
    options = parser.parse_args(argv)

    visited, totals = read_report(options.report)

    print(f"report: {options.report} (one instrumented run)")
    print(f"new code: the lines added to {options.folder}/ since {options.since}\n")

    print(f"{'assembly':42} {'covered':>8} {'lines':>8} {'line coverage':>14}")

    for name in SHIPPED:
        if name in totals:
            covered, lines = totals[name]
            share = 0.0 if lines == 0 else 100.0 * covered / lines
            print(f"{name:42} {covered:8,} {lines:8,} {share:13.2f}%")

    added = added_lines(options.since, options.folder)
    rows: list[tuple[str, int, int]] = []

    for file, lines in added.items():
        instrumented = visited.get(file)

        if instrumented is None:
            continue

        new = {line for line in lines if line in instrumented}

        if new:
            rows.append((file, sum(1 for line in new if instrumented[line]), len(new)))

    if not rows:
        raise SystemExit("coverage-on-new-code: no added line of src/ is in the report")

    if options.detail:
        print()

        for file, covered, lines in sorted(rows):
            print(f"{file:42} {covered:8,} {lines:8,} {100.0 * covered / lines:13.2f}%")

    covered = sum(row[1] for row in rows)
    lines = sum(row[2] for row in rows)
    share = 100.0 * covered / lines

    print(f"\nnew code: {covered:,} of {lines:,} executable added lines covered ({share:.2f}%), "
          f"gate {options.threshold:.0f}%")

    if share < options.threshold:
        print(f"FAIL: {options.threshold:.0f}% or more of the new code has to be covered")
        return 1

    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
