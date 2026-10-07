import json
import os
import pathlib
import runpy
import subprocess
import sys
import time

n=runpy.run_path(str(pathlib.Path(__file__).with_name('native-probe.py')))
js,call,sim=n['evaluate'],n['call'],n['simulator']
case=sys.argv[1]
out=pathlib.Path(__file__).resolve().parent
healthy=call(sim,'state')['result']
assert healthy['bridgeFailed']==0
if case=='webcontent-termination':
    pid=healthy['webProcessId']
    assert pid and pid>1
    command=subprocess.check_output(['ps','-p',str(pid),'-o','comm='],text=True).strip()
    assert 'WebContent' in command
    call(sim,'asset-hold')
    root=json.loads((pathlib.Path(os.environ['NOVALIST_AUDIT_STATE']) / 'media-roots.json').read_text())[0]
    js('(()=>{window.novalist.setProjectRoot('+json.dumps(root)+');return true})()')
    time.sleep(.2)
    js("(()=>{window.auditFault='pending';void window.novalist.readProjectAsset('media/second.png').then(()=>window.auditFault='fulfilled',()=>window.auditFault='rejected');return true})()")
    for _ in range(30):
        if call(sim,'state')['result']['assetHeld']:break
        time.sleep(.1)
    else:raise AssertionError('No pending native call before real termination')
    subprocess.run(['kill','-KILL',str(pid)],check=True)
else:
    if case=='backend-failure':
        call(sim,'asset-hold')
        root=json.loads((pathlib.Path(os.environ['NOVALIST_AUDIT_STATE']) / 'media-roots.json').read_text())[0]
        js('(()=>{window.novalist.setProjectRoot('+json.dumps(root)+');return true})()')
        time.sleep(.2)
        js("(()=>{window.auditFault='pending';void window.novalist.readProjectAsset('media/second.png').then(()=>window.auditFault='fulfilled',()=>window.auditFault='rejected');return true})()")
        for _ in range(30):
            if call(sim,'state')['result']['assetHeld']:break
            time.sleep(.1)
        else:raise AssertionError('No pending native call before pump failure')
        call(sim,case)
    else:
        call(sim,case)
        js("(()=>{window.auditFault='pending';void window.novalist.defaultProjectRoot().then(()=>window.auditFault='fulfilled',error=>window.auditFault={status:'rejected',message:error.message});return true})()")

start=time.monotonic()
for _ in range(210):
    state=call(sim,'state')['result']
    if state['nativeEditingPausedAlert'] and state['nativeReloadAvailable'] and state['nativeLaterAvailable']:break
    time.sleep(.1)
else:raise AssertionError('Actual native Editing paused alert did not appear')
elapsed=time.monotonic()-start
settled=None if case=='webcontent-termination' else js('window.auditFault')
if case!='webcontent-termination':
    assert settled=='rejected' or isinstance(settled,dict) and settled['status']=='rejected',settled
if case=='evaluator-timeout':
    armed=next(e for e in state['events'] if e['kind']=='evaluator-timeout-consumed')
    failed=next(e for e in state['events'] if e['kind']=='bridge-failed')
    # Apple's Stopwatch frequency on this arm64 runtime is nanoseconds.
    delta=failed['ticks']-armed['ticks']
    assert 14.5e9<=delta<=18e9,delta
else:delta=None
if case=='webcontent-termination':assert any(e['kind']=='content-process-did-terminate' for e in state['events'])
meta=json.loads((out/'audit-control-fixed-harness.json').read_text())
record={'scenario':case,'status':'transport-subset-passed','source_revision':meta['source_revision'],'probe_diff_sha256':meta['probe_diff_sha256'],'device':'iPhone 17 Pro simulator','os':'iOS 27.0','native_state':state,'pending_host_call':settled,'wall_seconds_until_alert':elapsed,'timeout_ticks':delta,'limits':['Native Reload action and post-reload production health are recorded separately.','This transport subset does not prove retained edit recovery or no replay of a mutation.']}
(out/('m04-fixed-'+case+'.json')).write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps({'scenario':case,'alert':True,'pending':settled,'seconds':elapsed,'webprocess':state['webProcessId']},indent=2))
