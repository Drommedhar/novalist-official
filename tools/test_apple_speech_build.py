"""MSBuild regression checks for per-configuration/RID native output isolation."""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent


@unittest.skipUnless(shutil.which("dotnet"), ".NET SDK required")
class AppleSpeechBuildTests(unittest.TestCase):
    def test_mobile_project_graph_has_no_duplicate_build_outputs(self):
        with tempfile.TemporaryDirectory(prefix="novalist-ios-graph-") as directory:
            project = Path(directory) / "Probe.csproj"
            targets = Path(directory) / "Inspect.targets"
            mobile = ROOT / "Novalist.Mobile" / "Novalist.Mobile.csproj"
            references = ET.parse(mobile).findall(".//ProjectReference")
            for reference in references:
                reference.set(
                    "Include",
                    str(
                        (
                            mobile.parent / reference.get("Include").replace("\\", "/")
                        ).resolve()
                    ),
                )
            project.write_text(
                """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>"""
                + "".join(
                    ET.tostring(reference, encoding="unicode")
                    for reference in references
                )
                + "</ItemGroup></Project>"
            )
            targets.write_text("""<Project>
<ItemGroup><ProjectReferenceTargets Include="InspectBuildOutput" Targets="InspectBuildOutput" /></ItemGroup>
<Target Name="InspectBuildOutput">
  <Message Importance="high" Text="BUILD_OUTPUT|$(TargetPath)" />
</Target></Project>""")
            result = subprocess.run(
                [
                    "dotnet",
                    "msbuild",
                    str(project),
                    "-graphBuild",
                    "-t:InspectBuildOutput",
                    "-p:RuntimeIdentifier=ios-arm64",
                    f"-p:CustomAfterMicrosoftCommonTargets={targets}",
                ],
                capture_output=True,
                text=True,
                timeout=60,
                check=True,
            )
            outputs = [
                line.split("BUILD_OUTPUT|", 1)[1]
                for line in result.stdout.splitlines()
                if "BUILD_OUTPUT|" in line
            ]
            self.assertGreaterEqual(len(outputs), 4)
            self.assertEqual(len(outputs), len(set(outputs)), result.stdout)

    def test_ios_restore_graph_excludes_desktop_web_runtime(self):
        # NuGet restore does not use ProjectReference.AdditionalProperties.
        # Exercise the graph (not just MSBuild evaluation) without an Apple SDK.
        with tempfile.TemporaryDirectory(prefix="novalist-ios-restore-") as directory:
            project = Path(directory) / "Probe.csproj"
            graph_path = Path(directory) / "restore.json"
            backend = ROOT / "Novalist.Backend" / "Novalist.Backend.csproj"
            project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{escape(str(backend))}"
      AdditionalProperties="BuildAppleDesktopSpeech=false;EnableImportApi=false" />
  </ItemGroup>
</Project>''')
            for rid in (
                "ios-arm64",
                "iossimulator-arm64",
                "iossimulator-x64",
                "win-x64",
                "osx-arm64",
                "linux-x64",
            ):
                with self.subTest(rid=rid):
                    subprocess.run(
                        [
                            "dotnet",
                            "msbuild",
                            str(project),
                            "-t:GenerateRestoreGraphFile",
                            f"-p:RuntimeIdentifier={rid}",
                            f"-p:RestoreGraphOutputPath={graph_path}",
                        ],
                        capture_output=True,
                        text=True,
                        timeout=60,
                        check=True,
                    )
                    graph = json.loads(graph_path.read_text())
                    entry = next(
                        value
                        for key, value in graph["projects"].items()
                        if Path(key).resolve() == backend.resolve()
                    )
                    references = entry["frameworks"]["net8.0"]["frameworkReferences"]
                    self.assertEqual(
                        "Microsoft.AspNetCore.App" in references,
                        not rid.startswith("ios"),
                    )

    def evaluate(self, platform, configuration, rid="", enabled=True):
        # No Apple workload/restore required: reproduce the real SDK import
        # ordering while only running the native configuration target.
        with tempfile.TemporaryDirectory(
            prefix="novalist-speech-msbuild-"
        ) as directory:
            project = Path(directory) / "Probe.csproj"
            project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <Import Project="{escape(str(ROOT / "native" / "AppleSpeech" / f"AppleSpeech.{platform}.targets"))}" />
</Project>''')
            result = subprocess.run(
                [
                    "dotnet",
                    "msbuild",
                    str(project),
                    f"-t:Configure{'Mac' if platform == 'Mac' else 'Ios'}Speech",
                    f"-p:Configuration={configuration}",
                    f"-p:RuntimeIdentifier={rid}",
                    f"-p:BuildAppleDesktopSpeech={str(enabled).lower()}",
                    "-getProperty:IntermediateOutputPath,_SpeechLibrary,_SpeechArchive,_SpeechArch,_SpeechPlatform",
                    "-getItem:NativeReference",
                ],
                capture_output=True,
                text=True,
                timeout=30,
                check=True,
            )
            return json.loads(result.stdout)

    @unittest.skipUnless(sys.platform == "darwin", "Mac target requires macOS")
    def test_mac_outputs_follow_final_configuration_and_runtime(self):
        outputs = set()
        for configuration in ("Debug", "Release"):
            for rid, arch in (("osx-arm64", "arm64"), ("osx-x64", "x86_64")):
                with self.subTest(configuration=configuration, rid=rid):
                    properties = self.evaluate("Mac", configuration, rid)["Properties"]
                    intermediate = properties["IntermediateOutputPath"].replace(
                        "\\", "/"
                    )
                    self.assertEqual(
                        properties["_SpeechLibrary"].replace("\\", "/"),
                        f"{intermediate}apple-speech/{arch}/libNovalistSpeech.dylib",
                    )
                    self.assertIn(f"/{configuration}/net8.0/{rid}/", intermediate)
                    self.assertEqual(properties["_SpeechArch"], arch)
                    outputs.add(properties["_SpeechLibrary"])
        self.assertEqual(len(outputs), 4)

    def test_mobile_reference_tracks_final_configuration_and_runtime(self):
        outputs = set()
        for configuration in ("Debug", "Release"):
            for rid, arch, platform in (
                ("ios-arm64", "arm64", "ios"),
                ("iossimulator-arm64", "arm64", "simulator"),
                ("iossimulator-x64", "x86_64", "simulator"),
            ):
                with self.subTest(configuration=configuration, rid=rid):
                    result = self.evaluate("iOS", configuration, rid)
                    properties = result["Properties"]
                    intermediate = properties["IntermediateOutputPath"].replace(
                        "\\", "/"
                    )
                    archive = f"{intermediate}apple-speech/{rid}/libNovalistSpeech.a"
                    self.assertEqual(
                        properties["_SpeechArchive"].replace("\\", "/"), archive
                    )
                    self.assertEqual(properties["_SpeechArch"], arch)
                    self.assertEqual(properties["_SpeechPlatform"], platform)
                    references = result["Items"]["NativeReference"]
                    self.assertEqual(len(references), 1)
                    self.assertEqual(
                        references[0]["Identity"].replace("\\", "/"), archive
                    )
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
