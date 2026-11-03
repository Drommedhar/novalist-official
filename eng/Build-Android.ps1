[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('android-x64', 'android-arm64')][string]$Runtime = 'android-x64',
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME,
    [string]$JavaSdkDirectory = $env:JAVA_HOME,
    [string]$DisplayVersion = '0.1.0',
    [ValidateRange(1, 2100000000)][int]$VersionCode = 1,
    [string]$SigningKeyStore,
    [string]$SigningKeyAlias = 'novalist-alpha',
    [switch]$SkipRenderer
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$AndroidSdkDirectory -and $env:LOCALAPPDATA) {
    $AndroidSdkDirectory = Join-Path $env:LOCALAPPDATA 'Android/Sdk'
}
if (!$JavaSdkDirectory -and $env:LOCALAPPDATA) {
    $JavaSdkDirectory = Join-Path $env:LOCALAPPDATA 'Android/jdk'
}
foreach ($directory in @($AndroidSdkDirectory, $JavaSdkDirectory)) {
    if (!$directory -or !(Test-Path -LiteralPath $directory -PathType Container)) {
        throw 'Set AndroidSdkDirectory/ANDROID_HOME and JavaSdkDirectory/JAVA_HOME to installed SDK and JDK directories.'
    }
}
$signing = @()
if ($SigningKeyStore) {
    if (!(Test-Path -LiteralPath $SigningKeyStore -PathType Leaf) -or !$env:ANDROID_SIGNING_PASSWORD) {
        throw 'Signing requires an existing keystore and ANDROID_SIGNING_PASSWORD.'
    }
    $signing = @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$SigningKeyStore",
        "-p:AndroidSigningKeyAlias=$SigningKeyAlias", '-p:AndroidSigningStorePass=env:ANDROID_SIGNING_PASSWORD',
        '-p:AndroidSigningKeyPass=env:ANDROID_SIGNING_PASSWORD')
}

Push-Location $repo
try {
    if (!$SkipRenderer) {
        Push-Location app
        try {
            & npm run build:mobile
            if ($LASTEXITCODE -ne 0) { throw 'Mobile renderer build failed.' }
        }
        finally { Pop-Location }
    }
    $verb = if ($Configuration -eq 'Release') { 'publish' } else { 'build' }
    & dotnet $verb Novalist.Mobile/Novalist.Mobile.csproj -f net10.0-android -c $Configuration -r $Runtime -maxcpucount:1 `
        -p:EnableImportApi=false -p:UseMonoRuntime=true `
        "-p:ApplicationDisplayVersion=$DisplayVersion" "-p:ApplicationVersion=$VersionCode" @signing `
        "-p:AndroidSdkDirectory=$AndroidSdkDirectory" "-p:JavaSdkDirectory=$JavaSdkDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Android build failed.' }
    $output = "Novalist.Mobile/bin/$Configuration/net10.0-android/$Runtime"
    if ($Configuration -eq 'Release') { $output += '/publish' }
    $apk = Join-Path $repo "$output/com.novalist.app-Signed.apk"
    if (!(Test-Path -LiteralPath $apk)) { throw 'The build did not produce the expected signed APK.' }
    Get-FileHash -LiteralPath $apk -Algorithm SHA256
}
finally { Pop-Location }
