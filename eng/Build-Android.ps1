[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('android-x64', 'android-arm64')][string]$Runtime = 'android-x64',
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME,
    [string]$JavaSdkDirectory = $env:JAVA_HOME,
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
    & dotnet build Novalist.Mobile/Novalist.Mobile.csproj -f net10.0-android -c $Configuration -r $Runtime `
        -p:EnableImportApi=false -p:UseMonoRuntime=true `
        "-p:AndroidSdkDirectory=$AndroidSdkDirectory" "-p:JavaSdkDirectory=$JavaSdkDirectory"
    if ($LASTEXITCODE -ne 0) { throw 'Android build failed.' }
    $apk = Join-Path $repo "Novalist.Mobile/bin/$Configuration/net10.0-android/$Runtime/com.novalist.app-Signed.apk"
    if (!(Test-Path -LiteralPath $apk)) { throw 'The build did not produce the expected signed APK.' }
    Get-FileHash -LiteralPath $apk -Algorithm SHA256
}
finally { Pop-Location }
