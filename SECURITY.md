# Security policy

SharpCorePDF reads PDF bytes that come from wherever the caller got them. A malformed document is normal
input here, not an attack, so the parser is written to survive one — but "survives" is a claim, and this
is where a hole in that claim gets reported.

## Supported versions

| Version | Supported |
| --- | --- |
| `1.0.0-preview.*` | yes — pre-release; a fix lands on `main` and ships in the next preview |
| anything older | nothing older exists: no version of this package has been published to a feed |

There is one codebase and one supported line. A fix is not back-ported, because there is nothing behind
it to port to.

## What is in scope

Every package in this repository — `SharpCorePDF.Domain`, `SharpCorePDF.Application`,
`SharpCorePDF.Infrastructure`, `SharpCorePDF.DependencyInjection` — and in particular:

- **Any input that turns into more than an exception.** Code execution, a crash the caller cannot catch,
  memory or time that grows with the document rather than with its size, or a byte written outside the
  stream the caller handed over.
- **The standard security handler.** RC4 and AES, revisions 2 to 6, the empty-password case, a password
  that is not ASCII, and the `/EncryptMetadata` flag that decides what a merged result keeps.
- **The reader's repair path.** A truncated file, a cross-reference table whose offsets are wrong, an
  object stream, a stream whose `/Length` lies. Damage is reported through the warning list rather than by
  throwing, so a document that gets past that list silently is a bug worth a report.
- **The merge path.** Object and page copying, outlines, page labels, annotations, and the incremental
  decisions that decide which object wins when two sources disagree about a number.

Out of scope, not because they are unimportant but because they are not in a consumer's process: the
example application under `examples/`, the measurement harness under `tools/`, the Python validator under
`docs/tools/`, and a document whose password the caller does not have — refusing that is the intended
behaviour, not a defect.

Also out of scope: anything that requires the library to reach the network. It never does. It opens no
socket, writes no log, and stores no secret; a password is used for one operation and not kept.

## Reporting a vulnerability

**Do not open a public issue, and do not attach the document to one.** A public issue describing a crash
in a PDF parser is a working exploit by the time it is indexed.

Two private channels, in this order:

1. **GitHub private vulnerability reporting** —
   <https://github.com/MPCoreDeveloper/SharpCorePDF/security/advisories/new>.
   This is preferred: the report, the fix and the advisory stay in one place, the discussion is private
   until it is published, and a CVE can be requested from the same screen if the issue earns one. If that
   page answers 404, private reporting is not switched on for this repository yet — use the second
   channel.
2. **The contact form at <https://www.posseth.com/contact>**, with the subject line
   `SharpCorePDF security`. No security address is published anywhere, on purpose: a form is a channel
   that works before an inbox exists, and it keeps the reporting path from depending on a mail server that
   a two-day-old project has not set up.

A report is most useful with:

- the package and version (`1.0.0-preview.3` and later — the exact `<Version>` matters, the surface is
  still moving), and the target framework (`net10.0` or `net11.0`);
- the call that produced the result — the type and method, not a code review;
- whether the document was encrypted, and which revision if you know it;
- the document itself, or the smallest prefix of it that still reproduces the result. It can be generated
  by `docs/tools/make-fixtures.py`, which means you can often describe the shape instead of attaching a
  file;
- what happened, and what you expected instead. A merge that reports no warning and writes a document
  another reader rejects is as valuable as a crash: `docs/tools/validate-pdf.py` exists because that is
  how the one real bug in this library was found.

## What to expect

| When | What happens |
| --- | --- |
| within 7 days | an acknowledgement, from the maintainer, with an initial read on severity |
| within 30 days | an assessment: whether it is in scope, what the fix looks like, and a disclosure date agreed with you |
| at the fix | a preview release that names the issue in its changelog, and an advisory if the severity warrants one |

Coordinated disclosure: a date is agreed before anything is published, and credit is given by name or
handle unless you ask to stay anonymous. There is no bug bounty — this is one maintainer and no budget —
and a report is not worth less for that.

## Safe harbour

Good-faith research on this library is welcome, and a report made through either channel above will not be
pursued or reported to a platform as abuse. That covers what you need to confirm a finding against your
own installation: crafted documents, fuzzing the parser, memory and time limits, and reading or writing
any file you own. It does not cover using a finding against someone else's deployment, or publishing
details of an unfixed issue.

