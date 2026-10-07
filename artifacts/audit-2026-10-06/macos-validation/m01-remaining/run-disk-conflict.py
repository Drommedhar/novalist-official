from pathlib import Path
import os,runpy,subprocess,time,json,hashlib,shutil,re
repo=Path('<repository>');work=Path('<audit-scratch>');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
probe=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));call,js=probe['call'],probe['evaluate']
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
record={'source':'source.json','scenario':'M01-09 real external disk modification and unresolved conflict through Home/termination/relaunch','simulator':sim};scene_file=None
def save():(work/'disk-conflict.json').write_text(json.dumps(record,indent=2)+'\n')
def disk():
 if scene_file is None:return None
 raw=scene_file.read_bytes();return {'relative_path':str(scene_file.relative_to(data)),'sha256':hashlib.sha256(raw).hexdigest(),'local_marker':b'M01LOCAL26' in raw,'external_marker':b'M01EXTERNAL26' in raw}
def observe():
 expression='''(()=>{const s=window.novalistStores.project.getState(),find=h=>({local:(h||'').includes('M01LOCAL26'),external:(h||'').includes('M01EXTERNAL26')});return{
  scope:{projectPath:s.projectPath,bookId:s.activeBookId,draftId:s.activeDraftId},
  editors:Object.entries(s.editors).map(([paneId,e])=>({paneId,isDirty:e.isDirty,sceneId:e.sceneId,chapterGuid:e.chapterGuid,hash:e.hash,...find(e.html)})),
  conflict:s.sceneConflict?{sceneId:s.sceneConflict.sceneId,chapterGuid:s.sceneConflict.chapterGuid,mine:find(s.sceneConflict.mine),theirs:find(s.sceneConflict.theirs)}:null,
  journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>find(e.html).local).map(e=>({scope:e.scope,source:e.source,sceneId:e.sceneId,chapterGuid:e.chapterGuid,hash:e.hash,...find(e.html)})),
  gate:window.auditHold?{writes:auditHold.writes.map(w=>({id:w.id,sceneId:w.params[1],chapterGuid:w.params[0],...find(w.params[2])})),released:auditHold.released,replies:auditHold.replies,forwardedReturns:auditHold.forwardedReturns}:null
 };})()'''
 return {**js(expression),'disk':disk()}
assert not any('M01LOCAL26' in p.read_text() or 'M01EXTERNAL26' in p.read_text() for p in data.rglob('*.novalist') if p.is_file())
shutil.copy2(work/'disk-conflict.swift',work/'harness/AuditUITests.swift');log=work/'disk-conflict.log'
with log.open('w') as out:
 p=subprocess.Popen(['xcodebuild','test','-project',str(work/'harness/AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sim,'-derivedDataPath',str(work/'harness/DerivedData'),'-parallel-testing-enabled','NO','-collect-test-diagnostics','never','-resultBundlePath',str(work/'disk-conflict.xcresult'),'CODE_SIGNING_ALLOWED=NO'],stdout=out,stderr=subprocess.STDOUT)
 def waitlog(marker,timeout=60):
  end=time.monotonic()+timeout
  while time.monotonic()<end:
   text=log.read_text()
   if marker in text:return
   if p.poll() is not None or "Test Suite 'All tests' failed" in text:raise RuntimeError('XCTest exited before '+marker)
   time.sleep(.03)
  raise TimeoutError(marker)
 try:
  waitlog('M01_CONFLICT_READY_GATE');assert js((work/'hold-scene-writes.js').read_text())
  waitlog('M01_CONFLICT_LOCAL_TYPED');end=time.monotonic()+5
  while time.monotonic()<end:
   before=observe()
   if len(before['gate']['writes'])==1:break
   time.sleep(.08)
  assert len(before['gate']['writes'])==1 and len(before['journal'])==1
  scene_id=before['gate']['writes'][0]['sceneId']
  matches=[q for q in data.rglob('*.novalist') if q.is_file() and f'id={scene_id} ' in q.read_text()]
  assert len(matches)==1;scene_file=matches[0]
  record['before_external_change']=observe();save()
  original=scene_file.read_text();assert 'M01LOCAL26' not in original
  new=original.split('\n',1)[0]+'\n<p>M01EXTERNAL26</p>'
  temporary=scene_file.with_suffix('.m01tmp');temporary.write_text(new);temporary.replace(scene_file)
  record['external_disk_before_release']=disk();save()
  assert js('(window.auditHold.release(0),true)')
  waitlog('M01_CONFLICT_VISIBLE');record['conflict_before_home']=observe();save()
  conflict=record['conflict_before_home'];assert conflict['conflict']['sceneId']==scene_id
  assert conflict['conflict']['mine']['local'] and conflict['conflict']['theirs']['external']
  assert conflict['disk']==record['external_disk_before_release'] and conflict['journal'][0]['local']
  assert len(conflict['gate']['replies'])==1 and conflict['gate']['replies'][0]['conflicted'] and not conflict['gate']['replies'][0]['hasError']
  assert all(v=='accepted' for v in conflict['gate']['forwardedReturns'])
  waitlog('M01_CONFLICT_TERMINATED');record['disk_after_termination']=disk();save()
  assert record['disk_after_termination']==record['external_disk_before_release']
  waitlog('M01_CONFLICT_RESTORED_PASS');record['after_relaunch']=observe();save()
  restored=record['after_relaunch'];assert restored['disk']==record['external_disk_before_release']
  assert restored['conflict']['sceneId']==scene_id and restored['conflict']['mine']['local'] and restored['conflict']['theirs']['external']
  assert len(restored['journal'])==1 and restored['journal'][0]['scope']==before['scope'] and restored['journal'][0]['sceneId']==scene_id
  record['xcodebuild_exit_code']=p.wait(timeout=60);assert p.returncode==0
  record['status']='passed-simulator-supporting-acceptance'
 except Exception as error:
  record['status']='failed';record['error']=type(error).__name__+': '+str(error)
  try:record['failure_observation']=observe()
  except Exception as other:record['probe_error']=type(other).__name__
  save();raise
save();print(record['status'])
