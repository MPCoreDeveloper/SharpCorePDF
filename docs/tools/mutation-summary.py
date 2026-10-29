"""Says what a Stryker.NET mutation report measured (T104).

Run with:  python docs/tools/mutation-summary.py artifacts/mutation/SharpCorePDF.Domain/reports/mutation-report.json
           python docs/tools/mutation-summary.py <report> --detail --markdown
           python docs/tools/mutation-summary.py <report> --compare <earlier-report> --detail

The report is the JSON one `stryker-config.json` asks for, written by `.github/workflows/mutation.yml`:

    ./.tools/dotnet-stryker --config-file stryker-config.json --project SharpCorePDF.Domain.csproj ^
        --output artifacts/mutation/SharpCorePDF.Domain

Coverage answers whether a line was executed; a mutation report answers whether the suite would notice a
line that stopped doing its job. Stryker changes one thing at a time -- an operator, a boundary, a
returned constant, a removed statement -- compiles the assembly and runs the tests that cover the change.
A mutant that makes a test fail is killed, and the suite is doing its job there; one that leaves every
test green has survived, and the line it changed is a line no test pins down.

The arithmetic is Stryker's own. Detected is killed plus timed out (a suite that hangs on a mutant has
noticed it); undetected is survived plus no coverage (no test reaches the line, which is why nothing
noticed). Ignored mutants -- outside the `--mutate` filter, or silenced by `ignore-mutations` -- and
mutants that could not compile take part in neither: they measure the filter and the compiler rather than
the suite, so counting them as survivors would turn the score into a report about configuration.

The score is printed to two decimals, with a verdict when `--threshold` is given, which nothing in this
repository asks for yet: `mutation.yml` reports rather than gates, and `break-at` is 0 in the config. The
exit code is 0 for a report it could read and 1 for one below `--threshold`; a report it cannot read stops
with a message rather than a score.

The last form is for a score that moved. Two runs of one tree either agree mutant for mutant -- in which case
the number belongs to the tree -- or they do not, and the run that moved is then the finding: an assertion that
fails only sometimes, a test that hangs on a loaded machine, a report written against a different tree.
Comparing two scores cannot tell those apart. Comparing verdicts by what each mutant changes can, which is why
`--compare` lists them by file, line, mutator and replacement rather than by the id a run happened to give them.

The tool only reads the report. It needs no third-party package and no network.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# Stryker's own arithmetic, in one place so the summary cannot drift from the report it summarises.
DETECTED = ("Killed", "Timeout")
UNDETECTED = ("Survived", "NoCoverage")

# A mutant that did not compile is excluded rather than counted against the suite: the compiler refused
# the mutation, so no test was ever asked about it.
BROKEN = ("CompileError", "RuntimeError")

# (file, line, mutator, replacement, status) -- one row per mutant, in report order.
Mutant = tuple[str, int, str, str, str]


def read_report(path: Path) -> list[Mutant]:
    """Returns every mutant the report names, with the status Stryker gave it."""
    try:
        report = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f"mutation-summary: cannot read the report {path}: {error}")

    files = report.get("files") or {}

    if not files:
        raise SystemExit(f"mutation-summary: the report {path} names no file")

    mutants: list[Mutant] = []

    for name, entry in files.items():
        for mutant in entry.get("mutants") or []:
            start = (mutant.get("location") or {}).get("start") or {}
            mutants.append(
                (
                    short_name(name),
                    int(start.get("line") or 0),
                    str(mutant.get("mutatorName") or "?"),
                    str(mutant.get("replacement") or ""),
                    str(mutant.get("status") or "Unknown"),
                )
            )

    return mutants


def short_name(name: str) -> str:
    """Turns the report's absolute path into the `Layer/File.cs` a reader recognises."""
    parts = name.replace("\\", "/").split("/")
    return "/".join(parts[-2:]) if len(parts) > 1 else name


def score(detected: int, undetected: int) -> float | None:
    """Returns the mutation score in percent, or None when no mutant was tested."""
    tested = detected + undetected
    return None if tested == 0 else 100.0 * detected / tested


def per_file(mutants: list[Mutant]) -> list[tuple[str, int, int, int]]:
    """Returns (file, detected, undetected, excluded) per file, worst score first."""
    tallies: dict[str, list[int]] = {}

    for name, _line, _mutator, _replacement, status in mutants:
        counts = tallies.setdefault(name, [0, 0, 0])

        if status in DETECTED:
            counts[0] += 1
        elif status in UNDETECTED:
            counts[1] += 1
        else:
            counts[2] += 1

    rows = [(name, counts[0], counts[1], counts[2]) for name, counts in tallies.items()]
    measured = [row for row in rows if row[1] + row[2] > 0]
    return sorted(measured, key=lambda row: (score(row[1], row[2]) or 0.0, row[0]))



def table(headers: list[str], rows: list[list[str]]) -> list[str]:
    """Returns a markdown table: a header, a rule and the rows."""
    return ["| " + " | ".join(headers) + " |", "| " + " | ".join("---" for _ in headers) + " |"] + [
        "| " + " | ".join(row) + " |" for row in rows
    ]


def escape(text: str) -> str:
    """Keeps a replacement from breaking the table it is printed in."""
    return text.replace("|", "/").replace("\n", " ").replace("`", "'")


def score_text(measured: float | None) -> str:
    """Returns a score as a table cell."""
    return "not measured" if measured is None else f"{measured:.2f} %"


def summarise(mutants: list[Mutant], detail: bool, markdown: bool, top: int) -> list[str]:
    """Returns the summary as plain text, or as the markdown a run summary takes."""
    detected = sum(1 for mutant in mutants if mutant[4] in DETECTED)
    undetected = sum(1 for mutant in mutants if mutant[4] in UNDETECTED)
    broken = sum(1 for mutant in mutants if mutant[4] in BROKEN)
    ignored = len(mutants) - detected - undetected - broken
    measured = score(detected, undetected)
    lines: list[str] = []

    if markdown:
        lines.append(f"### Mutation score: {score_text(measured)}")
        lines.append("")
        lines += table(
            ["Mutants", "Detected", "Undetected", "Compile errors", "Ignored"],
            [[f"{len(mutants):,}", f"{detected:,}", f"{undetected:,}", f"{broken:,}", f"{ignored:,}"]],
        )
    else:
        lines.append(f"mutants: {len(mutants):,}")
        lines.append(
            f"  detected: {detected:,}  undetected: {undetected:,}  compile errors: {broken:,}  ignored: {ignored:,}"
        )
        lines.append(f"mutation score: {score_text(measured)}  (detected / (detected + undetected))")

    if not detail:
        return lines

    files = per_file(mutants)

    if files:
        lines += ["", "Worst files first:" if markdown else "worst files first:", ""]
        lines += table(
            ["File", "Detected", "Undetected", "Excluded", "Score"],
            [
                [f"`{name}`", str(killed), str(survived), str(excluded), score_text(score(killed, survived))]
                for name, killed, survived, excluded in files[:top]
            ],
        )

    survivors = [mutant for mutant in mutants if mutant[4] in UNDETECTED]

    if survivors:
        lines += ["", "#### What survived" if markdown else "what survived:", ""]
        lines += table(
            ["File", "Line", "Mutator", "Status", "Replacement"],
            [
                [f"`{name}`", str(line), mutator, status, f"`{escape(replacement)}`"]
                for name, line, mutator, replacement, status in survivors[:top]
            ],
        )

    return lines


def key(mutant: Mutant) -> tuple[str, int, str, str]:
    """Identifies a mutant by what it changes, so two runs can be compared without agreeing on ids."""
    name, line, mutator, replacement, _status = mutant
    return (name, line, mutator, replacement)


def compare(earlier: list[Mutant], later: list[Mutant], markdown: bool, top: int) -> list[str]:
    """Returns what moved between two reports: the verdicts, not only the score they add up to."""
    before = {key(mutant): mutant[4] for mutant in earlier}
    moved: list[list[str]] = []

    for mutant in later:
        name, line, mutator, replacement, status = mutant
        was = before.get(key(mutant))

        if was is not None and was != status:
            moved.append([f"`{name}`", str(line), mutator, f"`{escape(replacement)}`", was, status])

    def tally(mutants: list[Mutant]) -> tuple[int, int, float | None]:
        """Returns (detected, undetected, score) for one report."""
        detected = sum(1 for mutant in mutants if mutant[4] in DETECTED)
        undetected = sum(1 for mutant in mutants if mutant[4] in UNDETECTED)
        return detected, undetected, score(detected, undetected)

    rows: list[list[str]] = []

    for label, mutants in (("earlier", earlier), ("later", later)):
        detected, undetected, measured = tally(mutants)
        rows.append([label, str(detected), str(undetected), score_text(measured)])

    lines = ["", "#### What moved" if markdown else "what moved between the two reports:", ""]
    lines += table(["Report", "Detected", "Undetected", "Score"], rows)
    lines += ["", "No verdict moved." if not moved else f"{len(moved)} verdicts moved:"]

    if moved:
        lines += [""]
        lines += table(["File", "Line", "Mutator", "Replacement", "Was", "Now"], moved[:top])

        if len(moved) > top:
            lines += ["", f"({len(moved) - top:,} more: raise --top to see them)"]

    absent = [name for name in before if name not in {key(mutant) for mutant in later}]
    added = [mutant for mutant in later if key(mutant) not in before]

    if absent or added:
        lines += [
            "",
            f"{len(absent):,} mutant(s) appear only in the earlier report and {len(added):,} only in this one: "
            "the code or the filter changed between the two runs.",
        ]

    return lines


def main(argv: list[str]) -> int:
    """Prints the summary and returns the verdict `--threshold` asks for."""
    parser = argparse.ArgumentParser(
        description="Summarises a Stryker.NET mutation report: what was killed and what survived.",
    )
    parser.add_argument("report", type=Path, help="the mutation-report.json Stryker wrote")
    parser.add_argument("--detail", action="store_true", help="the per-file table and the survivors, not just the total")
    parser.add_argument("--markdown", action="store_true", help="markdown, for a run summary or a pull request")
    parser.add_argument("--top", type=int, default=15, help="how many rows the detailed tables show (default: 15)")
    parser.add_argument(
        "--threshold",
        type=float,
        default=None,
        help="fail below this mutation score in percent; nothing in this repository asks for one yet",
    )
    parser.add_argument(
        "--compare",
        type=Path,
        metavar="EARLIER",
        default=None,
        help="a report of the same code from an earlier run; prints what moved between the two",
    )
    options = parser.parse_args(argv)

    mutants = read_report(options.report)

    if not options.markdown:
        print(f"report: {options.report}")

    print("\n".join(summarise(mutants, options.detail, options.markdown, options.top)))

    if options.compare is not None:
        print("\n".join(compare(read_report(options.compare), mutants, options.markdown, options.top)))

    if options.threshold is None:
        return 0

    measured = score(
        sum(1 for mutant in mutants if mutant[4] in DETECTED),
        sum(1 for mutant in mutants if mutant[4] in UNDETECTED),
    )

    if measured is None or measured < options.threshold:
        print(f"FAIL: {options.threshold:.0f}% or more of the tested mutants has to be detected")
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
