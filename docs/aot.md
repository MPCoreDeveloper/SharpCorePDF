# Native AOT and trimming

What the packages claim, how the claim is proven, and the one member that is deliberately outside it.

## The claim

Every package under `src/` sets `IsTrimmable` and `IsAotCompatible` (`src/Directory.Build.props`). The
first puts `IsTrimmable` metadata into the assembly, so a consumer's publish treats these packages as safe
to trim; together they turn on the trim, AOT and single-file analyzers, and the build is clean with them.

The reason the claim holds is structural rather than annotative: the library reads and writes every PDF
byte itself. There is no reflection over its own types, no dynamic code generation, no serialization of
its own records and no P/Invoke — the only matches for `System.Reflection` in `src/` are generated files
under `obj/`. The authoring writer, the lexer, the object-graph walker, the layout analysis and the
signature writer are ordinary code over arrays, spans and structs.

## The one exception

`AddSharpCorePDF(IServiceCollection, IConfiguration, string)` binds a configuration section to
`PdfEngineOptions`. That binding is the framework binder's, and it reads property names by reflection and
creates its converters at run time, which trimming can remove and an AOT publish cannot generate. The
overload is therefore annotated:

```csharp
[RequiresUnreferencedCode(BindingRequirement)]
[RequiresDynamicCode(BindingRequirement)]
public static IServiceCollection AddSharpCorePDF(
    this IServiceCollection services,
    IConfiguration configuration,
    string sectionName = DefaultConfigurationSection)
```

The annotation is how a consumer's publish finds out at build time rather than at run time: calling it from
a trimmed or AOT application is a warning, and the message names the way out. That way out is the overload
that takes the options object — `AddSharpCorePDF(PdfEngineOptions)` — which is what the smoke app uses.

## How it is proven

[`examples/SharpCorePDF.AotSmoke`](../examples/SharpCorePDF.AotSmoke) is the smallest consumer that touches
everything a publish has to keep: the authoring builder writes a page, `AddSharpCorePDF` registers the
engines, `IPdfMerger` merges two documents, `IPdfContentReader` reads the text back, and the byte-array
source carries the bytes between them. It runs under the JIT like any example, and it is published with
`PublishAot` for one RID at a time.

Its `expect-aot` switch is what makes the run a proof: a Native AOT process reports
`RuntimeFeature.IsDynamicCodeSupported == false`, so the app fails when the published binary still supports
dynamic code. A publish that silently fell back to the JIT cannot pass.

```powershell
dotnet run     --project .\examples\SharpCorePDF.AotSmoke
dotnet publish .\examples\SharpCorePDF.AotSmoke -c Release -r win-x64 -p:PublishAot=true
.\examples\SharpCorePDF.AotSmoke\bin\Release\net11.0\win-x64\publish\SharpCorePDF.AotSmoke.exe --expect-aot
```

Recorded 2026-11-06, .NET 11 RC 3 SDK, `Release`:

| RID | Publish | Run | Binary |
| --- | --- | --- | --- |
| `win-x64` | 0 warnings, 0 errors | `aot=True pages=2 chars=26 bytes=1348`, `AOT smoke: ok`, exit 0 | 3.3 MB |
| `linux-x64` | the `native-aot` job in `ci.yml` | the same line, with `expect-aot` | built on the runner |
| `linux-arm64` | the `arm64` job in `ci.yml` | the same line, with `expect-aot` | built on the runner |

A Native AOT binary is compiled for the machine it runs on, so each RID is proved on its own runner: the
local proof is `win-x64`, and the workflow covers `linux-x64` and `linux-arm64`. The arm64 job also runs
the test suite, which is the only way the AdvSimd kernel of `SimdByteScan` is ever executed — an x64
machine cannot run it, and a compile is not a run.

The version is worth stating beside the claim. The net11 assemblies are compiled with `runtime-async=on`
(`src/Directory.Build.props`), and .NET 11 compiles its own libraries the same way while Native AOT is a
first-class deployment mode there — the win-x64 run above is the proof that the combination behaves for
this library.

**GA had not shipped on the date of the record.** The .NET release index lists `11.0.0-rc.3` (2026-10-25) as
the newest .NET 11 release, and the proof above was made with the SDK pinned to it. The `native-aot` and
`arm64` jobs install `11.0.x`, so the first run after GA uses it without a change here; the evidence should
be re-read then, and no release note should claim GA before that run.

## What AOT changes, and what it does not

* **Startup and memory.** An AOT publish pays no JIT, which is the reason to choose the mode.
* **Steady state.** A long-running JIT process can match or beat AOT, because tiered PGO re-optimises the
  hot path with profile data the AOT compiler does not have. The numbers in
  [`competitive-scoreboard.md`](competitive-scoreboard.md) are JIT numbers.
* **Size.** A self-contained native binary (3.3 MB for the smoke app) instead of IL in a package.
* **Globalisation.** The smoke app is invariant (`InvariantGlobalization`), and a read uses the library's
  own encoding, CMap and glyph tables rather than ICU.
* **The public surface.** Nothing changes: a consumer on the JIT and a consumer publishing AOT call the same
  API, and only the configuration-binding overload needs the annotation above.
