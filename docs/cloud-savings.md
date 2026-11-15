# What SharpCorePDF saves you

Three costs a PDF library puts on a service: the licence, the native code you cannot see, and the CPU and
memory the library spends on every document. This page states what this repository measured for the third,
and turns it into a cost model you can replace with your own tariff. **Nothing here is a quote or a
benchmark against a named competitor** — the repository measures itself.

## 1. The licence

MIT. No seat, no runtime, no redistribution fee. The five packages are on nuget.org at `1.8.0`. If you are
replacing a commercial library, put your own seat price into the model below; this repository does not name
one.

## 2. The native code

No native binary, no P/Invoke and no dynamic code: `IsTrimmable`/`IsAotCompatible` are claims the build
checks, and `examples/SharpCorePDF.AotSmoke` publishes with `PublishAot` and runs in CI. A dependency you
cannot read is also a dependency you cannot patch; here the reader, the writer and the merge engine are in
the package.

## 3. The CPU and memory (measured)

The 100 MB measurement in [`measurements.md`](measurements.md) §10 — one document, server GC, one process,
median of three rounds, the file re-read from disk in every round:

| Row | Allocated | × input | Wall |
| --- | --- | --- | --- |
| open + inspect | 100.0 MB | **1.00** | 235.5 ms |
| merge 10 of its pages, before ADR-004 | 204.2 MB | **2.04** | 450.7 ms |
| merge 10 of its pages, after ADR-004 | **104.2 MB** | **1.04** | **383.2 ms** |
| edit one page | 104.2 MB | 1.04 | 378.4 ms |
| text of page 1 | 103.7 MB | 1.04 | 224.2 ms |

A merge of a 100 MB document therefore allocates **104.2 MB instead of 204.2 MB** and finishes **67.5 ms
sooner** on the same machine — **half the allocation** and **15% less wall time**, because each source is
read once instead of twice ([ADR-004](adr/0004-buffer-model.md)). The working set a request needs is the
document plus one output buffer, not two copies.

The Native AOT smoke app, for the other end of the scale: a published process authors a page, merges two
documents, reads the text back and asserts it runs without dynamic code in **17.4 ms** median, from a
**3.36 MiB** executable and a **21.61 MiB** publish folder.

## 4. The model (replace the tariff)

The tariff below is **illustrative, not a quote**: **$0.04 per vCPU-hour** and **$0.005 per GB-hour**, a
small general-purpose instance. Replace them with your provider's numbers.

For `N` merges a month of an `S` GB document:

- **Allocation avoided** — `N × S × 1.00 GB`, the difference between 2.04× and 1.04×.
- **GC CPU avoided** — if a core reclaims about **1 GB of allocation per second**, `N × S` CPU-seconds.
- **Wall time avoided** — at the measured **0.0675 s** per 100 MB merge, `N × 0.0675` seconds (this machine).
- **Working set** — `S × 1.04` GB per concurrent merge instead of `S × 2.04` GB; size the instance for your
  peak concurrency.

### Worked example: 1,000,000 merges a month of a 100 MB document

| | Before | After | Saved |
| --- | --- | --- | --- |
| Allocation | 204.2 TB | 104.2 TB | **100 TB** |
| GC CPU, at 1 GB/s | 56.7 CPU-hours | 28.9 CPU-hours | **27.8 CPU-hours → $1.11** |
| Wall time, measured | 125.2 CPU-hours | 106.4 CPU-hours | **18.8 CPU-hours → $0.75** |
| Working set, 10 concurrent merges | 2.04 GB | 1.04 GB | **1.0 GB → $3.65 a month per instance** |

The compute and memory lines are dollars at this volume. The line that is not is the licence: a commercial
seat at an illustrative **$1,500 per developer per year** is **$30,000 a year for 20 developers**, against
**$0** here. At 10,000,000 merges a month the compute and memory lines are about ten times larger — still
small next to the licence unless your volume is very large.

That is the honest shape of it: the allocation and the wall time are measured and real, the working set
halves, and the licence is the saving with an order of magnitude more weight. The compute saving is not a
slogan — it is 100 TB of allocation a month your GC does not reclaim at this volume, and it grows with the
volume.

## What this page does not claim

- **No comparison to a named library.** The repository measures itself, not a competitor; a fair comparison
  would run both against the same corpus on the same machine, and that run is not in this repository.
- **The tariff and the seat price are illustrative.** The formulas are the contract; the numbers are yours.
- **The GC throughput is an assumption.** 1 GB/s is a round figure; measure yours with `dotnet-counters`.
- **The wall time is one machine's.** [`measurements.md`](measurements.md) records the hardware and the
  rounds; yours will differ.

## Reproduce it

```powershell
# the 100 MB allocation and wall-time table above
dotnet run --project .\tools\SharpCorePDF.Measure -c Release -- --large

# the AOT figures: one process, one merge, no dynamic code
dotnet publish .\examples\SharpCorePDF.AotSmoke -c Release -r win-x64 -p:PublishAot=true
.\examples\SharpCorePDF.AotSmoke\bin\Release\net11.0\win-x64\publish\SharpCorePDF.AotSmoke.exe --expect-aot
```
