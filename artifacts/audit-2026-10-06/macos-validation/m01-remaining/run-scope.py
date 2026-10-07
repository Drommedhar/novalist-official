from pathlib import Path
import os,runpy,subprocess,time,json,hashlib,shutil,re,uuid
repo=Path('<repository>');work=Path('<audit-scratch>');sim='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B'
os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
probe=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));call,js=probe['call'],probe['evaluate']
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
marker='M01SCOPE26';setup=js('window.auditScopeSetup');assert setup['status']=='ready' and setup['otherBookId']!=setup['originalBookId']
record={'source':'source.json','scenario':'M01-11 recovery remains confined to original project/book/draft','marker':marker,'simulator':sim,'setup':setup}
def save():(work/'scope.json').write_text(json.dumps(record,indent=2)+'\n')
def disk():
 rows=[]
 for p in data.rglob('*.novalist'):
  if p.is_file() and marker in p.read_text():rows.append({'relative_path':str(p.relative_to(data)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'sceneId':re.search(r'<!--nv v=1 id=([^ ]+)',p.read_text()).group(1)})
 return rows
def observe():
 return {**js('''(()=>{const m='M01SCOPE26',s=window.novalistStores.project.getState();return {scope:{projectPath:s.projectPath,bookId:s.activeBookId,draftId:s.activeDraftId},editors:Object.entries(s.editors).map(([paneId,e])=>({paneId,sceneId:e.sceneId,chapterGuid:e.chapterGuid,isDirty:e.isDirty,dirtyMap:s.dirtyMap[e.sceneId],hasMarker:(e.html||'').includes(m)})),journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>(e.html||'').includes(m)).map(e=>({scope:e.scope,source:e.source,sceneId:e.sceneId,chapterGuid:e.chapterGuid,hash:e.hash})),gate:window.auditHold?{writes:auditHold.writes.map(w=>({id:w.id,chapterGuid:w.params[0],sceneId:w.params[1],hasMarker:w.params[2].includes(m)})),forwardedReturns:auditHold.forwardedReturns}:null};})()'''),'disk':disk()}
def nav(expression):
 js("(()=>{window.auditScopeNav={status:'running'};(async()=>{"+expression+";window.auditScopeNav={status:'ready'}})().catch(e=>window.auditScopeNav={status:'error',error:String(e)});return true})()")
 deadline=time.monotonic()+10
 while time.monotonic()<deadline:
  state=js('window.auditScopeNav')
  if state['status']=='ready':time.sleep(.6);return observe()
  if state['status']=='error':raise RuntimeError(state['error'])
  time.sleep(.1)
 raise TimeoutError('Scope navigation')
assert not disk();save();shutil.copy2(work/'scope.swift',work/'ipad-harness/AuditUITests.swift');log=work/'scope.log'
with log.open('w') as out:
 p=subprocess.Popen(['xcodebuild','test','-project',str(work/'ipad-harness/AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sim,'-derivedDataPath',str(work/'ipad-harness/DerivedData'),'-parallel-testing-enabled','NO','-collect-test-diagnostics','never','-resultBundlePath',str(work/'scope.xcresult'),'CODE_SIGNING_ALLOWED=NO'],stdout=out,stderr=subprocess.STDOUT)
 def waitlog(marker,timeout=60):
  end=time.monotonic()+timeout
  while time.monotonic()<end:
   text=log.read_text()
   if marker in text:return
   if p.poll() is not None or "Test Suite 'All tests' failed" in text:raise RuntimeError('XCTest exited before '+marker)
   time.sleep(.03)
  raise TimeoutError(marker)
 try:
  waitlog('M01_SCOPE_READY_GATE');assert js((work/'hold-scene-writes.js').read_text())
  waitlog('M01_SCOPE_TYPED');end=time.monotonic()+3
  while time.monotonic()<end:
   snapshot=observe()
   if snapshot['gate']['writes']:break
   time.sleep(.06)
  record['original_dirty_scope']=snapshot;save();assert len(snapshot['journal'])==len(snapshot['gate']['writes'])==1 and not snapshot['disk']
  assert snapshot['journal'][0]['scope']==snapshot['scope'];owner=snapshot['journal'][0]
  assert any(e['sceneId']==owner['sceneId'] and e['isDirty'] and e['dirtyMap'] and e['hasMarker'] for e in snapshot['editors'])
  waitlog('M01_SCOPE_TERMINATED');assert not disk()
  project=Path(setup['projectPath']);meta=project/'.novalist/project.json';metadata=json.loads(meta.read_text());original=next(b for b in metadata['books'] if b['id']==setup['originalBookId'])
  default=next(d for d in original['drafts'] if d['id']==setup['originalDraftId']);alt=next(d for d in original['drafts'] if d['id']==setup['alternateDraft']['id'])
  book=project/original['folderName'];shutil.copytree(book/'Drafts'/default['folderName'],book/'Drafts'/alt['folderName'],dirs_exist_ok=True)
  copy=data/'Audit M01 Scope Copy';assert not copy.exists();shutil.copytree(project,copy)
  copied=json.loads((copy/'.novalist/project.json').read_text());copied['id']=str(uuid.uuid4());copied['name']='Audit M01 Scope Copy';copied['activeBookId']=setup['originalBookId']
  (copy/'.novalist/project.json').write_text(json.dumps(copied,indent=2)+'\n')
  record['fixture_configuration']={'originalProjectId':metadata['id'],'copyProjectId':copied['id'],'sameBookSceneDraftIdsInCopy':True,'copyRelativePath':str(copy.relative_to(data)),'alternateDraftIncludesEmptySameScene':True}
  metadata['activeBookId']=setup['otherBookId'];original['activeDraftId']=setup['alternateDraft']['id'];tmp=meta.with_suffix('.audit-tmp');tmp.write_text(json.dumps(metadata,indent=2)+'\n');tmp.replace(meta);save()
  waitlog('M01_SCOPE_OTHER_BOOK_OPEN');time.sleep(.5);other=observe();record['other_book']=other;save()
  assert other['scope']['bookId']==setup['otherBookId'] and len(other['journal'])==1 and not other['disk'] and not any(e['hasMarker'] for e in other['editors'])
  wrongproject=nav('await window.novalistStores.project.getState().openProject('+json.dumps(str(copy))+','+json.dumps(setup['originalBookId'])+')');record['other_project_same_book_and_draft']=wrongproject;save()
  assert wrongproject['scope']=={'projectPath':str(copy),'bookId':setup['originalBookId'],'draftId':setup['originalDraftId']} and len(wrongproject['journal'])==1 and not wrongproject['disk'] and not any(e['hasMarker'] for e in wrongproject['editors'])
  wrongdraft=nav('await window.novalistStores.project.getState().openProject('+json.dumps(str(project))+','+json.dumps(setup['originalBookId'])+')');record['other_draft_same_project_and_book']=wrongdraft;save()
  assert wrongdraft['scope']=={'projectPath':str(project),'bookId':setup['originalBookId'],'draftId':setup['alternateDraft']['id']} and len(wrongdraft['journal'])==1 and not wrongdraft['disk'] and not any(e['hasMarker'] for e in wrongdraft['editors'])
  correct=nav('await window.novalistStores.project.getState().switchDraft('+json.dumps(setup['originalDraftId'])+')');end=time.monotonic()+8
  while time.monotonic()<end and (correct['journal'] or not correct['disk']):time.sleep(.2);correct=observe()
  record['returned_to_exact_owner']=correct;save();assert correct['scope']==snapshot['scope'] and not correct['journal'] and len(correct['disk'])==1 and correct['disk'][0]['sceneId']==owner['sceneId']
  assert 'Audit Tablet Fixture/Audit Tablet Book/Drafts/default/' in correct['disk'][0]['relative_path']
  waitlog('M01_SCOPE_RETURNED_EXACT_BUFFER_PASS');record['xcodebuild_exit_code']=p.wait(timeout=60);assert p.returncode==0;record['status']='passed-simulator-supporting-acceptance'
 except Exception as error:
  record['status']='failed';record['error']=type(error).__name__+': '+str(error)
  try:record['failure_observation']=observe()
  except Exception as other:record['probe_error']=type(other).__name__
  save();raise
save();print(record['status'])
