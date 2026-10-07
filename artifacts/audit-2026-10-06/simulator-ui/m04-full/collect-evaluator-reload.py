from pathlib import Path
import os,subprocess,json,base64,runpy
root=Path('/private/tmp/novalist-m04-full');repo=Path('<repository>');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
response=json.loads((data/'NovalistAudit/response.json').read_text())
before=json.loads(base64.b64decode(response['result']).decode())
(root/'evaluator-immediately-before-reload.json').write_text(json.dumps({'observed':before,'probeResponseUtc':response['finished']},indent=2)+'\n')
os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
n=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));call,js=n['call'],n['evaluate']
after=js("({journalContainsMarker:(localStorage.getItem('novalist.mobile.recovery.v1')||'').includes('M04EVAL26A'),markerInEditor:Array.from(document.querySelectorAll('iframe')).some(f=>f.contentDocument?.body.innerText.includes('M04EVAL26A'))})")
state=call(sim,'state')['result']
after['markerOnDisk']=any('M04EVAL26A' in q.read_text() for q in data.rglob('*.novalist') if q.is_file())
record={'case_status':'failed','immediatelyBeforeActualReload':before,'afterActualReload':after,'newNativePageHealthy':state['bridgeFailed']==0 and state['rendererReady'],'harness_notes':'XCTest completed with marker assertion failure in11.902s. Parent Python55s wait expired during xcodebuild post-failure result collection; recovered preserved pre-Reload response and separately collected post-Reload observations.'}
(root/'evaluator-reload-result.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps(record))
