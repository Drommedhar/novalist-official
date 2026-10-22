[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$fixturePath = Join-Path $workspacePath 'app/out'
$serverProject = Join-Path $fixturePath 'collaboration-sync-server/src/Novalist.Sync.Server/Novalist.Sync.Server.csproj'
if (-not (Test-Path -LiteralPath $serverProject)) {
    throw "Missing local collaboration server: $serverProject. See docs/development/collaboration-testing.md."
}

dotnet build $serverProject
if ($LASTEXITCODE -ne 0) { throw 'The local collaboration server build failed.' }

