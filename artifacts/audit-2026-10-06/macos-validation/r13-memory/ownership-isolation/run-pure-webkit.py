from pathlib import Path
import subprocess,time,json,re,sys
root=Path('__SWIFT_WORKDIR__');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81';bundle='com.novalist.audit.purewebkit'
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,bundle,'data'],text=True).strip())/'Documents'
def wait(target):
 until=time.monotonic()+50
 while time.monotonic()<until:
  try:j=json.loads((data/'state.json').read_text())
  except Exception:j={}
  if j.get('iterations',-1)==target and j.get('ready'):return j
  time.sleep(.1)
 raise TimeoutError('native run not finished')
def sample(tag,state):
 pid=state['pid'];record={'state':state}
 for tool,args in [('heap',['heap','--showSizes','--sortBySize',str(pid)]),('vmmap',['vmmap','-summary',str(pid)])]:
  p=subprocess.run(args,capture_output=True,text=True,check=True);(root/(tag+'-'+tool+'.txt')).write_text(p.stdout+p.stderr)
  if tool=='heap':record['large_cfstring_lines']=[l.strip() for l in p.stdout.splitlines() if 'CFString' in l]
  else:record['footprint_lines']=[l.strip() for l in p.stdout.splitlines() if 'footprint' in l.lower()]
 (root/(tag+'.json')).write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
state=wait(0);sample('pure-eval-0',state)
for target in [12,24]:
 (data/'command.json').write_text(json.dumps({'count':12,'size':10190000}))
 state=wait(target);time.sleep(2);sample('pure-eval-'+str(target),state)
