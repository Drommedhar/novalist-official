from pathlib import Path
import subprocess,shutil,json,hashlib,sys
root=Path('/private/tmp/novalist-m04-full');repo=Path('<repository>');checkout=root/'checkout';mode=sys.argv[1];tag=sys.argv[2]
probe=repo/'artifacts/audit-2026-10-06/macos-validation/audit-control-fixed.patch';final=repo/'artifacts/audit-2026-10-06/macos-validation/final-mobile-source.patch'
subprocess.run(['git','apply','-R',str(probe if mode=='clean' else final)],cwd=checkout,check=True)
subprocess.run(['git','apply',str(final if mode=='clean' else probe)],cwd=checkout,check=True)
for name in ['Novalist.Mobile','Novalist.Backend','Novalist.Core','Novalist.Sdk']:
 for folder in ['bin','obj']:shutil.rmtree(checkout/name/folder,ignore_errors=True)
record={'case':tag,'mode':mode,'audit_control_source_present':(checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists(),'bin_obj_cleared':True,'baseline_revision':'0c789b29581db47d89593c897bd53fbb3fb9275c','native_patch_sha256':hashlib.sha256((final if mode=='clean' else probe).read_bytes()).hexdigest(),'renderer_patch_evidence':'proactive-journal-source.json'}
(root/(tag+'-'+mode+'-source.json')).write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps(record))
