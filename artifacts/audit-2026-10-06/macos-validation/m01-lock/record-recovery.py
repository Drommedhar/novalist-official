from pathlib import Path
import os,runpy,json,hashlib,re
w=Path(__file__).resolve().parent;sid='BE6B8D09-298D-4C2A-AA28-CF5946F951B3';os.environ['NOVALIST_AUDIT_SIMULATOR']=sid
js=runpy.run_path(str(Path(os.environ['NOVALIST_REPO'])/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'))['evaluate'];d=json.loads((w/'pending-lock.json').read_text());base=Path((w/'app-data-path-probe.txt').read_text().strip())/'Documents'
o=js('''(() => {const p=novalistStores.project.getState(),pane=p.activeEditorPaneId,e=p.editors[pane];return {scope:{projectPath:p.projectPath,bookId:p.activeBookId,draftId:p.activeDraftId},pane,sceneId:e.sceneId,chapterGuid:e.chapterGuid,isDirty:e.isDirty,dirtyMapValue:!!p.dirtyMap[e.sceneId],marker:e.html.includes('M01REALLOCKPENDING26'),journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>(e.html||'').includes('M01REALLOCKPENDING26')).length,gatePresent:!!window.auditLock}})()''')
assert o['marker'] and not o['isDirty'] and not o['dirtyMapValue'] and o['journal']==0 and not o['gatePresent'];assert o['scope']==d['gate_installed']['scope'];assert o['sceneId']==d['gate_installed']['sceneId'];assert o['chapterGuid']==d['gate_installed']['chapterGuid']
files=[p for p in base.rglob('*.novalist') if p.is_file() and b'M01REALLOCKPENDING26' in p.read_bytes()];assert len(files)==1;scene=files[0];raw=scene.read_bytes();assert ('id='+o['sceneId']).encode() in raw
project=Path(o['scope']['projectPath']);projectfile=project/'.novalist/project.json';meta=json.loads(projectfile.read_text());book=next(b for b in meta['books'] if b['id']==o['scope']['bookId']);draft=next(v for v in book['drafts'] if v['id']==o['scope']['draftId']);draftdir=project/book['folderName']/'Drafts'/draft['folderName'];scenes=draftdir/'scenes.json';smeta=json.loads(scenes.read_text());matches=[]
def scan(v):
 if isinstance(v,dict):
  if v.get('id')==o['sceneId']:matches.append(v)
  for x in v.values():scan(x)
 elif isinstance(v,list):
  for x in v:scan(x)
scan(smeta);assert len(matches)==1 and matches[0]['chapterGuid']==o['chapterGuid'] and matches[0]['fileName']==scene.name
hashes=lambda p:{str(f.relative_to(p)):hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted(p.rglob('*')) if f.is_file()}
cart=[p.parent.parent for p in base.rglob('.novalist/project.json') if p.parent.parent.name=="The Cartographer's Daughter"];assert len(cart)==1;assert hashes(cart[0])==json.loads((w/'fixture-before.json').read_text())
d.update({'after_recovery':o,'disk_after_recovery':{'path':str(scene.relative_to(base)),'sha256':hashlib.sha256(raw).hexdigest(),'header_scene_id_matches':True,'marker_count':raw.count(b'M01REALLOCKPENDING26')},'exact_owner':{'projectId':meta['id'],'project_relative':str(project.relative_to(base)),'bookId':book['id'],'bookFolder':book['folderName'],'draftId':draft['id'],'draftFolder':draft['folderName'],'chapterGuid':o['chapterGuid'],'sceneId':o['sceneId'],'sceneFile':scene.name,'project_metadata_sha256':hashlib.sha256(projectfile.read_bytes()).hexdigest(),'scene_index_sha256':hashlib.sha256(scenes.read_bytes()).hexdigest()},'original_fixture_files':82,'original_fixture_hashes_unchanged':True,'recovery_native_test_passed':True,'status':'passed-instrumented-real-lock-simulator-subset-awaiting-clean-repeat'})
(w/'pending-lock.json').write_text(json.dumps(d,indent=2)+'\n');(w/'audit-project-after-recovery.json').write_text(json.dumps(hashes(project),indent=2)+'\n');print(json.dumps({'status':d['status'],'exact_owner':d['exact_owner'],'after_recovery':o},indent=2))
