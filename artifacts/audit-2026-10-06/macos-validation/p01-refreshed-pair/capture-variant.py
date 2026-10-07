import base64,hashlib,json,re,subprocess,sys
from pathlib import Path
base=Path(__file__).parent
checkout=base/'checkout'
variant=sys.argv[1]
assert variant in ('original','fixed')
sha=lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest=lambda p:{str(f.relative_to(p)):sha(f) for f in sorted(p.rglob('*')) if f.is_file()}
inputs=json.loads((base/'source-inputs.json').read_text())
changed={p:{'expected':h,'actual':sha(checkout/p)} for p,h in inputs.items() if sha(checkout/p)!=h}
assert set(changed)==({'app/build/manual-plugin.ts'} if variant=='original' else set()),changed
assets=checkout/'Novalist.Mobile/Resources/Raw/app'
html=(assets/'index.mobile.html').read_text()
entry_path=re.search(r'<script[^>]+src="(\./assets/index\.mobile-[^"]+\.js)"',html).group(1)
entry=(assets/entry_path).read_bytes()
images={f.name:sha(f) for f in sorted((checkout/'docs/manual/images').glob('*.png'))}
embedded=[]
for m in re.finditer(rb'data:image/[^;,"\s]+;base64,([A-Za-z0-9+/=]+)',entry):
 try:embedded.append(hashlib.sha256(base64.b64decode(m.group(1),validate=True)).hexdigest())
 except ValueError:pass
if variant=='original':assert sorted(embedded)==sorted(images.values())
else:assert not embedded
asset_manifest=manifest(assets)
if variant=='fixed':
 for name,h in images.items():assert h in asset_manifest.values(),name
app=checkout/'Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app'
assert app.is_dir()
sig=subprocess.run(['codesign','--verify','--deep','--strict',str(app)],capture_output=True,text=True)
assert sig.returncode==0,sig.stderr
app_manifest=manifest(app)
for relative,h in asset_manifest.items():assert app_manifest['app/'+relative]==h,relative
record={'variant':variant,'source_input_differences':changed,'renderer_entry':entry_path,'entry_bytes':len(entry),'entry_sha256':hashlib.sha256(entry).hexdigest(),'base64_image_literals_in_entry':len(embedded),'embedded_manual_image_hashes':embedded,'manual_image_hashes':images,'renderer_asset_count':len(asset_manifest),'native_app_files':len(app_manifest),'native_app_bytes':sum(f.stat().st_size for f in app.rglob('*') if f.is_file()),'app_tree_sha256':hashlib.sha256(json.dumps(app_manifest,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'codesign_deep_strict_exit':sig.returncode,'built_assets_equal_app_assets':True}
for name,d in [(variant+'-renderer-assets.json',asset_manifest),(variant+'-app-manifest.json',app_manifest),(variant+'-build.json',record)]:
 (base/name).write_text(json.dumps(d,indent=2)+'\n')
print(json.dumps({k:v for k,v in record.items() if k not in ('manual_image_hashes','embedded_manual_image_hashes','source_input_differences')},indent=2))
