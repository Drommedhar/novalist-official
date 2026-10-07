from pathlib import Path
import subprocess,json,hashlib
root=Path('<temporary-r13-frames>');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81';checkout=root/'final-checkout'
def treehash(path):
 files={str(f.relative_to(path)):hashlib.sha256(f.read_bytes()).hexdigest() for f in path.rglob('*') if f.is_file()}
 return hashlib.sha256(json.dumps(files,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def data():return Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents/Audit Simulator Fixture'
before=json.loads((root/'fixture-before.json').read_text());p=data();hashes={n:hashlib.sha256((p/n).read_bytes()).hexdigest() for n in before['files']}
scene=(p/next(n for n in hashes if n.endswith('.novalist'))).read_text();m=json.loads((p/next(n for n in hashes if '/Maps/' in n)).read_text())
retention={'all_fixture_hashes_unchanged':hashes==before['files'],'image_scene_and_map_files':hashes,'scene_reference_relative':'src="Images/audit-quadrants.png"' in scene,'scene_has_no_data_uri':'data:image' not in scene,'map_reference_relative':m['layers'][0]['images'][0]['path']=='Images/audit-quadrants.png','map_has_no_data_uri':'data:image' not in json.dumps(m)}
retention['image_bytes_unchanged']=all(hashes[n]==before['files'][n] for n in hashes if n.endswith('.png'))
retention['scene_bytes_unchanged']=all(hashes[n]==before['files'][n] for n in hashes if n.endswith('.novalist'))
retention['map_hash_change_explained']='Production maps/save normalized optional empty lists and persisted the fitted viewport; image path and dimensions unchanged.'
retention['map_image_dimensions_preserved']=[m['layers'][0]['images'][0][k] for k in ['x','y','width','height','rotation']]==[0,0,600,600,0]
(root/'file-retention.json').write_text(json.dumps(retention,indent=2)+'\n');assert all(retention[n] for n in ['image_bytes_unchanged','scene_bytes_unchanged','map_image_dimensions_preserved','scene_reference_relative','scene_has_no_data_uri','map_reference_relative','map_has_no_data_uri'])
app=checkout/'Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app'
subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
assert not(checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists()
assert not any('AuditControl' in f.read_text() for f in (checkout/'Novalist.Mobile/Pages').glob('*.cs'))
subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],capture_output=True)
subprocess.run(['xcrun','simctl','install',sim,str(app)],check=True)
subprocess.run(['xcrun','simctl','launch',sim,'com.novalist.app'],check=True)
after={n:hashlib.sha256((data()/n).read_bytes()).hexdigest() for n in before['files']};assert hashes==after
record={'source_snapshot':'final-snapshot.json','clean_native_build':True,'probe_source_and_hooks_absent':True,'strict_deep_signature_passed':True,'installed_and_launched':True,'synthetic_fixture_hashes_preserved':True,'app_tree_sha256':treehash(app),'app_tree_hash_method':'SHA256 compact sorted JSON mapping relative app paths to file SHA256','limitation':'Exact snapshot cleanup; subsequent memory-related source changes are not covered.'}
(root/'final-clean-install.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record,indent=2))
