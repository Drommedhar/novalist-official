#!/usr/bin/env pwsh
# Publish a shields.io "endpoint" JSON for the coverage badge to an orphan `badges`
# branch, using the built-in GITHUB_TOKEN (no external service / gist needed).
# The README points shields at the raw JSON, so the badge stays live.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Coverage,    # e.g. "100" or "99.2"
    [Parameter(Mandatory)] [string] $Repository,  # "owner/repo"
    [string] $Branch = 'badges',
    [string] $FileName = 'coverage.json'
)

$ErrorActionPreference = 'Stop'

$pct = [double]$Coverage
$color =
    if ($pct -ge 100)    { 'brightgreen' }
    elseif ($pct -ge 90) { 'green' }
    elseif ($pct -ge 75) { 'yellowgreen' }
    elseif ($pct -ge 50) { 'orange' }
    else                 { 'red' }

# Trim a trailing ".0" for a tidy "100%" instead of "100.0%".
$msg = ($Coverage -replace '\.0+$', '') + '%'
$json = (@{ schemaVersion = 1; label = 'coverage'; message = $msg; color = $color } | ConvertTo-Json -Compress)

$token = $env:GH_TOKEN
if ([string]::IsNullOrEmpty($token)) { throw 'GH_TOKEN is not set.' }

$work = Join-Path ([System.IO.Path]::GetTempPath()) "badge-$([guid]::NewGuid().ToString('N'))"
$remote = "https://x-access-token:$token@github.com/$Repository.git"

# Clone just the badges branch if it exists; otherwise start an orphan branch.
git clone --depth 1 --branch $Branch $remote $work 2>$null
if (-not (Test-Path $work)) {
    git clone --depth 1 $remote $work
    git -C $work checkout --orphan $Branch
    git -C $work rm -rf . 2>$null | Out-Null
}

Set-Content -Path (Join-Path $work $FileName) -Value $json -NoNewline
git -C $work add $FileName
git -C $work -c user.name='github-actions[bot]' `
            -c user.email='github-actions[bot]@users.noreply.github.com' `
            commit -m "chore: coverage badge $msg [skip ci]" 2>$null

if ($LASTEXITCODE -ne 0) {
    Write-Host "Coverage badge unchanged ($msg) — nothing to push."
    return
}

git -C $work push origin "HEAD:$Branch"
Write-Host "Published coverage badge: $msg ($color)"
