from pathlib import Path
import subprocess,shutil,json,hashlib
root=Path('/private/tmp/novalist-m04-full');repo=Path('<repository>');checkout=root/'checkout'
patch=repo/'artifacts/audit-2026-10-06/macos-validation/audit-control-fixed.patch'
subprocess.run(['git','apply','-R',str(patch)],cwd=checkout,check=True)
final=repo/'artifacts/audit-2026-10-06/macos-validation/final-mobile-source.patch'
subprocess.run(['git','apply',str(final)],cwd=checkout,check=True)
assert not (checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists()
for name in ['Novalist.Mobile','Novalist.Backend','Novalist.Core','Novalist.Sdk']:
 for folder in ['bin','obj']:
  shutil.rmtree(checkout/name/folder,ignore_errors=True)
record={'after_case':'evaluator-native-failure','probe_source_removed':True,'baseline_revision':'0c789b29581db47d89593c897bd53fbb3fb9275c','final_mobile_patch_sha256':hashlib.sha256(final.read_bytes()).hexdigest(),'mobile_backend_core_sdk_bin_obj_removed':True}
(root/'evaluator-clean-source.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps(record))
