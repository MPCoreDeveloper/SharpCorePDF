<!--
  The template a pull request starts from. CONTRIBUTING.md has the long version; this is the part that has
  to be answered in the pull request itself, because it is what a reviewer reads first and what a future
  reader of the history reads instead of the diff.
-->

## What this changes

<!-- One paragraph. What a caller can do after this that they could not do before, or what stops going wrong. -->

## Why, and what is wrong today

<!--
  The defect or the gap, in terms of behaviour rather than of the code you touched. If it is a defect,
  say what it produced and what it should have produced. An issue number is welcome; a link to a
  gitignored planning document is not — nobody reviewing this can open it.
-->

## The test that fails without this change

<!--
  Name it. A test that passes before and after is documentation, not proof; CONTRIBUTING.md asks for the
  one that goes red when the change is reverted, and how you checked that it does.
-->

## Checklist

- [ ] `dotnet build .\SharpCorePDF.slnx -c Release` is clean on both legs, with **zero warnings** (not "no new warnings")
- [ ] A test fails when this change is reverted, and it is in this pull request
- [ ] `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` updated in the same commit — or the surface is unchanged
- [ ] `CHANGELOG.md` has an entry under `[Unreleased]`, or the change is not user-visible
- [ ] Documentation that made a claim this change alters (README, `examples/`, `docs/measurements.md`) is updated in this pull request
- [ ] Nothing from a gitignored planning document, and no internal story, epic or requirement identifier, appears in a comment, a commit message or this description
- [ ] The change is not drive-by: no reordering, renaming or reformatting outside what the change needs

## Anything else a reviewer should know

<!-- A trade-off taken, a number measured, a thing you deliberately did not do. -->
