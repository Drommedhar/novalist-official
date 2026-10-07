from pathlib import Path
import os,runpy,subprocess,time,json,hashlib,shutil,re
repo=Path('<repository>');work=Path('<audit-scratch>');sim='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B'
os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
probe=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));call,js=probe['call'],probe['evaluate']
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
markers=['M01LEFT26','M01RIGHT26'];record={'source':'source.json','scenario':'M01-08 two genuinely dirty native editor panes through background/termination/recovery','markers':markers,'simulator':sim}
def save():(work/'two-pane.json').write_text(json.dumps(record,indent=2)+'\n')
def disk():
 rows=[]
 for p in data.rglob('*.novalist'):
  if not p.is_file():continue
  raw=p.read_bytes();text=raw.decode();found=[m for m in markers if m in text]
  if found:rows.append({'relative_path':str(p.relative_to(data)),'sha256':hashlib.sha256(raw).hexdigest(),'sceneId':re.search(r'<!--nv v=1 id=([^ ]+)',text).group(1),'markers':found})
 return rows
def observe():
 expression='''(()=>{const markers=MARKERS,state=window.novalistStores.project.getState(),find=s=>markers.filter(m=>(s||'').includes(m));return {
  scope:{projectPath:state.projectPath,bookId:state.activeBookId,draftId:state.activeDraftId},
  editors:Object.entries(state.editors).map(([paneId,e])=>({paneId,isDirty:e.isDirty,dirtyMap:state.dirtyMap[e.sceneId],sceneId:e.sceneId,chapterGuid:e.chapterGuid,hash:e.hash,markers:find(e.html)})).filter(e=>e.markers.length),
  journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>find(e.html).length).map(e=>({scope:e.scope,source:e.source,sceneId:e.sceneId,chapterGuid:e.chapterGuid,hash:e.hash,markers:find(e.html)})),
  gate:window.auditHold?{writes:auditHold.writes.map(w=>({id:w.id,chapterGuid:w.params[0],sceneId:w.params[1],markers:find(w.params[2])})),released:auditHold.released,replies:auditHold.replies,forwardedReturns:auditHold.forwardedReturns}:null
 };})()'''.replace('MARKERS',json.dumps(markers))
 return {**js(expression),'disk':disk()}
assert not disk()
shutil.copy2(work/'two-pane.swift',work/'ipad-harness/AuditUITests.swift');log=work/'two-pane.log'
with log.open('w') as out:
 p=subprocess.Popen(['xcodebuild','test','-project',str(work/'ipad-harness/AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sim,'-derivedDataPath',str(work/'ipad-harness/DerivedData'),'-parallel-testing-enabled','NO','-collect-test-diagnostics','never','-resultBundlePath',str(work/'two-pane.xcresult'),'CODE_SIGNING_ALLOWED=NO'],stdout=out,stderr=subprocess.STDOUT)
 def waitlog(marker,timeout=60):
  end=time.monotonic()+timeout
  while time.monotonic()<end:
   text=log.read_text()
   if marker in text:return
   if p.poll() is not None or "Test Suite 'All tests' failed" in text:raise RuntimeError('XCTest exited before '+marker)
   time.sleep(.03)
  raise TimeoutError(marker)
 try:
  waitlog('M01_TWO_READY_GATE');assert js((work/'hold-scene-writes.js').read_text())
  waitlog('M01_TWO_TYPED');end=time.monotonic()+4
  while time.monotonic()<end:
   snapshot=observe()
   if len(snapshot['gate']['writes'])==2:break
   time.sleep(.08)
  record['both_dirty_before_background']=snapshot;save()
  assert len(snapshot['editors'])==len(snapshot['journal'])==len(snapshot['gate']['writes'])==2
  assert all(e['isDirty'] and e['dirtyMap'] and len(e['markers'])==1 for e in snapshot['editors'])
  assert len({e['sceneId'] for e in snapshot['editors']})==len({e['paneId'] for e in snapshot['editors']})==2
  assert {e['sceneId'] for e in snapshot['editors']}=={e['sceneId'] for e in snapshot['journal']}=={e['sceneId'] for e in snapshot['gate']['writes']}
  assert all(e['scope']==snapshot['scope'] for e in snapshot['journal']) and not snapshot['disk']
  assert all(v=='accepted' for v in snapshot['gate']['forwardedReturns'])
  before=call(sim,'state')['result'];record['native_before_home']=before;ticks=max(e['ticks'] for e in before['events']);save()
  waitlog('M01_TWO_BEFORE_HOME');end=time.monotonic()+1.2
  while time.monotonic()<end:
   native=call(sim,'state')['result'];events=[e for e in native['events'] if e['ticks']>ticks]
   if any(e['kind']=='background-task' for e in events):break
   time.sleep(.05)
  record['native_background_state']=native;save()
  assert any(e['kind']=='background-task' and e['detail']=='granted' for e in events)
  assert not any(e['kind']=='background-acknowledgement' for e in events)
  waitlog('M01_TWO_TERMINATED');record['disk_after_termination']=disk();save();assert not record['disk_after_termination']
  waitlog('M01_TWO_RESTORED_PASS');record['after_relaunch']=observe();save()
  restored=record['after_relaunch'];assert len(restored['disk'])==2 and not restored['journal']
  assert {x['sceneId'] for x in restored['disk']}=={x['sceneId'] for x in snapshot['editors']}
  assert all(len(x['markers'])==1 for x in restored['disk']) and {m for x in restored['disk'] for m in x['markers']}==set(markers)
  record['xcodebuild_exit_code']=p.wait(timeout=60);assert p.returncode==0
  record['status']='passed-simulator-supporting-acceptance'
 except Exception as error:
  record['status']='failed';record['error']=type(error).__name__+': '+str(error)
  try:record['failure_observation']=observe()
  except Exception as other:record['probe_error']=type(other).__name__
  save();raise
save();print(record['status'])
