[CmdletBinding()]
param(
    [string] $SteamworksSdkArchive = $env:SteamworksSdkArchive,
    [string] $SteamworksNativeLibrary = $env:SteamworksNativeLibrary
)

$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$nativePath = & (Join-Path $PSScriptRoot 'Get-SteamworksNativeLibrary.ps1') -SteamworksSdkArchive $SteamworksSdkArchive -SteamworksNativeLibrary $SteamworksNativeLibrary
& (Join-Path $PSScriptRoot 'Prepare-SteamworksBinding.ps1')
$backendProject = Join-Path $workspacePath 'Novalist.Backend/Novalist.Backend.csproj'
$artifactsPath = Join-Path $workspacePath 'app/out/collaboration-steam-build'
dotnet build $backendProject '-p:EnableSteamworks=true' "-p:SteamworksNativeLibrary=$nativePath" --artifacts-path $artifactsPath
if ($LASTEXITCODE -ne 0) { throw 'The Steam collaboration backend build failed.' }
$backendPath = Join-Path $artifactsPath 'bin/Novalist.Backend/debug'
if (-not (Test-Path -LiteralPath (Join-Path $backendPath 'steam_api64.dll'))) {
    throw 'The Steam native library was not copied into the backend output.'
}
