[CmdletBinding()]
param([string] $SteamworksNativeLibrary = $env:SteamworksNativeLibrary)

$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SteamworksNativeLibrary)) {
    $SteamworksNativeLibrary = Join-Path (Split-Path -Parent $workspacePath) 'Fireplace/src-tauri/target/debug/steam_api64.dll'
}
if (-not (Test-Path -LiteralPath $SteamworksNativeLibrary -PathType Leaf)) {
    throw 'Set SteamworksNativeLibrary to your Steamworks SDK steam_api64.dll before starting VS Code, or build the sibling Fireplace checkout first.'
}
$nativePath = (Resolve-Path -LiteralPath $SteamworksNativeLibrary).Path
$backendProject = Join-Path $workspacePath 'Novalist.Backend/Novalist.Backend.csproj'
$artifactsPath = Join-Path $workspacePath 'app/out/collaboration-steam-build'
dotnet build $backendProject '-p:EnableSteamworks=true' "-p:SteamworksNativeLibrary=$nativePath" --artifacts-path $artifactsPath
if ($LASTEXITCODE -ne 0) { throw 'The Steam collaboration backend build failed.' }
$backendPath = Join-Path $artifactsPath 'bin/Novalist.Backend/debug'
if (-not (Test-Path -LiteralPath (Join-Path $backendPath 'steam_api64.dll'))) {
    throw 'The Steam native library was not copied into the backend output.'
}
