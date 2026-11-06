# Third-party comparison subjects

This project exists only to run reproducible comparisons. It is not packed, is not referenced by any
SharpCorePDF package, and is intentionally absent from `SharpCorePDF.slnx` and the unit-test project.

Pinned comparison subjects:

| Package | Version | Upstream licence | Use here |
| --- | --- | --- | --- |
| PdfPig | 0.1.16 | Apache-2.0, with upstream attribution in its licence file | Independent adapter invoked on shared fixtures |
| PDFsharp | 6.5.0 | AGPL-3.0-or-later or a commercial licence from empira | Independent adapter invoked on shared fixtures |

The adapters contain original glue code written against public APIs. No PdfPig or PDFsharp implementation
code is copied into SharpCorePDF. The packages are restored from nuget.org and remain subject to their own
licences; this file is not legal advice and does not grant any right those licences do not grant.

Before redistributing this harness or its restored binaries, review the obligations that apply to that
distribution, particularly PDFsharp's AGPL/commercial dual licence. An organisation that does not want to
redistribute a harness linked to PDFsharp can run that adapter in a separate repository and publish only its
methodology and measurements. SharpCorePDF's MIT licence does not change a comparison subject's licence.

Upstream licence references:

- <https://github.com/UglyToad/PdfPig/blob/master/LICENSE>
- <https://github.com/empira/PDFsharp/blob/master/LICENSE>
- <https://docs.pdfsharp.net/General/History/README-v6.5.html>
