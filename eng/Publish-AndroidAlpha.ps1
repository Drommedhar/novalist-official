[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][ValidateRange(1, 2100000000)][int]$VersionCode,
    [Parameter(Mandatory)][ValidateSet('android-x64', 'android-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$SigningKeyStore,
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME,
    [string]$JavaSdkDirectory = $env:JAVA_HOME
)

$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v?(\d+)\.(\d+)(?:\.(\d+))?(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Expected a version tag such as v3.6.2 or v3.6.2-alpha.1, got '$Tag'."
}
$patch = if ($Matches[3]) { $Matches[3] } else { '0' }
$version = "$($Matches[1]).$($Matches[2]).$patch-alpha.$VersionCode"
$repo = Split-Path $PSScriptRoot -Parent
if (!$AndroidSdkDirectory -and $env:LOCALAPPDATA) {
    $AndroidSdkDirectory = Join-Path $env:LOCALAPPDATA 'Android/Sdk'
}
if (!$JavaSdkDirectory -and $env:LOCALAPPDATA) {
    $JavaSdkDirectory = Join-Path $env:LOCALAPPDATA 'Android/jdk'
}
& "$PSScriptRoot/Build-Android.ps1" -Configuration Release -Runtime $Runtime `
    -AndroidSdkDirectory $AndroidSdkDirectory -JavaSdkDirectory $JavaSdkDirectory `
    -DisplayVersion $version -VersionCode $VersionCode -SigningKeyStore $SigningKeyStore

$apk = Join-Path $repo "Novalist.Mobile/bin/Release/net10.0-android/$Runtime/publish/com.novalist.app-Signed.apk"
$buildTools = Get-ChildItem -LiteralPath (Join-Path $AndroidSdkDirectory 'build-tools') -Directory |
    Where-Object Name -Match '^\d+\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$buildTools) { throw 'Android SDK build-tools are missing.' }
$signer = Join-Path $buildTools.FullName 'lib/apksigner.jar'
$verification = & (Join-Path $JavaSdkDirectory 'bin/java.exe') -jar $signer verify --verbose --print-certs $apk
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }

$certificate = Join-Path ([IO.Path]::GetTempPath()) ("novalist-alpha-{0}.der" -f [guid]::NewGuid())
try {
    & (Join-Path $JavaSdkDirectory 'bin/keytool.exe') -exportcert -keystore $SigningKeyStore `
        -alias novalist-alpha -storepass:env ANDROID_SIGNING_PASSWORD -file $certificate
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the alpha signing certificate.' }
    $fingerprint = (Get-FileHash -LiteralPath $certificate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($verification -notcontains "Signer #1 certificate SHA-256 digest: $fingerprint") {
        throw 'The APK was not signed with the configured alpha key.'
    }
} finally {
    if (Test-Path -LiteralPath $certificate) { Remove-Item -LiteralPath $certificate }
}

$destination = Join-Path $repo "artifacts/android-alpha/$Runtime"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$name = "novalist-android-alpha-$($Runtime.Replace('android-', '')).apk"
$asset = Join-Path $destination $name
Copy-Item -LiteralPath $apk -Destination $asset
$hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$asset.sha256" -Value "$hash  $name" -Encoding ascii
Write-Host "Android alpha $version ($Runtime), certificate SHA-256: $fingerprint"
Write-Host "Release assets: $asset and $asset.sha256"
