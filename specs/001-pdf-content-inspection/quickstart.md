# Quickstart: PDF Content Inspection

A runnable path from a clean checkout to evidence that the read-only content inspection capability
behaves as the contracts say. The commands below were run against this repository while the design was
written, and the outputs are what they printed at the time.

## What you are proving

| Contract | Evidence |
| --- | --- |
| Ten-member port: seven reads (one streaming) and three copy routes | Focused test class `PdfContentReaderTests` |
| Payload budget states and the copy route's equivalence | `PdfContentReaderTests.Budget.cs` |
| Warning kinds, scoping and absence-of-exception rule | Each partial: images, annotations, form, metadata, outline, pages |
| Page details and per-page warnings | `PdfContentReaderTests.Pages.cs` |

## Prerequisites

- .NET SDK **11.0.x** and **10.0.x** installed side by side. The test project is an executable and the
  .NET 11 SDK alone cannot produce the `net10.0` app host (`MSB3030`).
- `global.json` pins the test runner (`Microsoft.Testing.Platform`), not the SDK; do not edit it to make
  a build pass.
- Windows or macOS/Linux. On a non-Windows host, substitute your own RID where `win-x64` appears.

## Build and test both legs

From the repository root:

```powershell
dotnet build .\SharpCorePDF.slnx -c Release
dotnet test  .\SharpCorePDF.slnx
```

The suite is run twice, once per target framework. A solution-level `dotnet test` needs no extra
arguments; a project-level one does, because the test project multi-targets:

```powershell
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net11.0
dotnet test .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -p:TargetFramework=net10.0 -r win-x64
```

The `-r` is load-bearing: without it, some SDK builds skip the app host for the `net10.0` leg.

## Run one class in a second

xUnit v3 runs the test assembly as an executable, which is the fastest loop while designing:

```powershell
.\tests\SharpCorePDF.Tests\bin\Release\net11.0\SharpCorePDF.Tests.exe -class SharpCorePDF.Tests.PdfContentReaderTests -noColor
```

Observed on the reference machine:

```text
=== TEST EXECUTION SUMMARY ===
   SharpCorePDF.Tests  Total: 50, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0,494s
```

Variants that are worth knowing:

```powershell
# One theory or fact by name (substring match, no namespace needed)
.\tests\SharpCorePDF.Tests\bin\Release\net11.0\SharpCorePDF.Tests.exe -method *Budget* -noColor

# The other leg: build it first, or the executable on disk is the previous one
dotnet build .\tests\SharpCorePDF.Tests\SharpCorePDF.Tests.csproj -c Release -p:TargetFramework=net10.0 -r win-x64
.\tests\SharpCorePDF.Tests\bin\Release\net10.0\win-x64\SharpCorePDF.Tests.exe -class SharpCorePDF.Tests.PdfContentReaderTests -noColor
```

The focused run on `net10.0` reports `Total: 50, Errors: 0, Failed: 0` in 0,712s. Both figures are the
same class, so a difference between the legs is a real difference and not a filtering artefact.

`-class` takes the full namespace-qualified name. A typo silently matches nothing and reports
`Total: 0` with exit code 0, so check the total, not just the exit code. Verified: a class filter of
`SharpCorePDF.Tests.NoSuchTests` reports `Total: 0` and exits `0`; `-method *Budget*` reports `Total: 4`.
A `Total: 0` on a class that does exist means the executable predates it — rebuild that leg.

## A five-minute smoke programme

The suite is the authority, but seeing the capability print its own results is how you check that the
shape is usable. Create a scratch console app outside the repository (so it does not inherit
`src/Directory.Build.props` and its analyzer gate), point it at the two project references, and run it
against a fixture.

```powershell
$scratch = Join-Path $env:TEMP 'pdf-smoke'
dotnet new console -o $scratch --force
```

```xml
<!-- $scratch\pdf-smoke.csproj -->
<ItemGroup>
  <ProjectReference Include="D:\repos\MPCoreDeveloper\SharpCorePDF\src\SharpCorePDF.Application\SharpCorePDF.Application.csproj" />
  <ProjectReference Include="D:\repos\MPCoreDeveloper\SharpCorePDF\src\SharpCorePDF.Infrastructure\SharpCorePDF.Infrastructure.csproj" />
</ItemGroup>
```

```csharp
// $scratch\Program.cs
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Domain.ValueObjects;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Sources;

var source = new PdfFileSource(args[0]);

IPdfContentReader Open(int maxInlineBytes) => new PdfContentReaderService(
    new PdfPageContentReader(new PdfEngineOptions { MaxInlineContentBytes = maxInlineBytes }));

var reader = Open(64 * 1024);
var images = await reader.ReadImagesAsync(source, PageRange.All);

Console.WriteLine(
    $"images: {images.Count} (buffered {images.Items.Count(i => i.IsBuffered)}, " +
    $"withheld {images.Items.Count(i => !i.IsBuffered && i.ByteLength > 0)}), warnings: {images.Warnings.Count}");

foreach (var image in images.Items)
{
    var mask = image.Mask is { } m ? m.Kind.ToString() : "none";
    Console.WriteLine(
        $"  {image.Id} {image.Width}x{image.Height} {image.Filter ?? "raw"} " +
        $"bytes={image.ByteLength} buffered={image.IsBuffered} mask={mask}");
}

await foreach (var page in reader.ReadPageDetailsAsync(source, PageRange.All))
{
    Console.WriteLine(
        $"page {page.PageNumber}: {page.Images.Count} images, {page.Annotations.Count} annotations, " +
        $"{page.FormFields.Count} fields, warnings {page.Warnings.Count}");
}

var outline = await reader.ReadOutlineAsync(source);
Console.WriteLine($"outline: {outline.Count} bookmarks, warnings: {outline.Warnings.Count}");

var xmp = await reader.ReadMetadataAsync(source);
Console.WriteLine(xmp.Metadata is { } metadata
    ? $"xmp: {metadata.Properties.Count} properties, {metadata.RawPacket.Length} bytes"
    : $"xmp: absent, warnings: {xmp.Warnings.Count}");

// The same document, read with a budget that withholds everything, then fetched through the copy route.
var tight = Open(0);
var squeezed = await tight.ReadImagesAsync(source, PageRange.All);
Console.WriteLine($"budget=0: buffered {squeezed.Items.Count(i => i.IsBuffered)}, warnings {squeezed.Warnings.Count}");

if (squeezed.Items.FirstOrDefault(i => !i.IsBuffered && i.ByteLength > 0) is { } hand)
{
    using var destination = new MemoryStream();
    await tight.CopyImageDataAsync(source, hand, destination);
    Console.WriteLine($"copied {destination.Length} bytes for {hand.Id}, ByteLength says {hand.ByteLength}");
}
```

```powershell
Set-Location $scratch
dotnet run -c Release -- D:\repos\MPCoreDeveloper\SharpCorePDF\tests\SharpCorePDF.Tests\Assets\SafeWebCore.pdf
```

Observed output:

```text
images: 1 (buffered 1, withheld 0), warnings: 0
  4 0 613x613 DCTDecode bytes=17270 buffered=True mask=none
page 1: 1 images, 0 annotations, 0 fields, warnings 0
page 2: 1 images, 0 annotations, 0 fields, warnings 0
page 3: 1 images, 0 annotations, 0 fields, warnings 0
page 4: 1 images, 0 annotations, 0 fields, warnings 0
page 5: 1 images, 0 annotations, 0 fields, warnings 0
page 6: 1 images, 0 annotations, 0 fields, warnings 0
outline: 0 bookmarks, warnings: 0
xmp: absent, warnings: 0
budget=0: buffered 0, withheld 1, warnings 0
copied 17270 bytes for 4 0, ByteLength says 17270
```

Three things in that output are the contract working: the JPEG is passed through untouched
(`DCTDecode`, 17270 bytes, buffered); shrinking the budget to zero withholds the payload **without a
warning**; and the copy route writes exactly the 17270 bytes the withheld model promised.

Now the damaged case:

```powershell
dotnet run -c Release -- D:\repos\MPCoreDeveloper\SharpCorePDF\tests\SharpCorePDF.Tests\Assets\EncryptedRc4Large.pdf
```

```text
images: 0 (buffered 0, withheld 0), warnings: 2
page 1: 0 images, 0 annotations, 0 fields, warnings 1
outline: 0 bookmarks, warnings: 1
xmp: absent, warnings: 1
budget=0: buffered 0, withheld 0, warnings 2
```

Without a password the security handler does not open: `EncryptionNotOpened` from the open step is
folded into every document-scoped result (images, outline, metadata), and the page — plus the image
scan, which walks the page content — adds `ContentStreamUnreadable` of its own. The collection results
stay empty, yet the page still appears, because the page tree is not encrypted even when its content
cannot be read. No exception, no `null` collection — the shape the contracts promise.

## When it does not build

| Symptom | Cause | Fix |
| --- | --- | --- |
| `MSB3030` while building the tests | Only the .NET 11 SDK is installed, so no `net10.0` app host can be produced | Install the 10.0.x SDK beside it |
| `dotnet test` on the test project demands a target framework | The project multi-targets | Add `-p:TargetFramework=net11.0`, or `-p:TargetFramework=net10.0 -r win-x64` |
| The `net10.0` leg runs no tests | RID-less build skipped the app host | Add `-r win-x64` (or your RID) |
| Analyzer errors on a throwaway project | It inherited `src/Directory.Build.props` | Keep scratch projects outside the repository |
| A scoped run reports `Total: 0` and exits `0` | The class name was not namespace-qualified, the method pattern matched nothing, or the executable is stale | Use `SharpCorePDF.Tests.PdfContentReaderTests`, check the pattern, and rebuild that leg |
| `PublicAPI.Unshipped.txt` mismatch | The surface changed without the snapshot | Update the snapshot in the same commit |

`src/Directory.Build.props` references `SonarAnalyzer.CSharp`, so every `dotnet build` runs the analyzer
rules the project enforces; warnings there are build failures, not suggestions.

## Fixtures

The content tests do not read the binary corpus. `PdfContentFixtures` writes each document it needs in
code — a two-page image document with a JPEG, a Flate image and a soft mask, a JPX image the library does
not decode, an inline image, a form, an outline, a malformed packet, an oversized payload — so an
assertion can name the exact byte, filter or destination it expects. Only the encrypted cases come from
the checked-in corpus (`TestAssets.Encrypted*`).

That corpus lives in `tests\SharpCorePDF.Tests\Assets` and is generated, not hand-edited:

```powershell
uv run --with pikepdf python .\docs\tools\make-fixtures.py .\tests\SharpCorePDF.Tests\Assets
```

The smoke programme above uses two of those files: `SafeWebCore.pdf` (a DCT image drawn on six pages) and
`EncryptedRc4Large.pdf` (security-handler revision 3, RC4, with a user password). Others worth knowing:
`Outlined.pdf` (two bookmarks, one annotation), `Modern.pdf` (object streams and cross-reference streams,
with neither images nor an outline), `EncryptedClearMetadata.pdf` (a clear-text metadata stream beside an
encrypted body), and one `Encrypted*.pdf` per security-handler revision, including a non-ASCII password.

## Definition of done

1. `dotnet test .\SharpCorePDF.slnx` is green on **both** legs.
2. The focused class is green: 50 tests, 0 failed, from `PdfContentReaderTests`.
3. The conformance checklists in the contracts are each covered by a test that fails when the behaviour
   is reverted:
   - [`contracts/content-reader.md`](contracts/content-reader.md) — 10 items
   - [`contracts/page-content-reader.md`](contracts/page-content-reader.md) — 8 items
   - [`contracts/payload-budget.md`](contracts/payload-budget.md) — 9 items
   - [`contracts/warnings.md`](contracts/warnings.md) — 13 items
4. `PublicAPI.Unshipped.txt` carries the new surface and nothing has been promoted to `Shipped`.
5. The smoke programme above prints the two outputs shown, on a machine with only the documented
   prerequisites.
