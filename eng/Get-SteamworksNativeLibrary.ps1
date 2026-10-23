[CmdletBinding()]
param(
    [string] $SteamworksSdkArchive = $env:SteamworksSdkArchive,
    [string] $SteamworksNativeLibrary = $env:SteamworksNativeLibrary
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SteamworksNativeLibrary)) {
    if ([string]::IsNullOrWhiteSpace($SteamworksSdkArchive)) {
        $SteamworksSdkArchive = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads/steamworks_sdk_165.zip'
    }
    if (-not (Test-Path -LiteralPath $SteamworksSdkArchive -PathType Leaf)) {
        throw 'Set SteamworksSdkArchive to your licensed steamworks_sdk_165.zip, or SteamworksNativeLibrary to its win64 steam_api64.dll.'
    }
    $nativeDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'app/out/steamworks-native/1.65/win64'
    New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
    $SteamworksNativeLibrary = Join-Path $nativeDirectory 'steam_api64.dll'
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $SteamworksSdkArchive).Path)
    try {
        $entry = $archive.GetEntry('sdk/redistributable_bin/win64/steam_api64.dll')
        if ($null -eq $entry) { throw 'The SDK archive does not contain redistributable_bin/win64/steam_api64.dll.' }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $SteamworksNativeLibrary, $true)
    }
    finally { $archive.Dispose() }
}

$nativePath = (Resolve-Path -LiteralPath $SteamworksNativeLibrary).Path
$library = [System.Runtime.InteropServices.NativeLibrary]::Load($nativePath)
try {
    foreach ($symbol in @('SteamAPI_InitFlat', 'SteamAPI_SteamUtils_v011', 'SteamAPI_SteamMatchmakingServers_v003')) {
        $address = [IntPtr]::Zero
        if (-not [System.Runtime.InteropServices.NativeLibrary]::TryGetExport($library, $symbol, [ref] $address)) {
            throw "The Steam DLL is incompatible with the SDK 1.65 binding (missing $symbol). Use steamworks_sdk_165.zip."
        }
    }
}
finally { [System.Runtime.InteropServices.NativeLibrary]::Free($library) }
$nativePath
