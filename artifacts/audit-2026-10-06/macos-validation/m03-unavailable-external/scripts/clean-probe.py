from pathlib import Path
import hashlib,json,subprocess,shutil
w=Path(__file__).parent;c=w/'checkout';main=Path('<repository>')
subprocess.run(['patch','-R','-p1','-i',str(w/'read-only-resolver-observer.patch')],cwd=c,check=True)
assert not (c/'Novalist.Mobile/Services/AuditMissingPathObservation.cs').exists()
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
inputs=json.loads((w/'production-inputs.json').read_text());assets=json.loads((w/'renderer-assets.json').read_text())
assert all(h(c/p)==v and h(main/p)==v for p,v in inputs.items())
assert all(h(c/'Novalist.Mobile/Resources/Raw/app'/p)==v and h(main/'app/dist-mobile'/p)==v for p,v in assets.items())
for project in ['Novalist.Core','Novalist.Sdk','Novalist.Backend','Novalist.Mobile']:
 for folder in ['bin','obj']:
  p=c/project/folder
  if p.exists():shutil.rmtree(p)
(w/'clean-source.json').write_text(json.dumps({'production_input_count':len(inputs),'production_inputs_match_frozen_and_main':True,'renderer_asset_count':len(assets),'renderer_assets_match_frozen_and_main':True,'observer_removed':True,'four_native_project_bin_obj_removed':True},indent=2)+'\n')
print('Observer removed; source and assets verified; native outputs removed.')
