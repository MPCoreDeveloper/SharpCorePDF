# 10. Dependency Injection

The `SharpCorePDF.DependencyInjection` package registers the merger, the editor, the inspector and the
engines behind their ports. It is a thin layer: one call, no auto-discovery, no background services and
no logging of its own. The `SharpCorePDF` meta-package already depends on it, so the types are available
after the installation described in Chapter 3.

## One call

```csharp
using SharpCorePDF.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharpCorePDF();

var app = builder.Build();
```

That registers the services listed below as singletons. They are stateless; a merge or an edit creates
its own context, so concurrent requests can use the same `IPdfMerger` or `IPdfDocumentEditor` instance
without coordination.

| Service | Lifetime | Notes |
| --- | --- | --- |
| `PdfEngineOptions` | Singleton | `PdfEngineOptions.Default` unless replaced |
| `PdfDocumentReader` | Singleton | Concrete reader; registered for advanced use |
| `IPdfDocumentReader` | Singleton | Resolves to the same `PdfDocumentReader` |
| `PdfMergeEngine` | Singleton | Concrete merge engine |
| `IPdfMergeEngine` | Singleton | Resolves to the same `PdfMergeEngine` |
| `PdfEditEngine` | Singleton | Concrete edit engine |
| `IPdfEditEngine` | Singleton | Resolves to the same `PdfEditEngine` |
| `IPdfMerger` | Singleton | `PdfMergerService`, the merge use case to depend on |
| `IPdfDocumentEditor` | Singleton | `PdfDocumentEditorService`, the edit use case to depend on |
| `IPdfInspector` | Singleton | `PdfInspectorService` |
| `PdfPageContentReader` | Singleton | Concrete content reader |
| `IPdfPageContentReader` | Singleton | Resolves to the same `PdfPageContentReader` |
| `IPdfContentReader` | Singleton | `PdfContentReaderService` |

Application code should depend on `IPdfMerger`, `IPdfDocumentEditor` and `IPdfInspector`, not on the
concrete services. The concrete registrations exist so a test can replace one layer without replacing
the whole stack, and so the engines can be exercised directly.

Authoring is not registered because it is not a service: `PdfDocumentBuilder` is an ordinary class with
a static factory, and `Build()` returns an `IPdfContentSource` the merger already understands. A
typeface is immutable, so a typeface read at start-up can be registered as a singleton and shared by
every builder:

```csharp
builder.Services.AddSingleton(PdfTypeface.FromTrueType(File.ReadAllBytes("fonts/DejaVuSans.ttf")));
```

```csharp
app.MapPost("/merge", async (IPdfMerger merger, IFormFileCollection files) =>
{
    var plan = MergePlan.Create();

    foreach (var file in files)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        plan = plan.AddBytes(buffer.ToArray(), file.FileName);
    }

    var bytes = await merger.MergeToBytesAsync(plan);
    return Results.File(bytes, "application/pdf", "merged.pdf");
});
```

Each `AddBytes` call creates a source with a fresh identity, so uploading the same file twice contributes
its pages twice. The bytes are buffered before the merge because the merger reads its sources later; an
`IFormFile` stream is not guaranteed to stay open until then. Chapter 4 covers the other factories.

## Without the container

`AddSharpCorePDF()` only saves typing. The reader, the engine and the two services have public
constructors, so the same graph can be built by hand:

```csharp
using SharpCorePDF.Application.Services;
using SharpCorePDF.Domain.Ports;
using SharpCorePDF.Infrastructure;

var options = PdfEngineOptions.Default;
var reader = new PdfDocumentReader(options);

IPdfMerger merger = new PdfMergerService(reader, new PdfMergeEngine(options));
IPdfInspector inspector = new PdfInspectorService(reader);
```

The two routes are equivalent: the container builds exactly this graph and registers it as singletons.
The `no-container` example in the repository runs the same plan through both and compares the bytes.

## Three ways to configure the engine

The extension has three overloads; the configuration overload takes an optional section name, so these
four shapes are possible:

```csharp
services.AddSharpCorePDF();                                      // defaults
services.AddSharpCorePDF(PdfEngineOptions.Default with { ... }); // explicit options
services.AddSharpCorePDF(configuration);                         // the "SharpCorePDF" section
services.AddSharpCorePDF(configuration, "My:Pdf:Section");       // a section of your choosing
```

- **No argument** registers `PdfEngineOptions.Default` through `TryAddSingleton`, so an options
  instance you registered earlier is left in place.
- **An options instance** is registered with `Replace`, so it wins over an earlier registration.
- **A configuration** binds the named section and registers the bound value. The default section name
  is the `DefaultConfigurationSection` constant, `"SharpCorePDF"`.

The configuration overload does not fail when the section is missing: binding an absent section
produces the default values. That is convenient in development and dangerous in production, where a
typo in the section name silently disables a limit you thought you had set. Validate explicitly:

```csharp
var engineOptions = builder.Configuration
    .GetSection(SharpCorePDFServiceCollectionExtensions.DefaultConfigurationSection)
    .Get<PdfEngineOptions>() ?? PdfEngineOptions.Default;

var problems = engineOptions.Validate();

if (problems.Count > 0)
{
    throw new InvalidOperationException(
        $"SharpCorePDF configuration is invalid: {string.Join(" ", problems)}");
}

builder.Services.AddSharpCorePDF(engineOptions);
```

`Validate()` checks the numeric limits and returns every problem it finds instead of throwing. It does
not read any document, so it cannot catch a page selection that reaches past the last page; that
surfaces when the plan runs (Chapter 9).

## Engine options

`PdfEngineOptions` is an immutable record. Configure it with `with` expressions, and remember that the
defaults are deliberately permissive:

| Option | Default | Effect |
| --- | --- | --- |
| `MaxSourceBytes` | 512 MiB | Largest source read into memory; larger sources are rejected |
| `MaxNestingDepth` | 64 | Maximum dictionary/array nesting and page-tree depth |
| `MaxPagesPerSource` | 100 000 | Declared page limit; see the note below |
| `ReconstructCrossReferenceOnFailure` | `true` | Rebuild a damaged or missing cross-reference table by scanning |
| `VerifyOutput` | `false` | Re-read the written document and check its page count |
| `WriteBinaryMarker` | `true` | Write the binary comment after the `%PDF` header |

The source limit is enforced by the reader, and exceeding it throws `PdfSourceFormatException` with a
detail that names the limit. It is not a streaming cap: the whole source is held in memory while it is
processed, so the practical ceiling is your process's memory budget, not the number on the option.

`MaxNestingDepth` has two effects. It bounds how deep an object graph may be parsed, and it bounds the
page-tree walk; a page tree that is cut off contributes a `PageTreeTooDeep` warning rather than
failing the merge.

`ReconstructCrossReferenceOnFailure` is the difference between "repair what you can" and "reject what
is not well formed". Leaving it on produces a `CrossReferenceRebuilt` warning when repair happened.
Turning it off is appropriate when a damaged file should be treated as an error, for example in a
regulatory archive.

`VerifyOutput` is a development and test switch. It captures the complete output in memory, reads it
back and compares the page count with what was written, then adds an `OutputVerified` warning on
success. That doubles the work and holds the whole result in memory, which defeats the streaming
design for large documents. Use it in integration tests, not in production.

`WriteBinaryMarker` should stay on. Some transfer tools inspect the first bytes to decide whether a
file is binary; the marker costs a few bytes and prevents a class of corruption that is otherwise very
hard to diagnose.

> `MaxPagesPerSource` is part of the options record but this version does not enforce it. Do not rely on
> it as a security boundary. For untrusted input, set `MaxSourceBytes`, inspect the document with
> `IPdfInspector` and reject it if `PageCount` exceeds your own limit, before building a plan.

## Configuration file

With the configuration overload, the section names map directly to the property names:

```json
{
  "SharpCorePDF": {
    "MaxSourceBytes": 268435456,
    "MaxNestingDepth": 64,
    "MaxPagesPerSource": 50000,
    "ReconstructCrossReferenceOnFailure": true,
    "VerifyOutput": false,
    "WriteBinaryMarker": true
  }
}
```

Environment variables and command-line arguments follow the normal configuration rules, for example
`SharpCorePDF__MaxSourceBytes`. Because the section is bound to an immutable record, a property that is
missing keeps its default; a property with a value of the wrong type causes a binding exception at
start-up, which is one of the few failures you want to be loud.

## Logging and diagnostics

The library has no `ILogger` dependency and writes nothing to the console. That is deliberate: a class
library that logs through a static logger cannot be tested cleanly and cannot respect a host's
configuration. Everything you need to log is in the return values:

- `MergeResult` carries `PageCount`, `SourceCount`, `OutputBytes`, `OutputVersion`, `Duration`,
  `PrimarySourceId`, per-source summaries and `Warnings`.
- `PdfDocumentInfo` carries the same kind of facts for inspection, plus `IsReadable`, `IsEncrypted`
  and `RequiresPassword`.
- Exceptions carry `SourceName`, and `MergePlanValidationException` carries `Errors`.

Log them at the boundary of your application, where you know the request and the user. Chapter 9 shows
the exception-handling shape, and Chapter 8 explains the warning kinds.

## The producer string

When `MergeOutputOptions.Producer` is not set, the writer stamps the document with the product name and
the assembly version of the SharpCorePDF domain assembly, for example `SharpCorePDF 1.2.0`. Override it
with your own product name if the PDF is produced by your application rather than offered as a library
feature:

```csharp
var plan = MergePlan.Create()
    .AddFile("input.pdf")
    .WithOutput(options => options with { Producer = "Contoso Report Server 4.2" });
```

There is no separate "application name" setting in `PdfEngineOptions`; the producer belongs to the
output document, not to the engine, and therefore lives on `MergeOutputOptions` (Chapter 6).

## Testing with the container

For integration tests, register the real stack and turn verification on:

```csharp
var services = new ServiceCollection();
services.AddSharpCorePDF(new PdfEngineOptions { VerifyOutput = true });
services.AddSingleton<MyReportService>();

await using var provider = services.BuildServiceProvider();
var report = provider.GetRequiredService<MyReportService>();

var bytes = await report.BuildAsync();
Assert.True(bytes.Length > 0);
```

An `InvalidOperationException` from the merge in this configuration means the engine wrote a document
it could not read back — a defect worth reporting, not a problem with the input. Ordinary input
problems still appear as `PdfException` subclasses.

To test the layer above, replace `IPdfMerger` or `IPdfInspector` with a fake after the registration.
Because the concrete services are registered separately, replacing only the interface leaves the
engine available for tests that need it.
