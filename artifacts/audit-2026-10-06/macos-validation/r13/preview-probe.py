"""Read production Research DOM in a dedicated synthetic simulator; no fake nodes."""
import json, os, pathlib, runpy, subprocess, time
folder=pathlib.Path(__file__).resolve().parent
js=runpy.run_path(str(folder.parent/'native-probe.py'))['evaluate']
def pause(seconds=.5):time.sleep(seconds)
def choose(title):
    result=js("(()=>{const el=[...document.querySelectorAll('.codex-row')].find(x=>x.querySelector('.codex-row-name')?.textContent==="+json.dumps(title)+");if(!el)throw Error('Missing synthetic Research entry');el.click();return true})()")
    pause(1)
js("(()=>{[...document.querySelectorAll('button')].find(x=>x.innerText==='Skip')?.click();return true})()")
output=pathlib.Path(os.environ.get('NOVALIST_R13_RESULTS',str(folder)))
output.mkdir(parents=True,exist_ok=True)
results=[]
for typ,title in [('image','Audit R13 Image'),('pdf','Audit R13 PDF'),('audio','Audit R13 Audio'),('video','Audit R13 Video'),('oversize','Audit R13 Oversize')]:
    choose(title)
    value=js("(()=>{const e=document.querySelector('.research-editor');const m=e?.querySelector('img,object,audio,video');if(!e||!m)throw Error('No real Research preview');const r=m.getBoundingClientRect();return {title:e.querySelector('.research-title')?.value,tag:m.tagName,sourcePrefix:(m.getAttribute(m.tagName==='OBJECT'?'data':'src')||'').slice(0,65),width:m.naturalWidth||m.videoWidth||null,height:m.naturalHeight||m.videoHeight||null,readyState:m.readyState??null,duration:Number.isFinite(m.duration)?m.duration:null,mediaError:m.error?{code:m.error.code,message:m.error.message}:null,accessibleError:m.getAttribute('aria-description'),visibleError:e.querySelector('[role=alert]')?.textContent??null,relativeReference:e.querySelector('.research-meta-path')?.textContent,rect:{x:r.x,y:r.y,width:r.width,height:r.height},objectDocument:m.contentDocument?{type:m.contentDocument.contentType,text:m.contentDocument.body?.innerText?.slice(0,120)}:null}})()")
    subprocess.run(['xcrun','simctl','io',os.environ['NOVALIST_AUDIT_SIMULATOR'],'screenshot',str(output/(typ+'.png'))],check=True,capture_output=True)
    entry={'case':typ,'observed':value,'screenshot':typ+'.png'}
    if typ in ('audio','video'):
        js("(()=>{const m=document.querySelector('"+typ+"');window.auditPlayback={events:[],promise:'pending'};for(const event of ['play','playing','timeupdate','ended','error'])m.addEventListener(event,()=>window.auditPlayback.events.push({event,time:m.currentTime}));m.play().then(()=>window.auditPlayback.promise='resolved',e=>window.auditPlayback.promise=e.name);return true})()")
        pause(3)
        entry['playback']=js("({...window.auditPlayback,currentTime:document.querySelector('"+typ+"').currentTime,ended:document.querySelector('"+typ+"').ended})")
    results.append(entry)
choose('Audit R13 Image')
results.append({'case':'valid-image-after-oversize','observed':js("(()=>{const m=document.querySelector('.research-preview img');return {width:m.naturalWidth,description:m.getAttribute('aria-description'),title:m.getAttribute('title'),visibleError:document.querySelector('.research-editor [role=alert]')?.innerText??null}})()")})
(output/'preview-observations.json').write_text(json.dumps(results,indent=2)+'\n')
print(json.dumps(results,indent=2))
