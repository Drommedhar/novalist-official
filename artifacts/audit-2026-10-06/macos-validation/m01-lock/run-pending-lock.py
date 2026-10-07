from pathlib import Path
import os,runpy,subprocess,time,json,hashlib,shutil
w=Path(__file__).resolve().parent;sid='BE6B8D09-298D-4C2A-AA28-CF5946F951B3';os.environ['NOVALIST_AUDIT_SIMULATOR']=sid
p=runpy.run_path(str(Path(os.environ['NOVALIST_REPO'])/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));js,call=p['evaluate'],p['call'];marker='M01REALLOCKPENDING26'
data=Path((w/'app-data-path-probe.txt').read_text().strip())/'Documents';record={'scenario':'Real DeviceHub lock with exact-scene write held, termination and recovery','simulator':sid,'marker':marker,'source':'probe-source.json','status':'running'}
def save():(w/'pending-lock.json').write_text(json.dumps(record,indent=2)+'\n')
def disk():
 found=[]
 for f in data.rglob('*.novalist'):
  if not f.is_file():continue
  raw=f.read_bytes()
  if marker.encode() in raw:found.append({'path':str(f.relative_to(data)),'sha256':hashlib.sha256(raw).hexdigest()})
 return found
def observe():
 return js('''(() => {const p=novalistStores.project.getState(),pane=p.activeEditorPaneId,e=p.editors[pane],marker='M01REALLOCKPENDING26';return {scope:{projectPath:p.projectPath,bookId:p.activeBookId,draftId:p.activeDraftId},pane,editor:e?{sceneId:e.sceneId,chapterGuid:e.chapterGuid,isDirty:e.isDirty,marker:(e.html||'').includes(marker),hash:e.hash}:null,dirtyMapValue:e?!!p.dirtyMap[e.sceneId]:null,journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>(e.html||'').includes(marker)).map(e=>({scope:e.scope,sceneId:e.sceneId,chapterGuid:e.chapterGuid,source:e.source,hash:e.hash,marker:true})),gate:window.auditLock||null}})()''')
def lockstate():return int(subprocess.check_output(['xcrun','simctl','spawn',sid,'notifyutil','-g','com.apple.springboard.lockstate'],text=True).strip().split()[-1])
assert disk()==[]
shutil.copyfile(w/'pending-edit.swift',w/'harness/AuditUITests.swift');log=w/'pending-edit.log'
try:
 with log.open('w') as stream:
  process=subprocess.Popen(['xcodebuild','test','-project',str(w/'harness/AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sid,'-derivedDataPath',str(w/'harness/DerivedData'),'-parallel-testing-enabled','NO','-collect-test-diagnostics','never','-resultBundlePath',str(w/'pending-edit.xcresult'),'CODE_SIGNING_ALLOWED=NO'],stdout=stream,stderr=subprocess.STDOUT)
  deadline=time.monotonic()+90
  while 'M01_PENDING_GATE_READY' not in log.read_text():
   if process.poll() is not None:raise RuntimeError('Native test failed before gate: '+str(process.returncode))
   if time.monotonic()>deadline:raise TimeoutError('Gate readiness')
   time.sleep(.05)
  record['gate_installed']=js((w/'hold-scene-write.js').read_text());record['gate_installed_at']=time.time();save()
  while 'M01_PENDING_EDIT_DONE' not in log.read_text():
   if process.poll() is not None:raise RuntimeError('Native test failed before edit completed')
   time.sleep(.05)
  record['edit_observed_at']=time.time();record['after_edit']=observe();save()
  record['native_edit_test_exit']=process.wait(timeout=40);record['native_session_completed_at']=time.time();assert record['native_edit_test_exit']==0
  deadline=time.monotonic()+8
  while time.monotonic()<deadline:
   o=observe()
   if o['gate']['held']:break
   time.sleep(.08)
  record['before_lock']=o;record['disk_before_lock']=disk();record['native_before_lock']=call(sid,'state')['result'];record['lockstate_before']=lockstate();save()
  assert o['editor']['isDirty'] and o['dirtyMapValue'] and o['editor']['marker'] and len(o['journal'])==1
  assert o['gate']['held'] and all(e['marker'] for e in o['gate']['held']) and not record['disk_before_lock']
  assert record['lockstate_before']==0
  record['lock_action_started_at']=time.time();record['lock_action_output']=subprocess.check_output(['python3',str(w/'devicehub-controls.py'),'lock'],text=True);record['lock_action_completed_at']=time.time()
  deadline=time.monotonic()+8;record['lockstate_samples']=[]
  while time.monotonic()<deadline:
   state=lockstate();record['lockstate_samples'].append({'time':time.time(),'value':state})
   if state==1:break
   time.sleep(.03)
  assert state==1,'Real Lock did not produce locked state'
  record['native_while_locked']=call(sid,'state')['result'];record['locked_observation']=observe();record['disk_while_locked']=disk();save()
  before=record['native_before_lock']['events'];fresh=record['native_while_locked']['events'][len(before):];record['new_native_events']=fresh
  assert any(e['kind']=='background-task' and e['detail']=='granted' for e in fresh)
  assert not any(e['kind']=='background-acknowledgement' for e in fresh)
  assert record['locked_observation']['editor']['isDirty'] and record['locked_observation']['journal'] and not record['disk_while_locked']
  subprocess.run(['xcrun','simctl','io',sid,'screenshot',str(w/'pending-locked-screen.png')],check=True,capture_output=True)
  record['termination_started_at']=time.time();subprocess.run(['xcrun','simctl','terminate',sid,'com.novalist.app'],check=True);record['terminated_at']=time.time();record['disk_after_termination']=disk();assert not record['disk_after_termination']
  record['status']='locked-pending-edit-terminated-awaiting-recovery';save()
except Exception as error:
 record['status']='failed';record['error']=type(error).__name__+': '+str(error);save();raise
print(json.dumps({k:record[k] for k in ['status','disk_before_lock','lockstate_samples','new_native_events','disk_after_termination']},indent=2))
