#!/usr/bin/env pwsh
# Fail the build unless every line of the target assembly is covered.
#
# Cobertura emits the same source line across multiple state-machine/closure
# *fragments*; a naive per-line scan reports phantom gaps where the line is
# covered in another fragment. We dedupe by (filename, line) taking the max hit
# count before judging coverage.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $CoberturaGlob,  # glob to coverage.cobertura.xml
    [Parameter(Mandatory)] [string] $Package,        # assembly/package name, e.g. Novalist.Desktop
    [double] $Threshold = 100.0,
    [switch] $NoFail                                 # measure + report only; never throw on low coverage
)

$ErrorActionPreference = 'Stop'

$file = Get-ChildItem -Path $CoberturaGlob -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $file) { throw "No cobertura report found for glob: $CoberturaGlob" }

[xml]$xml = Get-Content -LiteralPath $file.FullName
$hits = @{}
foreach ($pkg in $xml.coverage.packages.package) {
    if ($pkg.name -ne $Package) { continue }
    foreach ($cls in $pkg.classes.class) {
        foreach ($ln in $cls.lines.line) {
            $key = "$($cls.filename):$($ln.number)"
            $h = [int]$ln.hits
            if (-not $hits.ContainsKey($key) -or $hits[$key] -lt $h) { $hits[$key] = $h }
        }
    }
}

$total = $hits.Count
if ($total -eq 0) { throw "Package '$Package' not found in $($file.FullName) (no lines)." }

$uncovered = @($hits.GetEnumerator() | Where-Object { $_.Value -eq 0 })
$covered = $total - $uncovered.Count
$rate = [math]::Round(100.0 * $covered / $total, 3)

Write-Host "Package $Package : $covered / $total lines ($rate%)"

# Emit for downstream steps (badge / summary) — always, even when below threshold,
# so the caller can publish the real number before failing the build.
"$Package=$rate"

if ($uncovered.Count -gt 0 -and $rate -lt $Threshold) {
    Write-Host "Uncovered lines:"
    $uncovered | Sort-Object Name | Select-Object -First 50 | ForEach-Object { Write-Host "  $($_.Name)" }
    if (-not $NoFail) {
        throw "Coverage $rate% for $Package is below threshold $Threshold%."
    }
}
