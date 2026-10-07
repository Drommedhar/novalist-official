from pathlib import Path
import json,hashlib,time,subprocess,runpy,datetime,re
from control import w,sid,js,async_call,canonical,data_path
owner=json.loads((w/'owner.json').read_text())
def config(kind):return {**owner,'kind':kind,'marker':'M01MSCOPE28' if kind=='manuscript' else 'M01RSCOPE28'}
def scope_for(path,book,draft):return {'projectPath':path,'bookId':book,'draftId':draft}
def observe(kind):
 c=config(kind)
 o=js('''(()=>{const c=CONFIG,p=novalistStores.project.getState(),m=novalistStores.manuscript.getState(),f=document.querySelector('iframe[title="manuscript"]'),scene=f?.contentDocument.querySelector('[data-scene-id="'+c.sceneId+'"]'),research=document.querySelector('[contenteditable="true"][aria-label="Content"]');return {scope:{projectPath:p.projectPath,bookId:p.activeBookId,draftId:p.activeDraftId},view:novalistStores.shell.getState().mainView,workspaceBusy:p.workspaceBusy,gate:window.auditLockVariant||null,manuscript:{present:!!scene,sceneId:scene?.dataset.sceneId,domMarker:!!scene?.textContent.includes(c.marker),storeMatches:m.sections.flatMap(s=>s.scenes).filter(s=>s.sceneId===c.sceneId).map(s=>({sceneId:s.sceneId,marker:s.html.includes(c.marker)}))},research:{present:!!research,domMarker:!!research?.textContent.includes(c.marker)},journal:JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]').filter(e=>(e.html||e.draft?.content||'').includes(c.marker))}})()'''.replace('CONFIG',json.dumps(c)))
 o['journal_hash']=canonical(o['journal']);o['disk']=disk(kind);o['observed_at']=datetime.datetime.now(datetime.timezone.utc).isoformat();return o
def disk(kind):
 c=config(kind);rows=[]
 for directory in [Path(c['path']),Path(c['copyPath'])]:
  if kind=='manuscript':
   for f in sorted(directory.rglob('*.novalist')):
    if not f.is_file() or 'Chapters' not in f.parts:continue
    raw=f.read_text()
    if c['sceneId'] not in raw:continue
    rows.append({'path':str(f.relative_to(data_path())),'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'marker_count':raw.count(c['marker'])})
  else:
   metadata=json.loads((directory/'.novalist/project.json').read_text());items=metadata['researchItems'];item=next(e for e in items if e['id']==c['researchId']);payload={k:item.get(k,[]) for k in ['id','title','type','content','tags','entityRefs']}
   rows.append({'path':str((directory/'.novalist/project.json').relative_to(data_path())),'projectId':metadata['id'],'researchId':item['id'],'item_count':len(items),'payload_sha256':canonical(payload),'marker_count':item['content'].count(c['marker']),'payload':payload})
 return rows
def show(kind):
 async_call("novalistStores.shell.getState().setMainView('dashboard'); return true")
 if kind=='manuscript':async_call("novalistStores.shell.getState().setMainView('manuscript'); await novalistStores.manuscript.getState().load(); return true")
 else:async_call('novalistStores.shell.getState().navigateToResearch('+json.dumps(owner['researchId'])+'); return true')
 end=time.monotonic()+10
 while time.monotonic()<end:
  o=observe(kind)
  if o[kind]['present'] and o['view']==kind:return o
  time.sleep(.1)
 raise TimeoutError('Actual target editor did not render')
def nav(expression):return async_call('await '+expression+'; return true')
def native(name):
 r=subprocess.run(['python3',str(w/'run-native.py'),name],capture_output=True,text=True)
 if r.returncode:raise RuntimeError('Native harness failed: '+name+' '+r.stdout+r.stderr)
 return {'name':name,'exit_code':r.returncode,'log_sha256':hashlib.sha256((w/(name+'.log')).read_bytes()).hexdigest()}
