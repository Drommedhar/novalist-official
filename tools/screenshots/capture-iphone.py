#!/usr/bin/env python3
"""Capture ten real iPhone screens on a fresh simulator seeded by sim-seed.sh.

Usage: capture-iphone.py <simulator-udid> <raw-output-directory>
Requires Xcode and the clean native app already installed. The runner navigates
through XCTest; it never injects JavaScript or changes the product UI.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time

PROJECT = '// !$*UTF8*$!\n{\n archiveVersion = 1; classes = {}; objectVersion = 56;\n objects = {\n  A00000000000000000000001 = {isa = PBXProject; attributes = {LastUpgradeCheck = 2700; }; buildConfigurationList = A00000000000000000000002; compatibilityVersion = "Xcode 14.0"; developmentRegion = en; hasScannedForEncodings = 0; knownRegions = (en, Base); mainGroup = A00000000000000000000003; productRefGroup = A00000000000000000000004; projectDirPath = ""; projectRoot = ""; targets = (A00000000000000000000005); };\n  A00000000000000000000002 = {isa = XCConfigurationList; buildConfigurations = (A00000000000000000000006); defaultConfigurationIsVisible = 0; defaultConfigurationName = Debug; };\n  A00000000000000000000003 = {isa = PBXGroup; children = (A00000000000000000000007, A00000000000000000000004); sourceTree = "<group>"; };\n  A00000000000000000000004 = {isa = PBXGroup; children = (A00000000000000000000008); name = Products; sourceTree = "<group>"; };\n  A00000000000000000000005 = {isa = PBXNativeTarget; buildConfigurationList = A00000000000000000000009; buildPhases = (A0000000000000000000000A, A0000000000000000000000B, A0000000000000000000000C); buildRules = (); dependencies = (); name = AuditUITests; productName = AuditUITests; productReference = A00000000000000000000008; productType = "com.apple.product-type.bundle.ui-testing"; };\n  A00000000000000000000006 = {isa = XCBuildConfiguration; buildSettings = {SDKROOT = iphoneos; IPHONEOS_DEPLOYMENT_TARGET = 27.0; SWIFT_VERSION = 5.0; }; name = Debug; };\n  A00000000000000000000007 = {isa = PBXFileReference; lastKnownFileType = sourcecode.swift; path = AuditUITests.swift; sourceTree = "<group>"; };\n  A00000000000000000000008 = {isa = PBXFileReference; explicitFileType = wrapper.cfbundle; includeInIndex = 0; path = AuditUITests.xctest; sourceTree = BUILT_PRODUCTS_DIR; };\n  A00000000000000000000009 = {isa = XCConfigurationList; buildConfigurations = (A0000000000000000000000D); defaultConfigurationIsVisible = 0; defaultConfigurationName = Debug; };\n  A0000000000000000000000A = {isa = PBXSourcesBuildPhase; buildActionMask = 2147483647; files = (A0000000000000000000000E); runOnlyForDeploymentPostprocessing = 0; };\n  A0000000000000000000000B = {isa = PBXFrameworksBuildPhase; buildActionMask = 2147483647; files = (); runOnlyForDeploymentPostprocessing = 0; };\n  A0000000000000000000000C = {isa = PBXResourcesBuildPhase; buildActionMask = 2147483647; files = (); runOnlyForDeploymentPostprocessing = 0; };\n  A0000000000000000000000D = {isa = XCBuildConfiguration; buildSettings = {PRODUCT_BUNDLE_IDENTIFIER = com.novalist.screenshots.uitests; PRODUCT_NAME = "$(TARGET_NAME)"; GENERATE_INFOPLIST_FILE = YES; CODE_SIGNING_ALLOWED = NO; SUPPORTED_PLATFORMS = "iphonesimulator iphoneos"; TARGETED_DEVICE_FAMILY = "1,2"; ENABLE_TESTING_SEARCH_PATHS = YES; SWIFT_EMIT_LOC_STRINGS = NO; }; name = Debug; };\n  A0000000000000000000000E = {isa = PBXBuildFile; fileRef = A00000000000000000000007; };\n };\n rootObject = A00000000000000000000001;\n}\n'

SCHEME = '<?xml version="1.0" encoding="UTF-8"?>\n<Scheme LastUpgradeVersion="2700" version="1.3">\n <BuildAction parallelizeBuildables="YES" buildImplicitDependencies="YES"><BuildActionEntries><BuildActionEntry buildForTesting="YES" buildForRunning="YES" buildForProfiling="NO" buildForArchiving="NO" buildForAnalyzing="YES"><BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="A00000000000000000000005" BuildableName="AuditUITests.xctest" BlueprintName="AuditUITests" ReferencedContainer="container:AuditHarness.xcodeproj"/></BuildActionEntry></BuildActionEntries></BuildAction>\n <TestAction buildConfiguration="Debug" selectedDebuggerIdentifier="Xcode.DebuggerFoundation.Debugger.LLDB" selectedLauncherIdentifier="Xcode.IDEFoundation.Launcher.LLDB" shouldUseLaunchSchemeArgsEnv="YES"><Testables><TestableReference skipped="NO"><BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="A00000000000000000000005" BuildableName="AuditUITests.xctest" BlueprintName="AuditUITests" ReferencedContainer="container:AuditHarness.xcodeproj"/></TestableReference></Testables></TestAction>\n</Scheme>\n'

NAMES = ["00-welcome", "01-dashboard", "02-write", "03-editor", "04-codex",
         "05-codex-entity", "06-wiki", "06-wiki-article", "07-plan-menu", "08-timeline"]

def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    udid, destination = sys.argv[1], Path(sys.argv[2]).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="novalist-phone-capture-"))
    project = work / "AuditHarness.xcodeproj"
    schemes = project / "xcshareddata/xcschemes"
    schemes.mkdir(parents=True)
    (project / "project.pbxproj").write_text(PROJECT)
    (schemes / "AuditUITests.xcscheme").write_text(SCHEME)
    source = Path(__file__).with_name("capture-iphone.swift")
    source_bytes = source.read_bytes()
    (work / "AuditUITests.swift").write_bytes(source_bytes)
    print(f"Native capture working directory: {work}", flush=True)
    common = ["xcodebuild", "-project", str(project), "-scheme", "AuditUITests",
              "-destination", f"platform=iOS Simulator,id={udid}",
              "-derivedDataPath", str(work / "DerivedData"), "CODE_SIGNING_ALLOWED=NO"]
    with (work / "build.log").open("w") as output:
        subprocess.run([*common, "build-for-testing"], check=True, stdout=output, stderr=subprocess.STDOUT)
    methods = ["test00Welcome", "test01Dashboard", "test02Write", "test03Editor", "test04Codex",
               "test05CodexEntity", "test06Wiki", "test07WikiArticle", "test08PlanMenu", "test09Timeline"]
    records = {}
    for name, method in zip(NAMES, methods):
        log = work / (name + ".log")
        with log.open("w") as output:
            completed = subprocess.run([
                *common, "test-without-building", "-collect-test-diagnostics", "never",
                "-only-testing:AuditUITests/AuditUITests/" + method,
                "-resultBundlePath", str(work / (name + ".xcresult"))],
                stdout=output, stderr=subprocess.STDOUT)
        if completed.returncode:
            print("\n".join(log.read_text().splitlines()[-30:]))
            raise SystemExit(completed.returncode)
        # Let native chrome settle after navigation, then capture the
        # original framebuffer with the fixed simulator status bar.
        time.sleep(2)
        target = destination / (name + ".png")
        subprocess.run(["xcrun", "simctl", "io", udid, "screenshot", str(target)],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        records[target.name] = hashlib.sha256(target.read_bytes()).hexdigest()
        print(f"Captured {target.name}", flush=True)
    (destination / "capture.json").write_text(json.dumps({
        "method": "Separate XCTest navigation steps followed by simctl framebuffer screenshots after each test exits; original PNG bytes",
        "simulator_udid": udid,
        "swift_sha256": hashlib.sha256(source_bytes).hexdigest(),
        "screenshots": records,
    }, indent=2) + "\n")
    print(f"Captured {len(records)} native screenshots in {destination}")

if __name__ == "__main__":
    main()
