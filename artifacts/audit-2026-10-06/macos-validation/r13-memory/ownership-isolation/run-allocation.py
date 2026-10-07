from pathlib import Path
import subprocess,time,json,runpy
root=Path('__ALLOCATION_WORKDIR__');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81';bundle='com.novalist.app'
app=root/'checkout/Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app'
client=runpy.run_path('__REPOSITORY__/artifacts/audit-2026-10-06/macos-validation/audit-control-client.py')['call']
subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.audit.purewebkit'],capture_output=True)
subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True,capture_output=True)
subprocess.run(['xcrun','simctl','install',sim,str(app)],check=True)
def sample(tag,pid,response=None):
 record={'response':response,'process_id':pid}
 for tool,args in [('heap',['heap','--showSizes','--sortBySize',str(pid)]),('vmmap',['vmmap','-summary',str(pid)])]:
  p=subprocess.run(args,capture_output=True,text=True,check=True);(root/(tag+'-'+tool+'.txt')).write_text(p.stdout+p.stderr)
  if tool=='heap':record['cfstring_lines']=[l.strip() for l in p.stdout.splitlines() if 'CFString' in l]
  else:record['footprint_lines']=[l.strip() for l in p.stdout.splitlines() if 'footprint' in l.lower()]
 (root/(tag+'.json')).write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record),flush=True)
for label,op in [('constructor','string-allocation-only'),('owned','string-owned-allocation-only')]:
 subprocess.run(['xcrun','simctl','terminate',sim,bundle],capture_output=True)
 output=subprocess.check_output(['xcrun','simctl','launch',sim,bundle],text=True).strip();pid=int(output.split(':')[-1]);time.sleep(6)
 state=client(sim,'state');sample(label+'-0',pid,state)
 for target in [12,24]:
  response=client(sim,op);time.sleep(2);sample(label+'-'+str(target),pid,response)
