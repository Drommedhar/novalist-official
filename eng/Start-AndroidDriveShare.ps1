[CmdletBinding()]
param(
    [string]$StateDirectory = (Join-Path $env:LOCALAPPDATA 'Novalist/AndroidDriveShare'),
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
$credentialPath = Join-Path $StateDirectory 'credential.xml'
if (!(Test-Path -LiteralPath $credentialPath)) { return }
$rclone = Join-Path $StateDirectory 'rclone.exe'
$configuration = Join-Path $StateDirectory 'drives.conf'
$processState = Join-Path $StateDirectory 'process.json'
foreach ($required in @($rclone, $configuration)) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "PC drive sharing is incomplete: $required" }
}

$mutex = [Threading.Mutex]::new($false, 'Local\Novalist.AndroidDriveShare')
$locked = $false
$server = $null
try {
    try { $locked = $mutex.WaitOne([TimeSpan]::FromSeconds(15)) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw 'Another launcher is still configuring PC drive sharing.' }
    if (Test-Path -LiteralPath $processState) {
        $saved = Get-Content -LiteralPath $processState -Raw | ConvertFrom-Json
        $running = Get-Process -Id $saved.id -ErrorAction SilentlyContinue
        if ($running -and $running.Path -eq $rclone -and $running.StartTime.ToUniversalTime() -eq ([DateTime]$saved.startedAt).ToUniversalTime()) {
            if ($Stop) {
                Stop-Process -InputObject $running
                Write-Host 'PC drive sharing stopped.'
                return
            }
            Write-Host 'PC drives are available through CIFS Documents Provider.'
            return
        }
    }
    if ($Stop) { return }

    $credential = Import-Clixml -LiteralPath $credentialPath
    $server = Start-Process -FilePath $rclone -ArgumentList @(
        'serve', 'sftp', 'pc:', '--config', ('"' + $configuration + '"'),
        '--addr', '127.0.0.1:2022', '--dir-cache-time', '1s', '--poll-interval', '0',
        '--vfs-cache-mode', 'off', '--cache-dir', ('"' + (Join-Path $StateDirectory 'cache') + '"')
    ) -WindowStyle Hidden -PassThru -Environment @{
        RCLONE_USER = $credential.UserName
        RCLONE_PASS = $credential.GetNetworkCredential().Password
    } -RedirectStandardOutput (Join-Path $StateDirectory 'server.stdout.log') -RedirectStandardError (Join-Path $StateDirectory 'server.stderr.log')

    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        if ($server.HasExited) { throw "PC drive sharing failed to start. See $StateDirectory/server.stderr.log" }
        $listener = Get-NetTCPConnection -State Listen -LocalPort 2022 -ErrorAction SilentlyContinue
        if ($listener) {
            if ($listener.LocalAddress -ne '127.0.0.1' -or $listener.OwningProcess -ne $server.Id) {
                throw 'PC drive sharing port 2022 is already used by another process.'
            }
            @{ id = $server.Id; startedAt = $server.StartTime.ToUniversalTime().ToString('o') } |
                ConvertTo-Json | Set-Content -LiteralPath $processState -Encoding utf8
            Write-Host 'PC drives are available through CIFS Documents Provider.'
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw 'PC drive sharing did not start within ten seconds.'
} catch {
    if ($server -and !$server.HasExited) { Stop-Process -InputObject $server }
    throw
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
