# Builds a deterministic local robustness sample, pins it with a SHA-256 lock, and scores it.
#
# This is a *synthetic* sample: every file comes from assets already in this repository, so anyone can
# rebuild it byte for byte and prove the run measured the same bytes. It is not an external corpus and
# must not be presented as one. Nothing is downloaded, and neither the corpus nor the lock is committed.
#
#   pwsh -File benchmarks/SharpCorePDF.Competitive/scripts/sample-corpus.ps1
#
param(
    [string] $OutputFolder = "artifacts/robustness-sample",
    [int]    $Iterations   = 1
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$corpus = Join-Path $root $OutputFolder
$lockPath = "$corpus.lock.json"

New-Item -ItemType Directory -Force -Path $corpus | Out-Null
Get-ChildItem -LiteralPath $corpus -Filter *.pdf | Remove-Item -Force

# The labelled parity fixtures: small, hand-checked, one per page shape.
$labelled = Join-Path $root 'tests/SharpCorePDF.Tests/Assets/parity/labelled'
Get-ChildItem -LiteralPath $labelled -Filter *.pdf |
    Copy-Item -Destination $corpus

# Anything the competitive harness already generated in artifacts/ (captures, fixtures), if present.
$generated = Join-Path $root 'artifacts/competitive'
if (Test-Path $generated) {
    Get-ChildItem -LiteralPath $generated -Filter *.pdf -Recurse |
        Where-Object { $_.Name -like 'fixture-*' -or $_.Name -like '*-create.pdf' -or $_.Name -like '*-merge.pdf' } |
        ForEach-Object { Copy-Item $_.FullName -Destination (Join-Path $corpus $_.Name) -Force }
}

Write-Host ("Sample corpus: {0} files under {1}" -f (Get-ChildItem -LiteralPath $corpus -Filter *.pdf).Count, $corpus)

dotnet run -c Release --project (Join-Path $root 'benchmarks/SharpCorePDF.Competitive') -- `
    --skip-correctness --skip-performance `
    --output (Join-Path $root 'artifacts/competitive-sample') `
    --robustness $corpus --robustness-lock $lockPath --write-robustness-lock --robustness-limit 50

Write-Host ''
Write-Host 'Re-run against the lock to prove the same bytes (this is the line a release cite uses):'
Write-Host ("  dotnet run -c Release --project benchmarks/SharpCorePDF.Competitive -- --skip-correctness --skip-performance --robustness {0} --robustness-lock {1} --robustness-limit 50" -f $OutputFolder, $lockPath)
