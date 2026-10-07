from pathlib import Path
import os,runpy,json,time,hashlib,subprocess
w=Path(__file__).resolve().parent;sid='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B';os.environ['NOVALIST_AUDIT_SIMULATOR']=sid
repo=Path(os.environ.get('NOVALIST_REPO',str(Path.cwd())));p=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'));js,call=p['evaluate'],p['call']
def async_call(body):
 js("(()=>{window.auditScopeTask={status:'running'};(async()=>{"+body+"})().then(result=>window.auditScopeTask={status:'ready',result}).catch(error=>window.auditScopeTask={status:'error',error:String(error)});return true})()")
 deadline=time.monotonic()+20
 while time.monotonic()<deadline:
  r=js('window.auditScopeTask')
  if r['status']=='ready':return r.get('result')
  if r['status']=='error':raise RuntimeError(r['error'])
  time.sleep(.08)
 raise TimeoutError('Production action')
def canonical(value):return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def data_path():return Path((w/'app-data-probe.txt').read_text().strip())/'Documents'
