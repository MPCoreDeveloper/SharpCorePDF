#requires -Version 7
<#
  Proves the packages are reproducible: packs the solution twice with the CI build property and compares
  every entry of every package, then checks that each package has a symbol package beside it.

  The comparison is entry-by-entry rather than a hash of the .nupkg file: the container records the time
  each entry was added, so two packs with identical contents can still differ as files. What a consumer
  installs is the entries, and those are what this compares.

  Run it from anywhere; it finds the repository root from its own path.
#>
param(
    [string] $Root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-EntryHashes {
    param([string] $Path)

    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)

    try {
        $hashes = @{}

        foreach ($entry in $zip.Entries) {
            $stream = $entry.Open()

            try {
                $buffer = New-Object System.IO.MemoryStream
                $stream.CopyTo($buffer)
                $hashes[$entry.FullName] = [System.Convert]::ToHexString(
                    [System.Security.Cryptography.SHA256]::HashData($buffer.ToArray()))
                $buffer.Dispose()
            }
            finally {
                $stream.Dispose()
            }
        }

        return $hashes
    }
    finally {
        $zip.Dispose()
    }
}

Push-Location $Root

try {
    $first = Join-Path $Root 'artifacts/repro1'
    $second = Join-Path $Root 'artifacts/repro2'

    Remove-Item -LiteralPath $first, $second -Recurse -Force -ErrorAction SilentlyContinue

    dotnet pack ./SharpCorePDF.slnx -c Release -o $first -p:ContinuousIntegrationBuild=true --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'the first pack failed' }

    dotnet pack ./SharpCorePDF.slnx -c Release -o $second -p:ContinuousIntegrationBuild=true --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'the second pack failed' }

    $packages = @(Get-ChildItem $first -Filter *.nupkg)
    if ($packages.Count -eq 0) { throw 'the first pack produced no package' }

    $symbols = @(Get-ChildItem $first -Filter *.snupkg)
    if ($symbols.Count -ne $packages.Count) {
        throw "$($packages.Count) package(s) but $($symbols.Count) symbol package(s)"
    }

    $problems = [System.Collections.Generic.List[string]]::new()

    foreach ($package in $packages) {
        $other = Join-Path $second $package.Name

        if (-not (Test-Path -LiteralPath $other)) {
            $problems.Add("$($package.Name) is missing from the second pack")
            continue
        }

        $a = Get-EntryHashes $package.FullName
        $b = Get-EntryHashes $other

        foreach ($entry in $a.Keys) {
            if (-not $b.ContainsKey($entry)) {
                $problems.Add("$($package.Name): $entry is only in the first pack")
            }
            elseif ($b[$entry] -ne $a[$entry]) {
                $problems.Add("$($package.Name): $entry differs")
            }
        }

        foreach ($entry in $b.Keys) {
            if (-not $a.ContainsKey($entry)) {
                $problems.Add("$($package.Name): $entry is only in the second pack")
            }
        }
    }

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "::error::$_" }
        throw "$($problems.Count) difference(s) between two packs"
    }

    Write-Output "reproducible: $($packages.Count) package(s), $($symbols.Count) symbol package(s), every entry identical"
}
finally {
    Pop-Location
}
