from pathlib import Path
import subprocess,sys,shutil
w=Path(__file__).parent
name=sys.argv[1]
shutil.copy2(w/(name+'.swift'),w/'AuditUITests.swift')
command=['xcodebuild','test','-collect-test-diagnostics','never','-project',str(w/'AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id=6D872EE9-8FBE-4B2D-AED4-917146C77351','-derivedDataPath',str(w/'DerivedData'),'-resultBundlePath',str(w/(name+'.xcresult')),'CODE_SIGNING_ALLOWED=NO']
with (w/(name+'.log')).open('w') as f:r=subprocess.run(command,stdout=f,stderr=subprocess.STDOUT)
print(name,r.returncode)
print('\n'.join(line for line in (w/(name+'.log')).read_text().splitlines() if 'R15_' in line or 'error:' in line or 'Test Case' in line))
sys.exit(r.returncode)
