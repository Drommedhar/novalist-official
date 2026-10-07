from pathlib import Path
import subprocess,shutil,json,hashlib,sys
root=Path('__WORKDIR__');checkout=root/'checkout';mode=sys.argv[1];tag=sys.argv[2]
subprocess.run(['git','apply',*(['-R'] if mode=='clean' else []),str(root/'native-control-only.patch')],cwd=checkout,check=True)
for n in ['Novalist.Mobile','Novalist.Backend','Novalist.Core','Novalist.Sdk']:
 for o in ['bin','obj']:shutil.rmtree(checkout/n/o,ignore_errors=True)
manifest=json.loads((root/'production-inputs.json').read_text())
differences=[n for n,h in manifest.items() if hashlib.sha256((checkout/n).read_bytes()).hexdigest()!=h]
if mode=='clean':assert not differences and not(checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists()
record={'mode':mode,'case':tag,'native_bin_obj_cleared':True,'probe_source_present':(checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists(),'production_input_differences':differences}
(root/(tag+'-'+mode+'-source.json')).write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
