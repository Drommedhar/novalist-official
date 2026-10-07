from pathlib import Path
import os,sys,json,hashlib,subprocess,runpy,datetime
w=Path(__file__).resolve().parent;kind=sys.argv[1];sid='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B';os.environ['NOVALIST_AUDIT_SIMULATOR']=sid
repo=Path(os.environ.get('NOVALIST_REPO',str(Path.cwd())));js=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'))['evaluate']
p=w/(kind+'-lock.json');r=json.loads(p.read_text());c=r['config'];marker=c['marker'];d=Path((w/'app-data-probe.txt').read_text().strip())/'Documents';project=d/'Audit M01 Lock Variants'
o=js('''(() => {const c=CONFIG,p=novalistStores.project.getState(),m=novalistStores.manuscript.getState();return {scope:{projectPath:p.projectPath,bookId:p.activeBookId,draftId:p.activeDraftId},view:novalistStores.shell.getState().mainView,journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>(e.html||e.draft?.content||'').includes(c.marker)).length,gatePresent:!!window.auditLockVariant,editors:Object.values(p.editors).map(e=>({sceneId:e.sceneId,chapterGuid:e.chapterGuid,isDirty:e.isDirty,marker:(e.html||'').includes(c.marker)})),manuscript:m.sections.flatMap(s=>s.scenes.map(e=>({chapterGuid:s.chapterGuid,sceneId:e.sceneId,marker:e.html.includes(c.marker)}))),researchDomMarker:!!document.querySelector('[contenteditable="true"][aria-label="Content"]')?.textContent.includes(c.marker)}})()'''.replace('CONFIG',json.dumps(c)))
assert o['scope']==r['gate_installed']['scope'] and o['journal']==0 and not o['gatePresent']
meta=json.loads((project/'.novalist/project.json').read_text());assert meta['id']=='project-1791378307548' and meta['activeBookId']==c['bookId'];book=next(b for b in meta['books'] if b['id']==c['bookId']);assert book['activeDraftId']==c['draftId'];draft=next(e for e in book['drafts'] if e['id']==c['draftId'])
owner={'projectId':meta['id'],'projectFolder':project.name,'bookId':book['id'],'bookFolder':book['folderName'],'draftId':draft['id'],'draftFolder':draft['folderName']}
if kind=='manuscript':
 matches=[f for f in d.rglob('*.novalist') if marker.encode() in f.read_bytes()];assert len(matches)==1;f=matches[0];raw=f.read_text();assert raw.count(marker)==1 and c['sceneId'] in raw
 assert any(e['sceneId']==c['sceneId'] and e['chapterGuid']==c['chapterGuid'] and e['marker'] for e in o['manuscript'])
 index=project/book['folderName']/'Drafts'/draft['folderName']/'scenes.json';indexraw=index.read_text();assert c['sceneId'] in indexraw and c['chapterGuid'] in indexraw
 owner.update({'sceneId':c['sceneId'],'chapterGuid':c['chapterGuid'],'sceneIndexSha256':hashlib.sha256(index.read_bytes()).hexdigest()});disk={'path':str(f.relative_to(d)),'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'markerCount':1,'matchingLiveSceneFiles':1}
else:
 items=meta['researchItems'];matches=[e for e in items if marker in e.get('content','')];assert len(matches)==1 and matches[0]['id']==c['researchId'] and matches[0]['content'].count(marker)==1 and o['researchDomMarker'];item=matches[0]
 assert item['title']==json.loads((w/'fixture-before.json').read_text())['research'][0]['title']
 owner.update({'researchId':item['id'],'researchItemCount':len(items),'researchStoredAtProjectLevel':True});disk={'path':'Audit M01 Lock Variants/.novalist/project.json','sha256':hashlib.sha256((project/'.novalist/project.json').read_bytes()).hexdigest(),'markerCountInLiveItem':1,'matchingResearchItems':1}
r['recovery']={'at':datetime.datetime.now(datetime.timezone.utc).isoformat(),'observation':o,'owner':owner,'disk':disk,'native_test_passed':True}
r['status']='passed-simulator-supporting-lock-recovery';p.write_text(json.dumps(r,indent=2)+'\n')
subprocess.run(['xcrun','simctl','io',sid,'screenshot',str(w/(kind+'-recovered.png'))],check=True,capture_output=True)
print(kind,'recovered exact owner; journal empty and runtime gate absent',json.dumps(disk))
