# Benchmarks

This folder is **not** part of the product solution.

| Project | Solution | Purpose |
| --- | --- | --- |
| `SharpCorePDF.Competitive` | `SharpCorePDF.Competitive.slnx` | Head-to-head correctness and performance against pinned PdfPig and PDFsharp packages |

```powershell
dotnet run -c Release --project .\benchmarks\SharpCorePDF.Competitive -- --help
```

Reports land under `artifacts/competitive/` by default (gitignored). The durable methodology and the
latest interpreted baseline live in [`docs/competitive-scoreboard.md`](../docs/competitive-scoreboard.md).
Third-party licence notes for the comparison subjects are in
[`SharpCorePDF.Competitive/THIRD-PARTY.md`](SharpCorePDF.Competitive/THIRD-PARTY.md).

Performance scenarios include cold `text-200-all` and warm `text-200-all-warm` (session open).

Switches worth knowing: `--robustness <folder>` runs the opt-in local-corpus leg;
`--robustness-lock <manifest.json>` pins it by SHA-256 (`--write-robustness-lock` writes the manifest once,
locally) and a changed byte is reported as `LockMismatch` without any engine opening the file. The
`text-200-all-<strategy>-alt` rows are named alternate word rules per adapter, never a replacement for
the default extraction row. Correctness reports two box columns: label-convention agreement and a
convention-free IoU, because PdfPig's ink boxes can disagree with the label convention while still
overlapping the characters.
