from pathlib import Path
import os,runpy,subprocess,time,json
repo=Path('<repository>')
root=Path('/private/tmp/novalist-m04-full')
sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
probe=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'))
call,js=probe['call'],probe['evaluate']
assert call(sim,'state')['result']['bridgeFailed']==0
log=root/'evaluator-input.log'
with log.open('w') as out:
 p=subprocess.Popen(['xcodebuild','test','-project','<temporary-harness>/AuditHarness.xcodeproj','-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sim,'-derivedDataPath','<temporary-harness>/DerivedData','-parallel-testing-enabled','NO','-resultBundlePath',str(root/'evaluator-input.xcresult'),'CODE_SIGNING_ALLOWED=NO'],stdout=out,stderr=subprocess.STDOUT)
 deadline=time.monotonic()+60
 while time.monotonic()<deadline:
  if 'AUDIT_M04_ARM_NOW' in log.read_text():break
  if p.poll() is not None:raise RuntimeError('UI test stopped before fault coordination')
  time.sleep(.02)
 else:raise TimeoutError('No XCTest fault coordination marker')
 time.sleep(1.5)
 assert call(sim,'evaluator-native-failure')['ok']
 result=p.wait(timeout=35)
state=call(sim,'state')['result']
marker='M04EVAL26A'
observed=js("(()=>{let marker='M04EVAL26A';return {markerInEditor:Array.from(document.querySelectorAll('iframe')).some(f=>f.contentDocument?.body.innerText.includes(marker)),journalContainsMarker:(localStorage.getItem('novalist.mobile.recovery.v1')||'').includes(marker),inertElementPresent:!!document.querySelector('[inert]')}})()")
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
observed['markerOnDisk']=any(marker in p.read_text() for p in data.rglob('*.novalist') if p.is_file())
record={'xctest_exit_code':result,'beforeReload':observed,'native_state':state,'source_revision':'0c789b29581db47d89593c897bd53fbb3fb9275c','probe_diff_sha256':'2d88274ef724e09d0bdfc0bd3a4885112d88f7d80002594cc5730a6f431795e8'}
(root/'evaluator-before-reload.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps({'xctest_exit_code':result,'beforeReload':observed,'nativeAlert':state['nativeEditingPausedAlert']}))
