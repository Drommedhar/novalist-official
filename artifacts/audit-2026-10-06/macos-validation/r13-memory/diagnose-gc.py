"""Diagnostic only: explicit GC must never count as production acceptance."""
import json, os, pathlib, runpy, subprocess, tempfile, time
base=pathlib.Path(__file__).resolve().parent
native=runpy.run_path(str(base.parent/'native-probe.py'));js=native['evaluate'];call=native['call'];sim=native['simulator']
processes=json.loads(pathlib.Path(os.environ['NOVALIST_MEMORY_PROCESSES']).read_text())
private=pathlib.Path(tempfile.mkdtemp(prefix='novalist-memory-diagnostic-',dir='/private/tmp'))
def snapshot(label):
    managed=call(sim,'memory-state')['result']
    path=private/'footprint.json'
    subprocess.run(['footprint','-p',str(processes['app']),'-p',str(processes['web']),'--noCategories','-f','bytes','-j',str(path)],check=True,stdout=subprocess.DEVNULL)
    data=json.loads(path.read_text())
    return {'label':label,'managed':managed,'native':[{'name':p['name'],'phys_footprint_peak':p['auxiliary']['phys_footprint_peak'],'phys_footprint':p['auxiliary']['phys_footprint']} for p in data['processes']]}
def select(title):
    js("(()=>{const r=[...document.querySelectorAll('.codex-row')].find(x=>x.querySelector('.codex-row-name')?.textContent==="+json.dumps(title)+");if(!r)throw Error('Missing fixture');r.click();return true})()")
snapshots=[snapshot('baseline')]
for index in [1,2,3,4]*3:
    select('Audit Memory '+str(index))
    deadline=time.monotonic()+25
    while not js("document.querySelector('.research-preview img')?.naturalWidth===1152"):
        assert time.monotonic()<deadline
        time.sleep(.1)
    select('Audit Memory Dispose');time.sleep(.1)
snapshots.append(snapshot('after-12-real-previews'))
call(sim,'diagnostic-gc');time.sleep(3)
snapshots.append(snapshot('after-diagnostic-only-GC'))
print(json.dumps({'scope':'Diagnostic only; explicit full GC/finalizer drain is not production acceptance. Clean fresh process using unfixed input-string ownership and temporary GC operations.','native_asset_reads':call(sim,'state')['result']['assetReads'],'snapshots':snapshots},indent=2))
