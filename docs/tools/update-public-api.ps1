param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$ApiFile,
    [string]$Framework = 'net10.0'
)

# Collects every RS0016 symbol the compiler reports and appends it to the approved-API snapshot in the
# exact format the analyzer expects: the text inside the quotes *is* the snapshot line.
$output = & dotnet build $Project -c Debug -f $Framework -v q --nologo 2>&1 | Out-String -Width 1000
$symbols = [regex]::Matches($output, "Symbol '([^']+)' is not part of the declared public API") |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique

if ($symbols.Count -eq 0) {
    Write-Output "no RS0016 symbols for $Project"
    exit 0
}

$existing = @(Get-Content $ApiFile)
$header = @($existing | Where-Object { $_ -like '#nullable*' })
$body = @($existing | Where-Object { $_ -notlike '#nullable*' }) + @($symbols)
$body = @($body | Sort-Object -Unique)

$lines = @('#nullable enable') + @($body | Where-Object { $_ -ne '#nullable enable' })
Set-Content -Path $ApiFile -Value $lines -Encoding utf8NoBOM

Write-Output "added $($symbols.Count) symbol(s) to $ApiFile"
