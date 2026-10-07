from pathlib import Path
import json,hashlib,subprocess,shutil,re
work=Path('<audit-scratch>');repo=Path('<repository>');checkout=work/'checkout';revision=json.loads((work/'source.json').read_text())['revision']
for name in ['RendererHostPage.cs','RendererHostPage.Transport.cs','RendererHostPage.Lifecycle.cs']:
 relative='Novalist.Mobile/Pages/'+name;(checkout/relative).write_bytes(subprocess.check_output(['git','show',revision+':'+relative],cwd=repo))
(checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').unlink(missing_ok=True)
inputs=json.loads((work/'production-inputs.json').read_text());different=[p for p,h in inputs.items() if not (checkout/p).is_file() or hashlib.sha256((checkout/p).read_bytes()).hexdigest()!=h];assert not different,different
assets=json.loads((work/'renderer-assets.json').read_text());different_assets=[p for p,h in assets.items() if hashlib.sha256((checkout/'Novalist.Mobile/Resources/Raw/app'/p).read_bytes()).hexdigest()!=h];assert not different_assets,different_assets
for project in ['Novalist.Mobile','Novalist.Backend','Novalist.Core','Novalist.Sdk']:
 for directory in ['bin','obj']:shutil.rmtree(checkout/project/directory,ignore_errors=True)
command=['dotnet','build','Novalist.Mobile/Novalist.Mobile.csproj','-f','net10.0-ios27.0','-r','iossimulator-arm64','-p:EnableCodeSigning=true','-p:CodesignKey=-','-p:NuGetAudit=false','-m:1','-nr:false','--nologo']
with (work/'clean-build.log').open('w') as output: result=subprocess.run(command,cwd=checkout,stdout=output,stderr=subprocess.STDOUT)
assert result.returncode==0
app=checkout/'Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app';subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
hashes={str(p.relative_to(app)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(app.rglob('*')) if p.is_file()};tree=hashlib.sha256(json.dumps(hashes,sort_keys=True,separators=(',',':')).encode()).hexdigest()
text=(work/'clean-build.log').read_text();record={'revision':revision,'source_input_count':len(inputs),'source_differences':different,'renderer_asset_count':len(assets),'renderer_differences':different_assets,'native_probe_source_absent':not (checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists(),'all_four_native_bin_obj_removed_before_build':True,'native_build_exit_code':result.returncode,'strict_deep_codesign':True,'app_tree_sha256':tree,'app_file_count':len(hashes),'command':command,'last_build_lines':text.splitlines()[-5:]};(work/'clean-build.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
