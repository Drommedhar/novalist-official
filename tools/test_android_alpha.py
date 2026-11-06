"""Check Android alpha publishing without a native SDK or signing key."""

import hashlib
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class AndroidAlphaTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="novalist-android-alpha-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        shutil.copyfile(ROOT / "eng/Publish-AndroidAlpha.ps1", self.root / "Publish-AndroidAlpha.ps1")
        (self.root / "Build-Android.ps1").write_text("""
param($Configuration, $Runtime, $AndroidSdkDirectory, $JavaSdkDirectory,
      $DisplayVersion, $VersionCode, $SigningKeyStore)
$PSBoundParameters | ConvertTo-Json | Set-Content "$PSScriptRoot/arguments.json"
throw 'Stopped before native Android build'
""", encoding="utf-8")

    def invoke(self, tag, code=123):
        result = subprocess.run([
            "pwsh", "-NoProfile", "-NonInteractive", "-File", str(self.root / "Publish-AndroidAlpha.ps1"),
            "-Tag", tag, "-VersionCode", str(code), "-Runtime", "android-arm64",
            "-SigningKeyStore", str(self.root / "unused.keystore"),
        ], capture_output=True, text=True, encoding="utf-8")
        self.assertNotEqual(result.returncode, 0)
        return result

    def test_display_version_matches_release_with_only_alpha_suffix(self):
        for tag, version in [("v3.6.2", "3.6.2-alpha"), ("3.6.2", "3.6.2-alpha"),
                             ("v3.7", "3.7.0-alpha"), ("v3.7.0-beta.1", "3.7.0-alpha")]:
            with self.subTest(tag=tag):
                result = self.invoke(tag)
                self.assertIn("Stopped before native Android build", result.stderr)
                arguments = json.loads((self.root / "arguments.json").read_text(encoding="utf-8-sig"))
                self.assertEqual(arguments["DisplayVersion"], version)
                self.assertEqual(arguments["Configuration"], "Release")
                self.assertEqual(arguments["Runtime"], "android-arm64")

    def test_numeric_update_code_is_independent_of_display_version(self):
        for code in (123, 456):
            with self.subTest(code=code):
                self.invoke("v3.6.2", code)
                arguments = json.loads((self.root / "arguments.json").read_text(encoding="utf-8-sig"))
                self.assertEqual(arguments["DisplayVersion"], "3.6.2-alpha")
                self.assertEqual(arguments["VersionCode"], code)

    def test_invalid_tag_is_rejected_before_building(self):
        result = self.invoke("not-a-release")
        self.assertIn("Expected a version tag", result.stderr)
        self.assertFalse((self.root / "arguments.json").exists())


class AndroidAlphaSigningTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="novalist-alpha-signing-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        eng = self.root / "eng"
        eng.mkdir()
        shutil.copyfile(ROOT / "eng/Publish-AndroidAlpha.ps1", eng / "Publish-AndroidAlpha.ps1")
        (eng / "Build-Android.ps1").write_text("", encoding="utf-8")
        apk = self.root / "Novalist.Mobile/bin/Release/net10.0-android/android-arm64/publish/com.novalist.app-Signed.apk"
        apk.parent.mkdir(parents=True)
        apk.write_bytes(b"fixture APK")
        (self.root / "sdk/build-tools/37.0.0").mkdir(parents=True)
        (self.root / "certificate.der").write_bytes(b"alpha certificate")
        self.fingerprint = hashlib.sha256(b"alpha certificate").hexdigest()
        self.asset = self.root / "artifacts/android-alpha/android-arm64/novalist-android-alpha-arm64.apk"
        (self.root / "verify.ps1").write_text("""
$ErrorActionPreference = 'Stop'
$fixture = Get-Content "$PSScriptRoot/verification.json" -Raw | ConvertFrom-Json
$javaCommand = Join-Path 'fixture-jdk' 'bin/java.exe'
Set-Item -LiteralPath "Function:global:$javaCommand" -Value {
    $fixture.output
    $global:LASTEXITCODE = $fixture.verifyExit
}
$keytoolCommand = Join-Path 'fixture-jdk' 'bin/keytool.exe'
Set-Item -LiteralPath "Function:global:$keytoolCommand" -Value {
    Copy-Item -LiteralPath "$PSScriptRoot/certificate.der" -Destination $args[$args.IndexOf('-file') + 1]
    $global:LASTEXITCODE = $fixture.certificateExit
}
& "$PSScriptRoot/eng/Publish-AndroidAlpha.ps1" -Tag v3.7.0 -VersionCode 123 `
    -Runtime android-arm64 -SigningKeyStore unused.keystore `
    -AndroidSdkDirectory "$PSScriptRoot/sdk" -JavaSdkDirectory fixture-jdk
""", encoding="utf-8")

    def invoke(self, lines, verify_exit=0, certificate_exit=0):
        (self.root / "verification.json").write_text(json.dumps({
            "output": lines, "verifyExit": verify_exit, "certificateExit": certificate_exit,
        }), encoding="utf-8")
        return subprocess.run([
            "pwsh", "-NoProfile", "-NonInteractive", "-File", str(self.root / "verify.ps1"),
        ], capture_output=True, text=True, encoding="utf-8", timeout=15)

    def test_verified_alpha_certificate_stages_apk_and_checksum(self):
        for label in ("Signer #1", "V3.0 Signer:",
                      "Signer (minSdkVersion=33, maxSdkVersion=2147483647)",
                      "V3.1 Signer: (minSdkVersion=33, maxSdkVersion=2147483647)"):
            with self.subTest(label=label):
                result = self.invoke([
                    "Verifies", "Number of signers: 1",
                    f"{label} certificate SHA-256 digest: {self.fingerprint}",
                ])
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(self.asset.read_bytes(), b"fixture APK")
                self.assertEqual(self.asset.with_suffix(".apk.sha256").read_text().strip(),
                                 f"{hashlib.sha256(b'fixture APK').hexdigest()}  {self.asset.name}")

    def test_wrong_missing_or_unrelated_certificate_never_stages_apk(self):
        for lines in (["Verifies"],
                      [f"Signer #1 certificate SHA-256 digest: {'0' * 64}"],
                      [f"V3.0 Signer: certificate SHA-256 digest: {'0' * 64}"],
                      [f"V3.0 Signer: public key SHA-256 digest: {self.fingerprint}"],
                      [f"Source Stamp Signer certificate SHA-256 digest: {self.fingerprint}"],
                      [f"V3.0 Signer: certificate SHA-256 digest: {self.fingerprint}",
                       f"V3.1 Signer: certificate SHA-256 digest: {'0' * 64}"]):
            with self.subTest(lines=lines):
                result = self.invoke(lines)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("not signed with the configured alpha key", result.stderr)
                self.assertFalse(self.asset.exists())

    def test_tool_failures_never_stage_apk(self):
        for verify_exit, certificate_exit, message in (
            (1, 0, "APK signature verification failed"),
            (0, 1, "Could not read the alpha signing certificate"),
        ):
            with self.subTest(message=message):
                result = self.invoke([
                    f"Signer #1 certificate SHA-256 digest: {self.fingerprint}",
                ], verify_exit, certificate_exit)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(message, result.stderr)
                self.assertFalse(self.asset.exists())


if __name__ == "__main__":
    unittest.main()
