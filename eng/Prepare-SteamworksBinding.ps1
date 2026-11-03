[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$revision = 'ba71581f1ed7349e8d0f17ddc6f135dd3bc8a6a3'
$expectedHash = 'AA3729AA825072366983F0DF7E6802BC42CD744E375077EA97CDC64D6F36BC7C'
$cachePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'app/out/steamworks-binding'
$sourcePath = Join-Path $cachePath $revision
$archivePath = Join-Path $cachePath "$revision.zip"
New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    Invoke-WebRequest "https://codeload.github.com/rlabrecque/Steamworks.NET/zip/$revision" -OutFile $archivePath
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw "Steamworks.NET archive checksum mismatch. Remove '$archivePath' and retry."
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $prefix = "Steamworks.NET-$revision/com.rlabrecque.steamworks.net/Runtime/"
    $runtimePath = [System.IO.Path]::GetFullPath((Join-Path $sourcePath 'Runtime'))
    foreach ($entry in $archive.Entries) {
        if (-not $entry.FullName.StartsWith($prefix) -or -not $entry.FullName.EndsWith('.cs')) { continue }
        $destination = [System.IO.Path]::GetFullPath((Join-Path $runtimePath $entry.FullName.Substring($prefix.Length)))
        if (-not $destination.StartsWith($runtimePath + [System.IO.Path]::DirectorySeparatorChar)) {
            throw 'Steamworks.NET archive contains a path outside its runtime directory.'
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
    }
    $license = $archive.GetEntry("Steamworks.NET-$revision/LICENSE.txt")
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($license, (Join-Path $sourcePath 'LICENSE.txt'), $true)
}
finally { $archive.Dispose() }
Write-Host "Prepared Steamworks.NET for SDK 1.65 ($revision)."
