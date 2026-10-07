from pathlib import Path
import os,sys,runpy,subprocess,time,json,hashlib,shutil,datetime
w=Path(__file__).resolve().parent;kind=sys.argv[1];sid='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B';os.environ['NOVALIST_AUDIT_SIMULATOR']=sid
repo=Path(os.environ.get('NOVALIST_REPO',str(Path.cwd())));p=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));js,call=p['evaluate'],p['call']
config={'kind':kind,'marker':'M01MANUSCRIPTLOCK27' if kind=='manuscript' else 'M01RESEARCHLOCK27','bookId':'book-1791378307548','draftId':'draft-default','chapterGuid':'4358bb7d-a881-444b-9008-1ae99a730cf0','sceneId':'8ea60cae-9bf4-4ba7-b57f-00e8831be1ee','researchId':'46a70571-a29b-42b9-80e2-b13f9a8c35dc'};marker=config['marker']
data=Path((w/'app-data-probe.txt').read_text().strip())/'Documents';record={'scenario':kind+' actual Lock, held real write, termination and recovery','simulator':sid,'config':config,'source_revision':'af744fbdb0e3d46a9d37be47243f6a7cb75fc9cd','status':'running'}
def now():return datetime.datetime.now(datetime.timezone.utc).isoformat()
def save():(w/(kind+'-lock.json')).write_text(json.dumps(record,indent=2)+'\n')
def disk():
 found=[]
 for f in data.rglob('*'):
  if not f.is_file() or f.suffix not in ['.novalist','.json']:continue
  if 'NovalistAudit' in f.parts:continue
  raw=f.read_bytes()
  if marker.encode() in raw:found.append({'path':str(f.relative_to(data)),'sha256':hashlib.sha256(raw).hexdigest(),'count':raw.count(marker.encode())})
 return found
def observe():
 return js('''(() => {const c=CONFIG,p=novalistStores.project.getState(),m=novalistStores.manuscript.getState(),scene=m.sections.flatMap(e=>e.scenes).find(e=>e.sceneId===c.sceneId),frame=document.querySelector('iframe[title="manuscript"]'),view=frame?.contentDocument.querySelector('[data-scene-id="'+c.sceneId+'"]'),research=document.querySelector('[contenteditable="true"][aria-label="Content"]');return {scope:{projectPath:p.projectPath,bookId:p.activeBookId,draftId:p.activeDraftId},view:novalistStores.shell.getState().mainView,manuscript:{mode:m.mode,sceneId:scene?.sceneId,marker:!!scene?.html.includes(c.marker),domMarker:!!view?.textContent.includes(c.marker)},research:{domMarker:!!research?.textContent.includes(c.marker)},journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>(e.html||e.draft?.content||'').includes(c.marker)).map(e=>({scope:e.scope,kind:e.kind,source:e.source,sceneId:e.sceneId,chapterGuid:e.chapterGuid,researchId:e.draft?.id,hash:e.hash,marker:true,baseContainsMarker:(e.base?.content||'').includes(c.marker)})),gate:window.auditLockVariant||null}})()'''.replace('CONFIG',json.dumps(config)))
def lockstate():return int(subprocess.check_output(['xcrun','simctl','spawn',sid,'notifyutil','-g','com.apple.springboard.lockstate'],text=True).strip().split()[-1])
assert not disk(),'Marker already present before run';record['baseline']=observe();record['baseline_disk']=disk();save()
name=kind+'-edit';shutil.copyfile(w/(name+'.swift'),w/'harness/AuditUITests.swift');log=w/(name+'.log')
try:
 with log.open('w') as stream:
  process=subprocess.Popen(['xcodebuild','test','-project',str(w/'harness/AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sid,'-derivedDataPath',str(w/'harness/DerivedData'),'-parallel-testing-enabled','NO','-collect-test-diagnostics','never','-resultBundlePath',str(w/(name+'.xcresult')),'CODE_SIGNING_ALLOWED=NO'],stdout=stream,stderr=subprocess.STDOUT)
  deadline=time.monotonic()+90
  while 'M01_VARIANT_GATE_READY' not in log.read_text():
   if process.poll() is not None:raise RuntimeError('Native test failed before gate: '+str(process.returncode))
   if time.monotonic()>deadline:raise TimeoutError('Gate readiness')
   time.sleep(.05)
  record['gate_installed']=js((w/'held-write-gate.js').read_text().replace('__CONFIG__',json.dumps(config)));record['gate_installed_at']=now();save()
  while 'M01_VARIANT_EDIT_DONE' not in log.read_text():
   if process.poll() is not None:raise RuntimeError('Native test failed before edit completed')
   if time.monotonic()>deadline:raise TimeoutError('Native edit completion')
   time.sleep(.05)
  record['edit_observed_at']=now();record['after_edit']=observe();save()
  record['native_edit_test_exit']=process.wait(timeout=40);record['native_session_completed_at']=now();assert record['native_edit_test_exit']==0
  deadline=time.monotonic()+8
  while time.monotonic()<deadline:
   o=observe()
   if o['gate']['held']:break
   time.sleep(.04)
  record['before_lock']=o;record['disk_before_lock']=disk();record['native_before_lock']=call(sid,'state')['result'];record['lockstate_before']=lockstate();save()
  assert len(o['journal'])==1 and o['journal'][0]['marker'] and not o['journal'][0]['baseContainsMarker']
  assert o['gate']['held'] and all(e['marker'] for e in o['gate']['held']) and not record['disk_before_lock']
  if kind=='manuscript':assert o['manuscript']['domMarker'] and o['manuscript']['marker'] and any(e['sceneId']==config['sceneId'] and e['dirty'] for e in o['gate']['editingClaims'])
  else:assert o['research']['domMarker']
  assert record['lockstate_before']==0
  record['lock_action_started_at']=now();record['lock_action_output']=subprocess.check_output(['python3',str(w/'devicehub-controls.py'),'lock'],text=True);record['lock_action_completed_at']=now()
  deadline=time.monotonic()+8;record['lockstate_samples']=[]
  while time.monotonic()<deadline:
   state=lockstate();record['lockstate_samples'].append({'at':now(),'value':state})
   if state==1:break
   time.sleep(.03)
  assert state==1,'Real Lock did not produce locked state'
  record['native_while_locked']=call(sid,'state')['result'];record['locked_observation']=observe();record['disk_while_locked']=disk();save()
  before=record['native_before_lock']['events'];fresh=record['native_while_locked']['events'][len(before):];record['new_native_events']=fresh
  assert record['native_while_locked']['applicationState']=='Background'
  assert any(e['kind']=='background-task' and e['detail']=='granted' for e in fresh)
  assert not any(e['kind']=='background-acknowledgement' for e in fresh)
  assert record['locked_observation']['journal'] and not record['disk_while_locked']
  subprocess.run(['xcrun','simctl','io',sid,'screenshot',str(w/(kind+'-locked.png'))],check=True,capture_output=True)
  record['termination_started_at']=now();subprocess.run(['xcrun','simctl','terminate',sid,'com.novalist.app'],check=True);record['terminated_at']=now();record['disk_after_termination']=disk();assert not record['disk_after_termination']
  record['status']='locked-pending-edit-terminated-awaiting-recovery';save()
except Exception as error:
 record['status']='failed';record['error']=type(error).__name__+': '+str(error);save();raise
print(json.dumps({k:record[k] for k in ['status','disk_before_lock','lockstate_samples','new_native_events','disk_after_termination']},indent=2))
