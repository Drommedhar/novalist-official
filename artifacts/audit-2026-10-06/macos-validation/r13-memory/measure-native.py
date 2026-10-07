"""Actual Research preview/disposal, read-only production cache snapshot, native footprint."""
import json, os, pathlib, runpy, subprocess, tempfile, threading, time
base=pathlib.Path(__file__).resolve().parent
out=pathlib.Path(os.environ.get('NOVALIST_MEMORY_RESULTS',str(base)))
out.mkdir(parents=True,exist_ok=True)
batches=int(os.environ.get('NOVALIST_MEMORY_BATCHES','4'))
native=runpy.run_path(str(base.parent/'native-probe.py'));js=native['evaluate'];call=native['call'];sim=native['simulator']
processes=json.loads(pathlib.Path(os.environ['NOVALIST_MEMORY_PROCESSES']).read_text())
started=time.monotonic();phase='baseline';step=0;samples=[];errors=[];stop=threading.Event();points=[]
private=pathlib.Path(tempfile.mkdtemp(prefix='novalist-memory-sampling-',dir='/private/tmp'))
def sampler():
    while not stop.is_set():
        try:
            path=private/'footprint.json'
            subprocess.run(['footprint','-p',str(processes['app']),'-p',str(processes['web']),'--noCategories','-f','bytes','-j',str(path)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
            data=json.loads(path.read_text());by_pid={x['pid']:x for x in data['processes']}
            entry={'seconds':round(time.monotonic()-started,3),'phase':phase,'step':step}
            for key,pid in processes.items():
                value=by_pid[pid];entry[key+'_bytes']=value['auxiliary']['phys_footprint'];entry[key+'_peak_bytes']=value['auxiliary']['phys_footprint_peak']
            samples.append(entry)
        except Exception as error:errors.append(type(error).__name__+': '+str(error))
        stop.wait(.5)
thread=threading.Thread(target=sampler,daemon=True);thread.start()
def wait(expression,expected=True,timeout=25):
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        result=js(expression)
        if result==expected:return
        time.sleep(.1)
    raise AssertionError((expression,result,expected))
def choose(index):
    title='Audit Memory Dispose' if index is None else 'Audit Memory '+str(index)
    js("(()=>{const el=[...document.querySelectorAll('.codex-row')].find(x=>x.querySelector('.codex-row-name')?.textContent==="+json.dumps(title)+");if(!el)throw Error('Missing synthetic memory fixture');el.click();return true})()")
    if index is None:wait("document.querySelector('.research-preview img')===null")
    else:wait("(()=>{const m=document.querySelector('.research-preview img');return m?.naturalWidth===1152&&m?.getAttribute('src')?.startsWith('data:image/png')&&!document.querySelector('.research-editor [role=alert]')})()")
def snapshot(label):
    cache=js('window.__novalistAuditAssetCache()');state=call(sim,'state')['result']
    assert cache['bytes']==sum(x['uriBytes'] for x in cache['entries'])
    assert all(x['bytes']==x['uriBytes'] for x in cache['entries'])
    assert 0<=cache['bytes']<=33554432 and cache['maximumBytes']==33554432
    assert cache['pending']==0
    assert state['webProcessId']==processes['web'] and not state['bridgeFailed']
    item={'seconds':round(time.monotonic()-started,3),'phase':phase,'step':step,'label':label,'cache':cache,'native_asset_reads':state['assetReads']};points.append(item)
    return item
try:
    time.sleep(3);snapshot('empty-start')
    phase='lru-check'
    for index in [1,2,1,3,2]:
        step+=1;choose(index);snapshot('preview-'+str(index));choose(None);snapshot('disposed-'+str(index))
    phase='warmup-idle';time.sleep(5);snapshot('warmup-settled')
    for batch in range(batches):
        phase='batch-'+str(batch+1)
        for index in [1,2,3,4]*4:
            step+=1;choose(index);snapshot('preview-'+str(index));choose(None);snapshot('disposed-'+str(index))
        phase='settled-'+str(batch+1);time.sleep(5);snapshot('batch-settled')
        print(json.dumps({'batch':batch+1,'previews':step,'cache_bytes':points[-1]['cache']['bytes'],'native_asset_reads':points[-1]['native_asset_reads'],'samples':len(samples)}),flush=True)
    phase='final-idle';time.sleep(15);snapshot('final-settled')
finally:
    stop.set();thread.join(timeout=5)
    (out/'native-footprint-samples.json').write_text(json.dumps({'measure':'macOS footprint auxiliary phys_footprint and phys_footprint_peak in bytes, exact simulator app and associated WKWebContent processes only','interval_seconds':.5,'samples':samples,'errors':errors},indent=2)+'\n')
    (out/'cache-observations.json').write_text(json.dumps(points,indent=2)+'\n')
assert not errors,errors
assert samples and len(points)==13+33*batches,len(points)
print(json.dumps({'status':'workload-and-cache-checks-passed; native-footprint-requires-separate-analysis','previews':step,'cache_observations':len(points),'native_samples':len(samples),'elapsed_seconds':round(time.monotonic()-started,3)}))
