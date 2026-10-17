# Contributing

SharpCorePDF reads and writes every PDF byte itself: no PdfSharp, no iText, no native library underneath.
That is the whole point of it, and it is also the reason a change here is held to a different bar than a
change in a wrapper — a bug in this repository is a bug in the file a caller hands to someone else.

This file is the practical part: how to build it, what a pull request has to clear, and what happens after
it is merged. The [Code of Conduct](CODE_OF_CONDUCT.md) applies to every part of it. Vulnerabilities go
through [SECURITY.md](SECURITY.md), never through a public issue.

## Getting set up

Two SDKs, not one:

```powershell
dotnet --list-sdks        # needs an 11.0.x band and a 10.0.x band
```

CI installs `11.0.x` and `10.0.x` for a reason worth knowing before your first build fails: the test project
is an executable, and the .NET 11 SDK alone cannot produce the `net10.0` app host it needs (`MSB3030`).
`global.json` pins the test runner, not the SDK band.

Then the whole thing:

```powershell
dotnet build .\SharpCorePDF.slnx -c Release
dotnet test  .\SharpCorePDF.slnx                      # both legs: net10.0 and net11.0
dotnet run --project .\examples\SharpCorePDF.Examples -c Release
```

`dotnet test` against the test project directly needs the target framework named, because the project
multi-targets:

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64
```

The `-r` is not decoration: the RID-less `net10.0` leg of some SDK builds skips the app host. On a
non-Windows machine use your own RID.

Optional but useful, when a change touches what a merge writes:

```powershell
# An independent reader, so "it opens in our library" is not the only evidence
uv run --with pikepdf python .\docs\tools\validate-pdf.py <file.pdf>

# The fixture corpus itself is generated, not committed by hand
uv run --with pikepdf python .\docs\tools\make-fixtures.py .\tests\SharpCorePDF.Tests\Assets
```

Neither Python tool runs in CI. They are what a maintainer runs before believing a result, and quoting their
output in a pull request is worth more than a description of it.

## What a pull request has to clear

1. **A test that fails when the behaviour is reverted.** Not "a test was added" — revert the fix locally,
   watch it go red, and mention that you did. This suite exists to catch regressions, not to record that
   code was written.
2. **The public API snapshot, in the same commit.** `PublicAPI.Shipped.txt` is the frozen surface;
   `PublicAPI.Unshipped.txt` takes the additions. `RS0016` (a public member the snapshot does not list) and
   `RS0017` (a snapshot entry that outlives its member) are build errors, so a forgotten entry fails the
   build rather than review — and an entry that changes is the clearest possible signal that a release is
   a breaking one.
3. **Zero warnings, with the analyzer's rules in the build.** The build is clean today on both legs, and
   `SonarAnalyzer.CSharp` runs the same rule set SonarCloud applies, so a finding appears at the line that
   causes it instead of after a push. A new warning is a regression like any other; `TreatWarningsAsErrors`
   already covers nullable and the two snapshot rules, and the rest is a habit. A rule this repository
   decides not to follow is silenced per file in `src/.editorconfig`, with the reason, and the line carries
   `// NOSONAR` so the server agrees — never by turning the rule off globally.
4. **The change keeps the 90% coverage target.** `codecov.yml` fails a pull request whose touched lines are
   below 90% covered, or whose project number falls below 90%. A change under `src/` without a test that
   exercises it is a change that fails the patch status; the test project's own coverage is excluded from
   the number, because a suite covering itself is not coverage of anything.
5. **PDF fixtures stay binary.** `.gitattributes` marks `*.pdf binary` on purpose: two AES fixtures carry
   no byte Git detects as binary, so without that rule a checkout rewrites them (1119 → 1155 bytes) and
   every cross-reference offset in them moves.
6. **No drive-by reformatting.** A diff that mixes a behaviour change with whitespace is a diff nobody can
   review, and this repository reviews diffs.
7. **Prose that matches the change.** The README and `docs/` state what the library refuses to do; if a
   change makes one of those sentences false, the sentence is part of the change.

The subject line says what the commit does, in the imperative, and the body says what breaks without it. A
pull request that cannot answer "what is wrong today" is a preference, not a fix.

## Changes that need an issue first

These are architectural, not incremental, and a pull request that arrives without the conversation is
likely to be declined on grounds that have nothing to do with its quality:

- **a runtime dependency** — the four layer packages have none beyond `Microsoft.Extensions.*`, and Domain
  has none at all. That is a feature, and it is what makes the allocation numbers in `docs/measurements.md`
  meaningful.
- **a native library or a platform-specific binary**, for the same reason plus a deployment one.
- **another target framework.** The two legs are a deliberate pair; adding `netstandard2.0` or `net8.0` is
  a reach decision with a build-matrix cost, not a fix.
- **anything that changes the shape of a public API** without saying so in the pull request. The snapshot
  will catch it; the reviewer still needs to know whether it was intended.

## Review, and what happens after a merge

One maintainer reviews here. An issue or a pull request gets a first answer within 7 days — the same
commitment `SECURITY.md` and `CODE_OF_CONDUCT.md` make, so that the slowest thing about this project is
never the silence.

Rights are granted on evidence, and the ladder is written down rather than assumed, because a contributor
who cannot see how to earn trust is being asked to work for free:

| Stage | What it lets you do | Earned by |
| --- | --- | --- |
| Contributor | open issues and pull requests | showing up |
| Triage | label, close duplicates, ask for a reproducer, mark a fix as verified | two merged pull requests |
| Reviewer | approve a change, including in the area you have been working in | five merged pull requests, with at least one that changed behaviour and one that only changed tests or docs |
| Committer | push to `main`, cut a preview, hold the snapshot discipline | six months of sustained contribution, and a stated commitment to points 1–3 above |

The ladder is not a formality: a project that never hands out rights is a project whose bus factor is one,
however good its code is.

## Licensing of contributions

Everything in this repository is MIT (see [LICENSE](LICENSE)), and contributions come in under the same
terms — inbound equals outbound, and no copyright assignment is asked for. There is no CLA today. If one is
ever introduced, it will be announced before it applies, it will never be retroactive, and the reason will
be stated: the .NET Foundation's eligibility criteria expect committers to be bound by one.

## Where to talk

The [issue tracker](https://github.com/MPCoreDeveloper/SharpCorePDF/issues) is the channel that exists
today, and it is the right place for a question that has an answer worth keeping — "how do I merge these
two documents and keep the bookmarks" belongs there as much as a bug does. A defect nobody can reproduce
yet is still worth filing: say what you expected and what you got, and the reproducer can follow in the
thread. End-to-end validation belongs in `tests/SharpCorePDF.Tests` once it is known, because a bug that
stays in the tracker is a bug the next consumer finds again.

GitHub Discussions is not enabled yet. When it is, questions move there and bugs stay in the tracker; this
paragraph will say so on the day it changes.

