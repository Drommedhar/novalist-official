from pathlib import Path
import subprocess,json,hashlib,shutil
w=Path('<audit-scratch>');app=w/'checkout/Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app';record={'app_tree_sha256':json.loads((w/'clean-build.json').read_text())['app_tree_sha256'],'devices':[]}
for sim in ['C9BC7CC6-C48A-431D-9CE0-BEA89C791D81','4F155C1B-8F9C-4919-8BD7-1CFAD764B61B']:
 root=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip());docs=root/'Documents';subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],capture_output=True)
 def hashes(base):return {str(p.relative_to(base)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(base.rglob('*')) if p.is_file() and 'NovalistAudit' not in p.relative_to(base).parts}
 before=hashes(docs);shutil.rmtree(docs/'NovalistAudit',ignore_errors=True)
 subprocess.run(['xcrun','simctl','install',sim,str(app)],check=True)
 fresh=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip());after=hashes(fresh/'Documents');assert before==after
 installed=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','app'],text=True).strip());installed_hashes=hashes(installed);tree=hashlib.sha256(json.dumps(installed_hashes,sort_keys=True,separators=(',',':')).encode()).hexdigest();assert tree==record['app_tree_sha256']
 subprocess.run(['xcrun','simctl','launch',sim,'com.novalist.app'],check=True)
 record['devices'].append({'simulator':sim,'install_launch_pass':True,'all_document_files_preserved':True,'document_file_count':len(before),'before_after_document_manifest_sha256':hashlib.sha256(json.dumps(before,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'installed_app_tree_matches_clean_build':True,'native_control_directory_removed':True,'container_relocated':root!=fresh})
(w/'clean-install.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
