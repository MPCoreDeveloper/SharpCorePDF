# Tooling this repository assumes

## Mutation testing is part of the harness, in every repository we touch

Standing instruction from the maintainer: **any repository we work on carries Stryker.NET**, whether or not
the request mentions tests. It is a harness, like the test project — not a one-off experiment. A task that
adds or changes production code ends with a scoped Stryker run over the files it touched, reported with the
score, and the survivors read rather than only counted.

What "installed" means, in this order:

1. `.config/dotnet-tools.json`, committed, pinning `dotnet-stryker` — a local tool, so `dotnet tool restore`
   is the whole setup and no clone has to install anything globally.
2. `stryker-config.json` at the root: `test-projects`, `test-runner`, `target-framework`, `configuration`,
   `mutation-level`, `coverage-analysis`, `thresholds`, `reporters`, `break-on-initial-test-failure` and
   `mutate`. The workflow and a local run read this one file.
3. A workflow that restores the same manifest, so the pin is stated once.
4. A README section (`Building and testing` → mutation testing) saying how to run it and how to read the
   report, and a longer record under `docs/` when there is something worth recording.

Setting it up on a repository that has none of this:

```powershell
dotnet new tool-manifest                                  # writes .config/dotnet-tools.json
dotnet tool install dotnet-stryker --version 5.0.0        # the version stryker-config.json is written for
dotnet tool restore
dotnet stryker --config-file stryker-config.json --project <Layer>.csproj --mutate "**/<File>.cs"
```

`StrykerOutput/` (the default output directory) and `artifacts/` are ignored; reports are never committed.

Known wrinkle on a Windows workstation: Stryker's analysis can pick Visual Studio's MSBuild and then fail to
compile mutants with `error CS0518: Predefined type 'System.Attribute' is not defined or imported`. Name the
SDK's copy before running, which `docs/mutation-testing.md` records:

```powershell
$sdk = (& dotnet --version).Trim()
$env:MSBUILD_EXE_PATH = "$env:ProgramFiles\dotnet\sdk\$sdk\MSBuild.dll"
```

## Why it is asked for

Coverage says a line ran. Stryker says whether the suite would have noticed that line changing: it applies
one mutation at a time, rebuilds, and runs the covering tests. A survivor is a line no assertion pins down —
either a missing test or a redundant statement. This repository treats a survivor as a finding to explain,
not as a number to raise.
