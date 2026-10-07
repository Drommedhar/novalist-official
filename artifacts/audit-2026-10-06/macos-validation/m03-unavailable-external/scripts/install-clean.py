from pathlib import Path
import json,hashlib,subprocess,shutil
w=Path(__file__).parent;c=w/'checkout';s=json.loads((w/'private-state.json').read_text());sim=s['simulator'];bundle=c/'Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app'
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
files={str(f.relative_to(bundle)):h(f) for f in sorted(bundle.rglob('*')) if f.is_file()};assert len(files)>500
subprocess.run(['codesign','--verify','--deep','--strict',str(bundle)],check=True)
record={'revision':json.loads((w/'source.json').read_text())['revision'],'build_exit':0,'signature_verified':True,'probe_removed':True,'app_file_count':len(files),'tree_hash_algorithm':'SHA256 of compact sorted JSON mapping relative file path to SHA256, separators comma/colon','app_tree_sha256':hashlib.sha256(json.dumps(files,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'files':files}
(w/'clean-app.json').write_text(json.dumps(record,indent=2)+'\n')
subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],check=False,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
subprocess.run(['xcrun','simctl','install',sim,str(bundle)],check=True)
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip());audit=data/'Documents/NovalistAudit'
assert not any(p=='NovalistAudit' or p.startswith('NovalistAudit/') for p in json.loads((w/'documents-before.json').read_text()))
if audit.exists():shutil.rmtree(audit)
print(json.dumps({k:v for k,v in record.items() if k!='files'}))
