from pathlib import Path
import subprocess,json,hashlib,tarfile,io,shutil
w=Path(__file__).parent;repo=Path('<repository>');revision='560839ac27746ec31c948d19c91ae3813eb5ec37';sim='6D872EE9-8FBE-4B2D-AED4-917146C77351';c=w/'checkout';c.mkdir()
blob=subprocess.check_output(['git','archive',revision],cwd=repo);tarfile.open(fileobj=io.BytesIO(blob)).extractall(c,filter='data')
shutil.copytree(repo/'Novalist.Mobile/Resources/Raw/app',c/'Novalist.Mobile/Resources/Raw/app',dirs_exist_ok=True)
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
base=repo/'artifacts/audit-2026-10-06/macos-validation/m03-unavailable-external'
for n in ['production-inputs.json','renderer-assets.json']:shutil.copy2(base/n,w/n)
inputs=json.loads((w/'production-inputs.json').read_text());assets=json.loads((w/'renderer-assets.json').read_text());assert all(h(repo/p)==v and h(c/p)==v for p,v in inputs.items());assert all(h(c/'Novalist.Mobile/Resources/Raw/app'/p)==v for p,v in assets.items())
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip());before={str(p.relative_to(data/'Documents')):h(p) for p in sorted((data/'Documents').rglob('*')) if p.is_file()};assert not any(p.startswith('NovalistAudit/') for p in before)
for n in ['Documents','Library']:shutil.copytree(data/n,w/'app-data-before'/n)
(w/'documents-before.json').write_text(json.dumps(before,indent=2)+'\n');(w/'source.json').write_text(json.dumps({'revision':revision,'simulator':sim,'production_inputs':len(inputs),'mobile_assets':len(assets),'original_documents':len(before),'scope':'Native explicit stop count with simulator Input=None; no capture contents or transcript observations.'},indent=2)+'\n')
shutil.copytree(Path('<private-m03-workspace>/AuditHarness.xcodeproj'),w/'AuditHarness.xcodeproj');shutil.copy2(Path('<private-m03-workspace>/run.py'),w/'run.py')
print(json.dumps({'snapshot':revision,'source_files':len(inputs),'assets':len(assets),'backed_up_documents':len(before)}))
