from pathlib import Path
import subprocess,json,hashlib,sys,time
root=Path('__WORKDIR__');checkout=root/'checkout';mode,tag=sys.argv[1:3]
subprocess.run(['python3',str(root/'mode.py'),mode,tag],check=True)
log=root/(tag+'-'+mode+'-build.log')
args=['dotnet','build','Novalist.Mobile/Novalist.Mobile.csproj','-f','net10.0-ios27.0','-p:RuntimeIdentifier=iossimulator-arm64','-p:EnableCodeSigning=true','-p:CodesignKey=-','-p:NuGetAudit=false','-m:1','-nr:false']
with log.open('w') as out:r=subprocess.run(args,cwd=checkout,stdout=out,stderr=subprocess.STDOUT)
record={'mode':mode,'case':tag,'build_exit_code':r.returncode,'log_sha256':hashlib.sha256(log.read_bytes()).hexdigest()}
if r.returncode==0:
 app=checkout/'Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app'
 verify=subprocess.run(['codesign','--verify','--deep','--strict',str(app)],capture_output=True,text=True);record['codesign_exit_code']=verify.returncode
 digest=hashlib.sha256()
 for p in sorted(app.rglob('*')):
  if p.is_file():digest.update(str(p.relative_to(app)).encode());digest.update(bytes.fromhex(hashlib.sha256(p.read_bytes()).hexdigest()))
 record['app_tree_sha256']=digest.hexdigest()
 if verify.returncode==0:
  sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
  subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],capture_output=True)
  subprocess.run(['xcrun','simctl','install',sim,str(app)],check=True)
  record['installed']=True
  subprocess.run(['xcrun','simctl','launch',sim,'com.novalist.app'],check=True,capture_output=True)
  record['launched']=True
(root/(tag+'-'+mode+'-build.json')).write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record));sys.exit(r.returncode)
