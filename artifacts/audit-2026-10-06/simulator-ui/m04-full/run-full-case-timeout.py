from pathlib import Path
import os,runpy,subprocess,time,json,hashlib,sys
repo=Path('<repository>');root=Path('/private/tmp/novalist-m04-full');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
case=sys.argv[1];tag=sys.argv[2];marker='M04'+tag+'26';task='Audit M04 task '+tag;scene='Audit M04 '+tag
os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
probe=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));call,js=probe['call'],probe['evaluate']
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
def disk():
 tasks=0
 for q in data.rglob('project.json'):
  obj=json.loads(q.read_text());tasks+=sum(t.get('text')==task for t in obj.get('tasks',[]))
 return {'markerOnDisk':any(marker in q.read_text() for q in data.rglob('*.novalist') if q.is_file()),'taskCountOnDisk':tasks}
def observation():
 expression="(()=>{const marker="+json.dumps(marker)+";return {markerInEditor:Array.from(document.querySelectorAll('iframe')).some(f=>f.contentDocument?.body.innerText.includes(marker)),journalContainsMarker:(localStorage.getItem('novalist.mobile.recovery.v1')||'').includes(marker),workspaceInert:!!document.querySelector('.shell[inert],.app-shell[inert]'),replay:window.auditReplay||null}})()"
 return {**js(expression),**disk()}
state=call(sim,'state')['result'];assert state['bridgeFailed']==0
assert disk()['taskCountOnDisk']==0 and not disk()['markerOnDisk']
assert js((root/'replay-gate.js').read_text().strip())
source=(root/'full-case-template.swift').read_text().replace('SCENE_TITLE',scene).replace('MARKER_VALUE',marker).replace('TASK_VALUE',task)
(root/(tag+'.swift')).write_text(source);Path('<temporary-harness>/AuditUITests.swift').write_text(source)
log=root/(tag+'.log');record={'case':case,'tag':tag,'marker':marker,'task_label':task,'scene_label':scene,'source':'proactive-journal-source.json','runtime_gate_sha256':hashlib.sha256((root/'replay-gate.js').read_bytes()).hexdigest()}
def save(): (root/(tag+'.json')).write_text(json.dumps(record,indent=2)+'\n')
with log.open('w') as out:
 p=subprocess.Popen(['xcodebuild','test','-project','<temporary-harness>/AuditHarness.xcodeproj','-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sim,'-derivedDataPath','<temporary-harness>/DerivedData','-parallel-testing-enabled','NO','-resultBundlePath',str(root/(tag+'.xcresult')),'CODE_SIGNING_ALLOWED=NO'],stdout=out,stderr=subprocess.STDOUT)
 record['xcodebuild_pid']=p.pid;save()
 def waitlog(text,seconds=60):
  end=time.monotonic()+seconds
  while time.monotonic()<end:
   content=log.read_text()
   if text in content:return
   if p.poll() is not None:raise RuntimeError('XCTest exited before '+text)
   if "Test Suite 'All tests' failed" in content:raise RuntimeError('XCTest assertion failed before '+text)
   time.sleep(.03)
  raise TimeoutError(text)
 try:
  waitlog('AUDIT_M04_ARM_NOW')
  record['beforeArm']=observation();save()
  assert record['beforeArm']['taskCountOnDisk']==1
  assert record['beforeArm']['replay']['taskRequestCount']==1
  assert record['beforeArm']['replay']['taskReplyHeld']==1
  if case.startswith('evaluator'):
   time.sleep(1.25)
   assert call(sim,case)['ok']
  else:
   waitlog('AUDIT_M04_TYPED')
   for _ in range(10):
    typed=observation()
    if typed['journalContainsMarker']:break
    time.sleep(.05)
   record['immediatelyBeforeFault']=typed;save()
   assert typed['journalContainsMarker'] and typed['markerInEditor']
   if case=='backend-failure': assert call(sim,case)['ok']
   elif case=='webcontent-termination':
    state=call(sim,'state')['result'];pid=state['webProcessId']
    assert pid and pid>1
    command=subprocess.check_output(['ps','-p',str(pid),'-o','comm='],text=True).strip()
    assert 'WebContent' in command
    record['associated_webcontent_pid']=pid;save()
    subprocess.run(['kill','-KILL',str(pid)],check=True)
  waitlog('AUDIT_M04_BEFORE_ACTUAL_RELOAD',40)
  record['nativeFailureState']=call(sim,'state')['result']
  if case!='webcontent-termination':record['beforeReload']=observation()
  else:record['beforeReloadDisk']=disk()
  save()
  if case=='evaluator-timeout':
   events=record['nativeFailureState']['events']
   armed=next(e for e in events if e['kind']=='evaluator-timeout-consumed');failed=next(e for e in events if e['kind']=='bridge-failed')
   record['evaluator_deadline_seconds']=(failed['ticks']-armed['ticks'])/1e9
   assert 14.5<=record['evaluator_deadline_seconds']<=18
  if case=='webcontent-termination':assert any(e['kind']=='content-process-did-terminate' for e in record['nativeFailureState']['events'])
  save()
  waitlog('AUDIT_M04_MARKER_AFTER_ACTUAL_RELOAD=',45)
  record['afterReload']=observation();record['healthyAfterReload']=call(sim,'state')['result']['bridgeFailed']==0;save()
  waitlog('AUDIT_M04_SINGLE_TASK_AFTER_RELOAD_PASS',15)
  record['status']='passed';record['xcodebuild_exit_code']=p.wait(timeout=45)
 except Exception as error:
  record['status']='failed';record['harness_error_type']=type(error).__name__;record['harness_error']=str(error)
  try:record['failureObservation']=observation()
  except Exception:pass
  save();print(json.dumps(record,indent=2));raise
save();print(json.dumps({key:record[key] for key in ['case','status','beforeArm','beforeReload' if case!='webcontent-termination' else 'beforeReloadDisk','afterReload','healthyAfterReload']},indent=2))
