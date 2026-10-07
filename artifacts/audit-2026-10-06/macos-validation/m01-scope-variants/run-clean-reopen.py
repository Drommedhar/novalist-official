from pathlib import Path
import subprocess,time,json,hashlib,datetime
w=Path(__file__).resolve().parent;sid='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B';record={'started_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'screenshots':[]}
assert json.loads((w/'clean-install.json').read_text())['installed_equals_built']
p=subprocess.Popen(['python3',str(w/'run-native.py'),'clean-reopen'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);log=w/'clean-reopen.log';deadline=time.monotonic()+100;done=set()
while p.poll() is None:
 if time.monotonic()>deadline:p.terminate();raise TimeoutError('Clean native UI test deadline')
 text=log.read_text() if log.exists() else ''
 for kind in ['manuscript','research']:
  if kind not in done and ('M01_CLEAN_'+kind.upper()+'_READY') in text:
   path=w/('clean-'+kind+'.png');subprocess.run(['xcrun','simctl','io',sid,'screenshot',str(path)],check=True,capture_output=True);record['screenshots'].append({'kind':kind,'at_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()});done.add(kind)
 time.sleep(.03)
record['exit_code']=p.returncode;record['result']=p.communicate()[0];record['raw_log_sha256']=hashlib.sha256(log.read_bytes()).hexdigest();record['all_screenshots_captured']=len(done)==2
(w/'clean-native.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record));assert p.returncode==0 and len(done)==2
