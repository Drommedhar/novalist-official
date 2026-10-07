import hashlib
import json
import os
import pathlib
import runpy
import time

n = runpy.run_path(str(pathlib.Path(__file__).with_name('native-probe.py')))
js, call, sim = n['evaluate'], n['call'], n['simulator']
roots = json.loads((pathlib.Path(os.environ['NOVALIST_AUDIT_STATE']) / 'media-roots.json').read_text())
checks = []

def wait(expression, expected=True, timeout=15):
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        value=js(expression)
        if value==expected:return value
        time.sleep(.1)
    raise AssertionError((expression,value,expected))

def root(index):
    js('(()=>{window.novalist.setProjectRoot('+json.dumps(roots[index])+');return true})()')
    time.sleep(.2)

def image(path):
    return js("(()=>{document.querySelector('#audit-media')?.remove();const box=document.createElement('div');box.id='audit-media';box.style.cssText='position:fixed;inset:100px 10px;z-index:99999;background:white';const image=document.createElement('img');image.id='audit-image';image.src='novalist-project://project/media/"+path+"';box.append(image);document.body.append(box);window.auditImage=image;return true})()")

def held():
    for _ in range(60):
        if call(sim,'state')['result']['assetHeld']: return
        time.sleep(.1)
    raise AssertionError('Native asset reply did not reach asynchronous gate')

js("(()=>{document.querySelector('.help-close')?.click();return true})()")
root(0);image('shared.png');wait('window.auditImage.naturalWidth',2)
checks.append({'case':'native-image-decode','status':'passed','width':2,'route':'Actual projectImages MutationObserver -> native coordinated read -> data URI -> WKWebView image decode'})

root(0);call(sim,'asset-hold');image('shared.png');held()
js("(()=>{window.auditImage.src='novalist-project://project/media/second.png';return true})()")
wait('window.auditImage.naturalWidth',4)
call(sim,'asset-release');time.sleep(.3)
assert js('window.auditImage.naturalWidth')==4
checks.append({'case':'delayed-native-source-switch','status':'passed','observed':'4px new source decoded while old 2px native reply was held; releasing old reply retained the 4px source.'})

root(0);call(sim,'asset-hold')
js("(()=>{window.auditOld='pending';void window.novalistResolveProjectAsset('novalist-project://project/media/shared.png').then(x=>window.auditOld=x===null?'discarded':'returned');return true})()")
held();root(1);image('shared.png');wait('window.auditImage.naturalWidth',3)
call(sim,'asset-release');wait("window.auditOld",'discarded')
reads=call(sim,'state')['result']['assetReads']
js("(()=>{window.auditCached='pending';void window.novalistResolveProjectAsset('novalist-project://project/media/shared.png').then(uri=>{const im=new Image();im.onload=()=>window.auditCached=im.naturalWidth;im.src=uri});return true})()")
wait('window.auditCached',3)
assert call(sim,'state')['result']['assetReads']==reads
checks.append({'case':'delayed-native-project-switch-cache','status':'passed','observed':'Two distinct synthetic roots have 2px/3px content at identical relative path. Old request resolved null after switch; new 3px preview/cache remained, cached reread caused zero additional native reads.'})

root(0);call(sim,'asset-hold');image('shared.png');held()
js("(()=>{document.querySelector('#audit-media').remove();return true})()")
call(sim,'asset-release');time.sleep(.3)
assert js("window.auditImage.getAttribute('src')")=='novalist-project://project/media/shared.png'
assert js('window.auditImage.isConnected') is False
checks.append({'case':'delayed-native-dismissal','status':'passed','observed':'Dismissed node stayed disconnected with its original project-relative URL; late reply was not installed.'})

root(0);image('oversized.png');wait("!!window.auditImage.getAttribute('aria-description')")
reason=js("window.auditImage.getAttribute('aria-description')")
checks.append({'case':'native-oversize-rejection','status':'passed','observed':reason,'limit':'Native failure and accessible image error proved; full Research visible-error component remains separate.'})

before=json.loads((pathlib.Path(os.environ['NOVALIST_AUDIT_STATE']) / 'media-before.json').read_text())
after={str(index):{str(p.relative_to(pathlib.Path(folder))):hashlib.sha256(p.read_bytes()).hexdigest() for p in pathlib.Path(folder).rglob('*') if p.is_file()} for index,folder in enumerate(roots)}
assert before==after
checks.append({'case':'disk-retention-and-relative-references','status':'passed','observed':'All fixture file hashes including >16MiB image and project-relative reference JSON are unchanged.'})
out=pathlib.Path(__file__).resolve().parent
meta=json.loads((out/'audit-control-fixed-harness.json').read_text())
result={'source_revision':meta['source_revision'],'probe_diff_sha256':meta['probe_diff_sha256'],'device':'iPhone 17 Pro simulator','os':'iOS 27.0','status':'passed','checks':checks,'fixture_hashes':before,'limitations':['Exercises production asset loader and native host in actual WKWebView using synthetic DOM preview nodes; does not establish every scene/map/Research/3D UI integration.','No native memory peak or physical-device result claimed.','Probe removal and clean final build still required.']}
(out/'native-media-races-fixed.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps({'status':'passed','cases':len(checks),'checks':checks},indent=2))
