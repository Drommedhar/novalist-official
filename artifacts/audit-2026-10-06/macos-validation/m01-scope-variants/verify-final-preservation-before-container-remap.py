from pathlib import Path
import subprocess,hashlib,json,datetime,runpy
w=Path(__file__).resolve().parent;sid='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B';data=Path((w/'app-data-clean.txt').read_text().strip());lib=runpy.run_path(str(w/'scope-lib.py'))
def hashes(path):return {str(p.relative_to(path)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(path.rglob('*')) if p.is_file()}
old=json.loads((w/'documents-before.json').read_text());changed=[p for p,v in old.items() if not (data/'Documents'/p).is_file() or hashlib.sha256((data/'Documents'/p).read_bytes()).hexdigest()!=v];assert not changed,changed
before=json.loads((w/'fixtures-before-clean.json').read_text());after={name:hashes(data/'Documents'/name) for name in before};(w/'fixtures-after-clean.json').write_text(json.dumps(after,indent=2)+'\n');assert after==before
ms=json.loads((w/'manuscript-scope.json').read_text());rs=json.loads((w/'research-scope.json').read_text());disk_m=lib['disk']('manuscript');disk_r=lib['disk']('research');assert disk_m==ms['returned_to_exact_owner']['disk'];assert disk_r==rs['returned_to_exact_owner']['disk'];assert sum(v['marker_count'] for v in disk_m)==1 and sum(v['marker_count'] for v in disk_r)==1
m_unchanged=[a['path'] for a,b in zip(disk_m,ms['baseline_disk']) if a==b];assert len(m_unchanged)==5
r_unchanged=[a['projectId'] for a,b in zip(disk_r,rs['baseline_disk']) if a==b];assert len(r_unchanged)==1
assert not (data/'Documents/NovalistAudit').exists();assert json.loads((w/'clean-native.json').read_text())['exit_code']==0
subprocess.run(['xcrun','simctl','terminate',sid,'com.novalist.app'],check=True)
record={'at_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'original_files':len(old),'all_original_hashes_unchanged':True,'new_fixture_files':sum(map(len,after.values())),'all_new_fixture_hashes_unchanged_across_clean_reopen':True,'all_five_wrong_scene_files_match_initial_baseline':True,'unchanged_scene_paths':m_unchanged,'other_project_research_payload_matches_initial_baseline':True,'unchanged_research_project':r_unchanged,'manuscript_exact_owner_marker_count':1,'research_exact_existing_item_marker_count':1,'research_existing_id_title_and_count_preserved':True,'final_scene_files':disk_m,'final_research_payloads':disk_r,'observer_control_directory_absent':True,'clean_app_terminated_after_final_success':True,'native_clean_reopen_passed':True}
(w/'preservation.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps({k:v for k,v in record.items() if k not in ['final_scene_files','final_research_payloads']}))
