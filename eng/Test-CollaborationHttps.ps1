[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This test runner uses the Windows current-user certificate store.' }
$workspacePath = Split-Path -Parent $PSScriptRoot
$fixturePath = Join-Path $workspacePath ('app/out/https-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixturePath | Out-Null
$password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

function New-TestCertificate([string] $Prefix) {
    $key = [Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=Novalist local HTTPS test', $key, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $names = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $names.AddDnsName('localhost')
        $names.AddIpAddress([Net.IPAddress]::Loopback)
        $request.CertificateExtensions.Add($names.Build())
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $usages = [Security.Cryptography.OidCollection]::new()
        [void] $usages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages, $true))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddHours(2))
        [IO.File]::WriteAllBytes((Join-Path $fixturePath "$Prefix.pfx"), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
        [IO.File]::WriteAllText((Join-Path $fixturePath "$Prefix.pem"), $certificate.ExportCertificatePem())
        [IO.File]::WriteAllText((Join-Path $fixturePath "$Prefix.key"), $key.ExportPkcs8PrivateKeyPem())
        return $certificate
    } finally { $key.Dispose() }
}

$trusted = New-TestCertificate 'trusted'
$untrusted = New-TestCertificate 'untrusted'
$variables = @{
    NODE_EXTRA_CA_CERTS = Join-Path $fixturePath 'trusted.pem'
    NOVALIST_COLLAB_TLS_DIR = $fixturePath
    NOVALIST_COLLAB_TLS_PASSWORD = $password
    NOVALIST_SYNC_INTEGRATION = '1'
}
$previous = @{}
try {
    certutil -user -f -addstore Root (Join-Path $fixturePath 'trusted.pem') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not trust the temporary localhost test certificate.' }
    foreach ($name in $variables.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $variables[$name], 'Process')
    }
    Push-Location (Join-Path $workspacePath 'app')
    try {
        npx playwright test e2e/collaboration-https.spec.ts --workers=1 --output out/collaboration-https-results
        if ($LASTEXITCODE -ne 0) { throw 'HTTPS collaboration acceptance failed.' }
    } finally { Pop-Location }
} finally {
    try {
        certutil -user -delstore Root $trusted.Thumbprint | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the temporary localhost test certificate.' }
    } finally {
        $trusted.Dispose()
        $untrusted.Dispose()
        foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
        foreach ($name in @('trusted.pfx', 'trusted.pem', 'trusted.key', 'untrusted.pfx', 'untrusted.pem', 'untrusted.key')) {
            Remove-Item -LiteralPath (Join-Path $fixturePath $name) -Force
        }
    }
    Write-Host 'Removed the temporary HTTPS test certificate and private keys.'
}
