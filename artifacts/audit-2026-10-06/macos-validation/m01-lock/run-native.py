from pathlib import Path
import subprocess,sys,shutil
w=Path(__file__).resolve().parent;name=sys.argv[1];shutil.copyfile(w/(name+'.swift'),w/'harness/AuditUITests.swift')
with (w/(name+'.log')).open('w') as f:
 result=subprocess.run(['xcodebuild','test','-project',str(w/'harness/AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id=BE6B8D09-298D-4C2A-AA28-CF5946F951B3','-derivedDataPath',str(w/'harness/DerivedData'),'-parallel-testing-enabled','NO','-collect-test-diagnostics','never','-resultBundlePath',str(w/(name+'.xcresult')),'CODE_SIGNING_ALLOWED=NO'],stdout=f,stderr=subprocess.STDOUT)
print(name, result.returncode);sys.exit(result.returncode)
