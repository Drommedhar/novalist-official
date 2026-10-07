from pathlib import Path
import subprocess,hashlib,json,shutil,datetime
w=Path(__file__).resolve().parent;sid='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B';app=w/'checkout/Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app'
assert json.loads((w/'clean-build.json').read_text())['exit_code']==0
subprocess.run(['codesign','--verify','--deep',str(app)],check=True)
subprocess.run(['xcrun','simctl','terminate',sid,'com.novalist.app'],check=True)
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sid,'com.novalist.app','data'],text=True).strip())
control=data/'Documents/NovalistAudit';shutil.copytree(control,w/'private-final-observer-control');shutil.rmtree(control)
old=json.loads((w/'ipad-documents-before.json').read_text());assert all((data/'Documents'/p).is_file() and hashlib.sha256((data/'Documents'/p).read_bytes()).hexdigest()==v for p,v in old.items())
fixture=data/'Documents/Audit M01 Lock Variants';before={str(p.relative_to(fixture)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(fixture.rglob('*')) if p.is_file()};(w/'fixture-before-clean.json').write_text(json.dumps(before,indent=2)+'\n')
subprocess.run(['xcrun','simctl','install',sid,str(app)],check=True)
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sid,'com.novalist.app','data'],text=True).strip());installed=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sid,'com.novalist.app','app'],text=True).strip());(w/'app-data-clean.txt').write_text(str(data)+'\n')
def hashes(path):return {str(p.relative_to(path)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(path.rglob('*')) if p.is_file()}
files=hashes(app);assert files==hashes(installed);assert not (data/'Documents/NovalistAudit').exists();assert all(hashlib.sha256((data/'Documents'/p).read_bytes()).hexdigest()==v for p,v in old.items());assert hashes(data/'Documents/Audit M01 Lock Variants')==before
source=json.loads((w/'production-inputs.json').read_text());assert all(hashlib.sha256((w/'checkout'/p).read_bytes()).hexdigest()==v for p,v in source.items());assert hashes(w/'checkout/Novalist.Mobile/Resources/Raw/app')==json.loads((w/'renderer-assets.json').read_text())
assert not (w/'checkout/Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists()
for f in app.rglob('Novalist.Mobile.dll'):
 symbols=subprocess.check_output(['strings',str(f)],text=True);assert 'AuditControlLoopAsync' not in symbols and 'AuditRecord' not in symbols
(w/'final-clean-app-inputs.json').write_text(json.dumps(files,indent=2)+'\n')
r={'at_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_revision':'af744fbdb0e3d46a9d37be47243f6a7cb75fc9cd','deep_signature_verified':True,'production_input_count':len(source),'all_production_input_hashes_match':True,'renderer_assets_count':203,'all_renderer_hashes_match':True,'clean_bundle_files':len(files),'clean_bundle_tree_sha256':hashlib.sha256(json.dumps(files,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'installed_equals_built':True,'native_observer_source_and_symbols_absent':True,'control_directory_removed':True,'original_document_files':len(old),'original_document_hashes_unchanged':True,'new_fixture_hashes_unchanged_across_install':True}
(w/'clean-install.json').write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r))
