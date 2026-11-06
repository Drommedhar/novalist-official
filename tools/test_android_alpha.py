"""Check the Android publisher's build arguments without a native SDK or signing key."""

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


if __name__ == "__main__":
    unittest.main()
