# .NET Foundation application

The decision this document records: **the application is filed as soon as the eight conditions below are
all true, and not before.** It is not a "someday"; it is a gate with a checklist, so that the answer to
"when do we apply" is a state of the repository rather than a mood.

`docs/foundation-readiness.md` says where the project stands against each criterion today.
This document says when it is ready to file, and holds the answers, so that filing costs an hour.

## Definition of ready

All eight, checked in this order. The state column is what the repository said on 2026-10-17.

| # | Condition | State | Closed by |
| --- | --- | --- | --- |
| 1 | The three documents every criterion asks for exist at the repository root: `SECURITY.md`, `CODE_OF_CONDUCT.md`, `CONTRIBUTING.md` | **true** | done 2026-10-17 |
| 2 | No row of the eligibility table in `docs/foundation-readiness.md` sits below "Met" | **false** — three do (the CLA, automated dependency scanning, the public channel) | root-cause each: see that table |
| 3 | GitHub Discussions is enabled, or another public channel is named in the README | **false** — Discussions is off (`has_discussions: false`) | Settings → Features → Discussions |
| 4 | Private vulnerability reporting is enabled, so `SECURITY.md`'s first channel resolves | **unknown** — a settings switch, not a file | Settings → Code security |
| 5 | At least one **stable** release is on nuget.org, not a preview only | **true** — `1.0.0` is stable, listed since 2026-10-17, all five ids | done 2026-10-17 |
| 6 | 90 days have passed since that first stable release | **false** — the clock started 2026-10-17, so 2027-01-15 | clock |
| 7 | At least one issue or pull request exists from someone other than the maintainer | **false** — 0 / 0, one contributor | time, and a project people can find |
| 8 | The five owner decisions in `docs/foundation-readiness.md` are answered in writing, in this repository | **false** — two answered on 2026-10-17 (package id, account shape); three remain | the owner |

Why 5, 6 and 7 are in the list even though no criterion names a number: the Foundation's Activity metrics
(Packages, Releases, Issues, Pull Requests, Contributors) are read as evidence of a community, and a
repository whose first package is a preview and whose only contributor is its author has no evidence to
read. Condition 6 is the one that turns "thin activity" into "a project with a history"; applying before
it is allowed — the form says so — but it is filed as Seed either way, so there is nothing to gain by
arriving early with less to show.

**A criterion can be waived.** The Project Committee may grant a waiver for a specific criterion, and the
application form has room to argue one. Conditions 1–5 and 8 have no waiver worth asking for; 6 and 7 are
the two where "we know, here is why we are early" is a legitimate sentence rather than an excuse.

## Filing

1. Open **one** issue from the Foundation's template:
   <https://github.com/dotnet-foundation/projects/issues/new?template=application.yml> — it applies the
   labels `project application` and `project support` and assigns the committee members itself.
2. Paste the answers from the section below. Do not paraphrase them at the last minute: they were written
   from the repository, and the repository is what a reviewer will read.
3. Expect the sequence in `docs/foundation-readiness.md`: review by the committee, then the Board (monthly),
   then the CLA through DocuSign, then the onboarding checklist.
4. Keep the issue updated rather than opening a second one. It is the record of the application.

What must not go into it: a link to `_bmad-output/` (gitignored — the reviewer would find nothing), a claim
about a differentiator that `docs/measurements.md` does not back, or an issue count inflated by filing
against yourself.

## The answers

Written from the repository on 2026-10-17, so that they can be pasted rather than remembered. Where a field
is the owner's to fill, it says so instead of guessing.

### 1–4. Project information and contacts

| Field | Answer |
| --- | --- |
| Project name | SharpCorePDF |
| Description | Dependency-injection first, domain-driven PDF library for .NET 10 and .NET 11: merges whole documents or selected pages, with every PDF byte read and written in-house. *(This is the repository description, verbatim — the same sentence a visitor and a reviewer see.)* |
| License | MIT. `LICENSE` at the root, `PackageLicenseExpression` = MIT, and GitHub reports SPDX `mit`. No third-party code is vendored, so there is nothing to reconcile. |
| Source | <https://github.com/MPCoreDeveloper/SharpCorePDF> |
| Repository age | created 2026-10-15; 13 commits at `477f8e1` |
| Packages | five: `SharpCorePDF` — the entry point, a meta-package with no assemblies of its own, which is the one to install — plus `SharpCorePDF.Domain`, `.Application`, `.Infrastructure`, `.DependencyInjection` |
| Maintainer | M. Posseth — GitHub [@MPCoreDeveloper](https://github.com/MPCoreDeveloper), [posseth.com](https://www.posseth.com) |
| Contact for the application | the form at <https://www.posseth.com/contact>; no address is published, which is the same channel `SECURITY.md` names |
| Account shape | a **User** account, not an Organization, and decided on 2026-10-17 that it stays one: this is one person's project, the maintainer's other work is private so there is no second public face to put behind an Organization, and an Organization with a single member is a structure with nobody to staff it. Relevant to the 2FA item, see §5 |
| Home page | the README is the page. GitHub's `homepage` field is empty; the description is filled. |

### 5. Eligibility and technical checklist

The form's checklist, answered from evidence rather than intent.

| The form asks | Answer | Evidence |
| --- | --- | --- |
| The project has a published Security Policy | **yes**, since 2026-10-17 | `SECURITY.md` — scope, two private channels, 7-day acknowledgement, safe harbour |
| The project has a public issue tracker | **yes** | Issues enabled, 0 open, 0 closed |
| The copyright ownership of everything the project produces is clearly defined | **yes** | `LICENSE` (MIT, "Copyright (c) 2026 MPCoreDeveloper"), `<Copyright>` in `src/Directory.Build.props`, and `NOTICE` — one page stating who holds it, that no third-party source code is included, the three `Microsoft.Extensions.*` runtime dependencies and their licence, and what the name is not |
| Mandatory dependencies are permissively licensed | **yes** | MIT/`Microsoft.Extensions.*` only, and `SharpCorePDF.Domain` has no package reference at all |
| Committers are bound by a CLA, or are willing to embrace the Foundation's | **willing** | no CLA today; `CONTRIBUTING.md` states inbound = outbound and that a CLA would be announced before it applies |
| The project uses Source Link | **yes**, to verify before filing | `RepositoryUrl` is set, and the .NET 8+ SDK embeds SourceLink from it. Nothing has been packed for a feed yet, so this is checked in the nupkg at `docs/release-checklist.md` G3 |
| Embedded PDBs or symbol packages | **no** — a known gap | `dotnet pack` on 2026-10-17 produced **0** `.snupkg` and no embedded PDBs. `docs/release-checklist.md` G4 |
| Reproducible build settings | **partly** — `Deterministic` is on, reproducibility is not proven | `src/Directory.Build.props`. The honest answer is "deterministic compiler output; two machines have not been compared" |
| Code signs its artifacts | **no** | nothing is signed. This is one of the two things worth asking the Foundation for |
| The organization has 2FA enabled | **owner**, see below — there is no organization to point at | decided 2026-10-17 that this stays a personal User account, so the item resolves to the personal switch (Settings → Password and authentication). Only the owner can read that setting, and this document does not claim it on their behalf |
| A home page with status and purpose | **yes** | README (status, install, measured numbers) plus the repository description |
| A public communication channel | **not yet** | Issues is the only one; Discussions is off. Condition 3 in the definition of ready |
| A public location for documentation | **yes** | `README.md`, `docs/` (measurements, release gates, readiness), `examples/`, and the XML documentation file every package ships |

Two items are the owner's answer and cannot be written from here: whether 2FA is already on the account,
and whether artifacts will be **code-signed** through the Foundation. The third question this section used
to ask — whether the account becomes an **Organization** — was answered on 2026-10-17: it does not, and
the project is filed as a personal account.

### 6. Why apply

Draft, for the owner to edit rather than to paste blind:

> SharpCorePDF reads and writes every PDF byte itself. It has no PdfSharp underneath, no iText, no native
> library, and a `SharpCorePDF.Domain` package with no dependency at all — which is what lets it promise
> what a wrapper cannot: a merge whose allocations are measured, whose damage is reported instead of
> thrown, and whose output is checked against an independent reader.
>
> The reason to bring it to the Foundation is not reach. It is that a library which parses untrusted input
> for other people's applications should not depend on one person's availability. Membership gives it a
> neutral home, a documented succession path, and the two things a one-person project cannot buy: a
> trademark that is not a personal GitHub account, and code signing for the packages.
>
> It is a small project — one maintainer, and at the time of writing, no release on nuget.org yet. It is
> applying because it satisfies the eligibility criteria rather than because it has a community to
> demonstrate, and it would rather be told "Seed, re-evaluate in a quarter" than pretend otherwise.

### 7. Infrastructure requirements

- **Build servers:** GitHub-hosted runners only. `.github/workflows/ci.yml` builds on `ubuntu-latest` with
  two SDK bands (`11.0.x` and `10.0.x`) and runs both test legs; nothing self-hosted exists or is planned.
- **Hosting and certificates:** none. The library has no service, no website and no endpoint; the README is
  the project's page and GitHub serves it.
- **Code signing:** this is the request. Five packages would benefit from Authenticode signing — a
  certificate that an individual maintainer cannot justify buying for a free library — and the repository
  already publishes through nuget.org **trusted publishing** (OIDC, no stored API key), so there is no
  secret to hand over.
- **Analysis:** SonarCloud is wired up and gated on a repository secret. It is not required by anything
  here; it is mentioned so that nobody discovers it during onboarding.

### 8. Additional notes

- **What it is not.** The nearest neighbours are PdfSharp/PdfSharpCore (a general PDF object model),
  iText (a commercial suite) and QuestPDF (a layout engine). SharpCorePDF does one thing: merge whole
  documents or selected pages, encrypted or not, keeping outlines, page labels and annotations, with every
  byte read and written in-house. It is dependency-injection first because that is how the domain is
  wired, not as a differentiator in itself.
- **The name.** `SharpCorePDF` is one transposition away from `PdfSharpCore`, a popular package in the same
  problem space. That was noticed before the first release, and it is stated here rather than left for a
  reviewer to find. The decision recorded on 2026-10-17 is to keep it, and to claim `SharpCorePDF` itself
  first: that id is published as an entry-point package that depends on the four layers, so a consumer
  installs the product's name rather than working out which of four layer names the merge needs. `NOTICE`
  says in one paragraph that the similarity is in the spelling only and that no code from PdfSharp,
  PdfSharpCore, iText or QuestPDF is part of this repository.
- **Known gaps, stated up front:** no symbol package (a debugger gets nothing), no code signing, and
  reproducibility is asserted by a compiler switch rather than by comparing two machines. All three are
  gates in `docs/release-checklist.md`, not aspirations.
- **Bus factor of one.** The contribution ladder in `CONTRIBUTING.md` exists so that this number can move;
  today it has not moved yet.

## Before pressing submit

- [ ] The eight conditions in the definition of ready are all true, and the table above says so.
- [ ] `dotnet pack` still produces five packages with the version the readiness table claims: the four
      layers with both legs in each, and `SharpCorePDF` with a dependency on all four and no assembly of
      its own.
- [ ] The test count in the README is still the count `dotnet test` reports — a reviewer who checks one
      number and finds it wrong reads the rest differently.
- [ ] No link in the issue points at `_bmad-output/` or at anything else that is not in the public
      repository.
- [ ] The five owner decisions are answered in `docs/foundation-readiness.md`, with a date — two are
      (package id, account shape); three are not.

## Owner inputs this document cannot supply

| Input | Where it is used | Why it cannot be drafted |
| --- | --- | --- |
| Package id, final | §8, and the first nuget.org push | **Decided 2026-10-17**: `SharpCorePDF` as the entry point plus the four layer ids. Irreversible once a version is published, which is why it was decided before one was |
| Contribution vs Assignment model | the CLA | a legal choice about copyright ownership |
| Trademarks: Licensed vs Disclaimed | the CLA | Licensed is the default; Disclaimed gives up enforcement |
| Signatories (names + contact addresses) | the DocuSign envelope | top 1–5 contributors, with addresses only the owner has |
| Account becomes an Organization | §5, the 2FA item | **Decided 2026-10-17**: no. It stays a personal account, so the 2FA item is answered on the account rather than on an organization |


