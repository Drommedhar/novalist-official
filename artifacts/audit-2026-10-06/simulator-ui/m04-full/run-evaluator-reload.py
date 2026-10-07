from pathlib import Path
import os,runpy,subprocess,time,json
repo=Path('<repository>');root=Path('/private/tmp/novalist-m04-full');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
n=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));call,js=n['call'],n['evaluate']
log=root/'evaluator-reload.log'
with log.open('w') as out:
 p=subprocess.Popen(['xcodebuild','test','-project','<temporary-harness>/AuditHarness.xcodeproj','-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sim,'-derivedDataPath','<temporary-harness>/DerivedData','-parallel-testing-enabled','NO','-resultBundlePath',str(root/'evaluator-reload.xcresult'),'CODE_SIGNING_ALLOWED=NO'],stdout=out,stderr=subprocess.STDOUT)
 for _ in range(1000):
  if 'AUDIT_M04_BEFORE_ACTUAL_RELOAD' in log.read_text():break
  if p.poll() is not None:raise RuntimeError('UI test stopped before Reload')
  time.sleep(.05)
 else:raise TimeoutError('No pre-reload checkpoint')
 before=js("({journalContainsMarker:(localStorage.getItem('novalist.mobile.recovery.v1')||'').includes('M04EVAL26A'),markerInEditor:Array.from(document.querySelectorAll('iframe')).some(f=>f.contentDocument?.body.innerText.includes('M04EVAL26A'))})")
 result=p.wait(timeout=55)
 after=js("({journalContainsMarker:(localStorage.getItem('novalist.mobile.recovery.v1')||'').includes('M04EVAL26A'),markerInEditor:Array.from(document.querySelectorAll('iframe')).some(f=>f.contentDocument?.body.innerText.includes('M04EVAL26A'))})")
state=call(sim,'state')['result']
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
after['markerOnDisk']=any('M04EVAL26A' in q.read_text() for q in data.rglob('*.novalist') if q.is_file())
record={'xctest_exit_code':result,'immediatelyBeforeActualReload':before,'afterActualReload':after,'newNativePageHealthy':state['bridgeFailed']==0 and state['rendererReady'],'case_status':'failed' if result else 'passed'}
(root/'evaluator-reload-result.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps(record))
