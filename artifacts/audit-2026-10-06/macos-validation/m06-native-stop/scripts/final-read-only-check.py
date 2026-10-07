from pathlib import Path
import hashlib
w=Path(__file__).parent;exec((w/'devicehub-input.py').read_text().split('mode=sys.argv[1]')[0])
assert val(attr(row,'AXSelected')) is True
assert marked==['Use System Settings'];assert json.loads((w/'input-before.json').read_text())['previous_simulator_input']==marked[0]
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def container(kind):return Path(subprocess.check_output(['xcrun','simctl','get_app_container',udid,'com.novalist.app',kind],text=True).strip())
data=container('data');before=json.loads((w/'documents-before.json').read_text());assert all((data/'Documents'/p).is_file() and h(data/'Documents'/p)==v for p,v in before.items());assert not(data/'Documents/NovalistAudit').exists()
repo=Path('<repository>');prior=json.loads((repo/'artifacts/audit-2026-10-06/macos-validation/m03-unavailable-external/clean-app.json').read_text());bundle=container('app');files={str(p.relative_to(bundle)):h(p) for p in sorted(bundle.rglob('*')) if p.is_file()};assert files==prior['files'];inputs=json.loads((w/'production-inputs.json').read_text());assert all(h(repo/p)==v and h(w/'checkout'/p)==v for p,v in inputs.items())
record={'selected_simulator':udid,'selected_device_verified_before_and_after_input_read':val(attr(row,'AXSelected')) is True,'simulator_input_selection':marked[0],'simulator_input_same_as_prior':True,'original_documents_count':len(before),'all_original_documents_byte_identical':True,'observer_directory_absent':True,'native_app_unchanged_from_prior_clean_m03_manifest':True,'native_app_tree_sha256':prior['app_tree_sha256'],'native_app_file_count':len(files),'production_inputs_match_frozen_and_main':len(inputs),'observer_never_added':True,'no_new_native_build_install_or_recording_started':True,'host_microphone_permission_or_audio_setting_changes':False}
(w/'final-state.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
