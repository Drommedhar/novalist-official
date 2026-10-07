from pathlib import Path
import subprocess,json,hashlib,shutil,time,datetime
w=Path(__file__).resolve().parent;checkout=w/'checkout';revision=json.loads((w/'source.json').read_text())['revision'];repo=Path(str(Path.cwd()))
for kind in ['manuscript','research']:assert json.loads((w/(kind+'-scope.json')).read_text())['status']=='passed-simulator-supporting-scope-isolation'
for name in ['RendererHostPage.cs','RendererHostPage.Lifecycle.cs']:
 path='Novalist.Mobile/Pages/'+name;(checkout/path).write_bytes(subprocess.check_output(['git','show',revision+':'+path],cwd=repo))
(checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').unlink()
def hashes(path):return {str(p.relative_to(path)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(path.rglob('*')) if p.is_file()}
source=json.loads((w/'production-inputs.json').read_text());assert all(hashlib.sha256((checkout/p).read_bytes()).hexdigest()==v for p,v in source.items());assert hashes(checkout/'Novalist.Mobile/Resources/Raw/app')==json.loads((w/'renderer-assets.json').read_text())
for project in ['Novalist.Mobile','Novalist.Backend','Novalist.Core','Novalist.Sdk']:
 for name in ['bin','obj']:shutil.rmtree(checkout/project/name,ignore_errors=True)
args=['dotnet','build','Novalist.Mobile/Novalist.Mobile.csproj','-f','net10.0-ios27.0','-p:RuntimeIdentifier=iossimulator-arm64','-p:EnableCodeSigning=true','-p:CodesignKey=-','-m:1','-nr:false'];started=datetime.datetime.now(datetime.timezone.utc).isoformat();clock=time.monotonic();log=w/'clean-build.raw.log'
with log.open('w') as f:result=subprocess.run(args,cwd=checkout,stdout=f,stderr=subprocess.STDOUT)
r={'source_revision':revision,'started_utc':started,'exit_code':result.returncode,'seconds':round(time.monotonic()-clock,3),'command':args,'raw_log_sha256':hashlib.sha256(log.read_bytes()).hexdigest(),'temporary_observer_removed':True,'four_projects_built_from_cleared_bin_obj':True,'production_input_hashes_match':len(source),'renderer_asset_hashes_match':203};(w/'clean-build.json').write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r));assert result.returncode==0
