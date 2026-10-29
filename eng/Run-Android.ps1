[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Phone', 'Tablet')][string]$Device,
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME
)

$ErrorActionPreference = 'Stop'
if (!$AndroidSdkDirectory) { $AndroidSdkDirectory = Join-Path $env:LOCALAPPDATA 'Android/Sdk' }
$adb = Join-Path $AndroidSdkDirectory 'platform-tools/adb.exe'
$emulator = Join-Path $AndroidSdkDirectory 'emulator/emulator.exe'
$avd = if ($Device -eq 'Phone') { 'Novalist_Phone_API36' } else { 'Novalist_Tablet_API36' }
$gpu = 'host'
$apk = Join-Path (Split-Path $PSScriptRoot -Parent) 'Novalist.Mobile/bin/Debug/net10.0-android/android-x64/com.novalist.app-Signed.apk'

foreach ($path in @($adb, $emulator, $apk)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file not found: $path. Run eng/Build-Android.ps1 and check the Android SDK installation." }
}
if ($avd -notin (& $emulator -list-avds)) { throw "Emulator '$avd' is missing. See docs/android-development.md for setup." }

& (Join-Path $PSScriptRoot 'Start-AndroidDriveShare.ps1')

function Get-EmulatorSerial {
    foreach ($line in (& $adb devices)) {
        if ($line -notmatch '^(emulator-\d+)\s+device$') { continue }
        $candidate = $Matches[1]
        $name = & $adb -s $candidate emu avd name 2>$null
        if ($LASTEXITCODE -eq 0 -and $avd -in $name) { return $candidate }
    }
    return $null
}

& $adb start-server
if ($LASTEXITCODE -ne 0) { throw 'Could not start the Android debug bridge.' }
$serial = Get-EmulatorSerial
if ($serial) {
    $avdPattern = '(?:^|\s)(?:-avd\s+"?' + [regex]::Escape($avd) + '"?|@' + [regex]::Escape($avd) + ')(?=\s|$)'
    $headlessProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'emulator.exe' OR Name LIKE 'qemu-system-%'" |
        Where-Object { $_.CommandLine -match $avdPattern -and $_.CommandLine -match '(?:^|\s)-no-window(?:\s|$)' } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
    if ($headlessProcesses.Count -gt 0) {
        Write-Host "Restarting the headless $avd with a visible window; saved project data will be retained."
        & $adb -s $serial emu kill
        if ($LASTEXITCODE -ne 0) { throw 'Could not stop the headless emulator.' }
        $headlessProcesses | Wait-Process -Timeout 30
        $serial = $null
    }
}
$emulatorProcess = $null
if (!$serial) {
    Write-Host "Opening $avd..."
    $emulatorProcess = Start-Process -FilePath $emulator -ArgumentList @(
        '-avd', $avd, '-no-snapshot', '-no-metrics', '-gpu', $gpu, '-feature', '-Vulkan'
    ) -WindowStyle Normal -PassThru
} else {
    Write-Host "Using $avd on $serial."
}

$deadline = (Get-Date).AddMinutes(5)
$ready = $false
while ((Get-Date) -lt $deadline) {
    if ($emulatorProcess -and $emulatorProcess.HasExited) { throw "The emulator exited before startup completed (exit $($emulatorProcess.ExitCode))." }
    if (!$serial) { $serial = Get-EmulatorSerial }
    if ($serial) {
        $booted = & $adb -s $serial shell getprop sys.boot_completed 2>$null
        if ($LASTEXITCODE -eq 0 -and "$booted".Trim() -eq '1') {
            $package = & $adb -s $serial shell pm path android 2>$null
            if ($LASTEXITCODE -eq 0 -and "$package" -match '^package:') { $ready = $true; break }
        }
    }
    Start-Sleep -Seconds 2
}
if (!$ready) { throw "Emulator '$avd' did not become ready within five minutes." }

Write-Host "Installing Novalist on $avd..."
& $adb -s $serial install -r --no-incremental $apk
if ($LASTEXITCODE -ne 0) { throw 'Installing the Android APK failed.' }
$activity = & $adb -s $serial shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER com.novalist.app
if ($LASTEXITCODE -ne 0 -or "$activity" -notmatch 'com\.novalist\.app/[^\s]+') { throw 'Could not find the Novalist launcher activity.' }
$component = $Matches[0]
& $adb -s $serial shell am start -S -W -n $component
if ($LASTEXITCODE -ne 0) { throw 'Launching Novalist failed.' }
Write-Host "Novalist is running on $avd. The emulator stays open after this launcher finishes."
