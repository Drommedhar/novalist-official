"""MSBuild regression checks for per-configuration/RID native output isolation."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parent.parent


@unittest.skipUnless(shutil.which("dotnet"), ".NET SDK required")
class AppleSpeechBuildTests(unittest.TestCase):
    def evaluate(self, platform, configuration, rid="", enabled=True):
        # No Apple workload/restore required: reproduce the real SDK import
        # ordering while only running the native configuration target.
        with tempfile.TemporaryDirectory(prefix="novalist-speech-msbuild-") as directory:
            project = Path(directory) / "Probe.csproj"
            project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <Import Project="{escape(str(ROOT / 'native' / 'AppleSpeech' / f'AppleSpeech.{platform}.targets'))}" />
</Project>''')
            result = subprocess.run([
                "dotnet", "msbuild", str(project),
                f"-t:Configure{'Mac' if platform == 'Mac' else 'Ios'}Speech",
                f"-p:Configuration={configuration}", f"-p:RuntimeIdentifier={rid}",
                f"-p:BuildAppleDesktopSpeech={str(enabled).lower()}",
                "-getProperty:IntermediateOutputPath,_SpeechLibrary,_SpeechArchive,_SpeechArch,_SpeechPlatform",
                "-getItem:NativeReference",
            ], capture_output=True, text=True, timeout=30, check=True)
            return json.loads(result.stdout)

    @unittest.skipUnless(sys.platform == "darwin", "Mac target requires macOS")
    def test_mac_outputs_follow_final_configuration_and_runtime(self):
        outputs = set()
        for configuration in ("Debug", "Release"):
            for rid, arch in (("osx-arm64", "arm64"), ("osx-x64", "x86_64")):
                with self.subTest(configuration=configuration, rid=rid):
                    properties = self.evaluate("Mac", configuration, rid)["Properties"]
                    intermediate = properties["IntermediateOutputPath"].replace("\\", "/")
                    self.assertEqual(properties["_SpeechLibrary"].replace("\\", "/"),
                                     f"{intermediate}apple-speech/{arch}/libNovalistSpeech.dylib")
                    self.assertIn(f"/{configuration}/net8.0/{rid}/", intermediate)
                    self.assertEqual(properties["_SpeechArch"], arch)
                    outputs.add(properties["_SpeechLibrary"])
        self.assertEqual(len(outputs), 4)

    def test_mobile_reference_tracks_final_configuration_and_runtime(self):
        outputs = set()
        for configuration in ("Debug", "Release"):
            for rid, arch, platform in (("ios-arm64", "arm64", "ios"),
                                        ("iossimulator-arm64", "arm64", "simulator"),
                                        ("iossimulator-x64", "x86_64", "simulator")):
                with self.subTest(configuration=configuration, rid=rid):
                    result = self.evaluate("iOS", configuration, rid)
                    properties = result["Properties"]
                    intermediate = properties["IntermediateOutputPath"].replace("\\", "/")
                    archive = f"{intermediate}apple-speech/{rid}/libNovalistSpeech.a"
                    self.assertEqual(properties["_SpeechArchive"].replace("\\", "/"), archive)
                    self.assertEqual(properties["_SpeechArch"], arch)
                    self.assertEqual(properties["_SpeechPlatform"], platform)
                    references = result["Items"]["NativeReference"]
                    self.assertEqual(len(references), 1)
                    self.assertEqual(references[0]["Identity"].replace("\\", "/"), archive)
                    self.assertEqual(references[0]["ForceLoad"], "true")
                    self.assertEqual(references[0]["SmartLink"], "false")
                    outputs.add(archive)
        self.assertEqual(len(outputs), 6)

    @unittest.skipUnless(sys.platform == "darwin", "Mac target requires macOS")
    def test_mobile_backend_opt_out_does_not_create_mac_library(self):
        properties = self.evaluate("Mac", "Debug", enabled=False)["Properties"]
        self.assertEqual(properties["_SpeechLibrary"], "")


if __name__ == "__main__":
    unittest.main()
