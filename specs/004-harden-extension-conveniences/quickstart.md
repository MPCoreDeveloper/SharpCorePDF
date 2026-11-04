# Quickstart: Extension Hardening

Five commands. The first four are the local gates; the fifth is the reading that says whether the suite would
have noticed the change being undone.

## 1. Build, both legs, zero warnings

```powershell
dotnet build .\SharpCorePDF.slnx -c Release
```

Expect `0 Warning(s)` and `0 Error(s)`. The two files this feature touches are in `SharpCorePDF.Application`,
which is built for `net10.0` and `net11.0` in the same pass.

## 2. The tests, on their own

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0 --filter-class "*PdfContentReaderExtensionsTests" --filter-class "*PdfMergerExtensionsTests"
```

Expect 22 passes. The two that carry the first finding are `Materialises_an_image_as_a_view_of_the_buffer` and
`Materialises_an_embedded_file_as_a_view_of_the_buffer`. Each one:

1. hands the extension a `MemoryStream`-writing `IPdfContentReader` that records the stream it was given;
2. calls `ReadImageDataAsync` / `ReadEmbeddedFileDataAsync`;
3. asserts `MemoryMarshal.TryGetArray(result, out var segment)` gives back **that stream's** buffer, `Count`
   equal to the payload, `Offset` zero — and that the bytes are the payload.

## 3. See the test fail on revert

In `src/SharpCorePDF.Application/Extensions/PdfContentReaderExtensions.cs`, put the old line back in either
member:

```csharp
return buffer.ToArray();
```

Run command 2 again. Both tests fail with `Assert.Same()` on the array — `ToArray()` allocates a new one — and
that is the whole point of the assertion: no test can distinguish a view from a copy by content, so the test
distinguishes them by identity. Restore the line before continuing.

## 4. The whole suite, both legs

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64
```

Nothing is expected to move: no member was added, removed or re-shaped, so no test changes except the files this
feature adds and the merger test it extends. `PublicAPI.Shipped.txt` must be unchanged — `RS0016`/`RS0017` are
build errors, so a forgotten snapshot entry would already have failed command 1.

## 5. Mutation testing, scoped to the two files

```powershell
# On a workstation with Visual Studio installed, name the SDK's MSBuild first (docs/mutation-testing.md):
$sdk = (& dotnet --version).Trim()
$env:MSBUILD_EXE_PATH = "$env:ProgramFiles\dotnet\sdk\$sdk\MSBuild.dll"

dotnet tool restore
dotnet stryker --config-file stryker-config.json --project SharpCorePDF.Application.csproj `
  --mutate "**/PdfContentReaderExtensions.cs" --mutate "**/PdfMergerExtensions.cs"
python .\docs\tools\mutation-summary.py .\StrykerOutput\reports\mutation-report.json --detail
```

What to look for — and what the first run of this change found: the two `GetBuffer().AsMemory(0, (int)Length)`
lines carry **no mutants** at all. Stryker's `Standard` level does not mutate an integer literal or a method
call, so the reading cannot say anything about view-versus-copy; step 3 above is that evidence instead.

What the reading does say has been worth reading three times. The first pass found `ReadPageDetailAsync` and the
null guards of the two materialising reads `NoCoverage`; the second closed the members this change touched; the
third closed the gaps the second left — the two `Save*` members, `OpenDestination` and the merger overloads — and
took the score over the same 57 scored mutants from 28.07 % to 68.42 %, with `NoCoverage` at zero in both files.
Every survivor that remains is explained in `docs/mutation-testing.md` under "The fifth record"; the copy that
stays in `MergeToBytesAsync` is a decision (research D2), and it carries no mutant either.

## What to read afterwards

- `SECURITY.md` → the trust-boundary section: destination paths are caller input, no root is enforced.
- `docs/manual/src/09-error-handling.md` → "Writing files safely": the same boundary, in the chapter a caller
  reaches when a write fails.
- The XML documentation of `MergeToFileAsync`, `SaveImageDataAsync` and `SaveEmbeddedFileAsync`, and of
  `MergeToBytesAsync` and both materialising reads — five remarks, each stating one thing the member assumes.
