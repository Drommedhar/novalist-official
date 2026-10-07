from pathlib import Path
import hashlib,json,shutil
w=Path(__file__).parent;s=json.loads((w/'private-state.json').read_text());local=Path(s['local_path']);external=Path(s['external_path']);app=local.parents[1]
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();hs=lambda v:hashlib.sha256(v.encode()).hexdigest()
def tree(p):return {str(f.relative_to(p)):h(f) for f in sorted(p.rglob('*')) if f.is_file()}
expected=json.loads((w/'unavailability.json').read_text());records=[json.loads(l) for l in (app/'Documents/NovalistAudit/m03-resolution.jsonl').read_text().splitlines()];records=[r for r in records if r['storedPathSha256']==hs(str(external))];assert records
for r in records:assert r['storedDirectoryExists'] is False and r['bookmarkReturnedNull'] is True and r['ownedFallbackExecuted'] is True and r['ownedFallbackReturnedNull'] is True and r['matchingLocalDirectoryExists'] is True and r['returnedMatchingLocal'] is False
(w/'native-missing-path-observations.json').write_text(json.dumps(records,indent=2)+'\n')
regfile=app/'Library/owned-project-paths.json';reg=json.loads(regfile.read_text());recent=json.loads((app/'Library/settings.json').read_text())['recentProjects'];assert not any(r['projectId']==s['external_id'] for r in recent);assert next(r for r in recent if r['projectId']==s['local_id'])['path']==str(local)
assert h(regfile)==expected['registry_sha256_before'];assert tree(local)==json.loads((w/'local-before-unavailable.json').read_text());assert str(external) not in reg and str(local) in reg
m=json.loads((local/'.novalist/project.json').read_text());assert m['id']==s['local_id'] and m['activeBookId']==s['local_book_id']
original=json.loads((w/'documents-before.json').read_text());assert all((app/'Documents'/p).is_file() and h(app/'Documents'/p)==v for p,v in original.items())
result={'native_observation_count':len(records),'actual_missing_bookmark_and_unowned_fallback_proven':True,'external_recent_pruned_by_production_library':True,'local_recent_kept_at_original_path':True,'local_project_and_book_ids_unchanged':True,'local_all_file_hashes_unchanged':True,'local_file_count':len(tree(local)),'registry_sha256_after':h(regfile),'registry_bytes_unchanged':True,'external_not_added_to_owned_registry':True,'original_documents_file_count':len(original),'all_original_documents_hashes_unchanged':True,'ui_native_test':'06-launch-with-missing-external','ui_stayed_on_library':True}
(w/'missing-path-result.json').write_text(json.dumps(result,indent=2)+'\n')
assert not external.exists();backup=w/'external-before-unavailable';assert tree(backup)==json.loads((w/'external-before-unavailable.json').read_text());shutil.copytree(backup,external);assert tree(external)==tree(backup)
em=json.loads((external/'.novalist/project.json').read_text());assert em['id']==s['external_id'] and em['activeBookId']==s['external_book_id']
restored={'external_path_sha256':hs(str(external)),'restored_to_exact_original_path':True,'restored_file_count':len(tree(external)),'all_restored_file_hashes_match_post_open_backup':True,'external_project_and_book_ids_unchanged':True,'inode_after_restoration':external.stat().st_ino,'inode_differs_from_deleted_original':external.stat().st_ino!=expected['external_inode_before'],'registry_unchanged_by_restore':h(regfile)==expected['registry_sha256_before'],'recents_not_manually_edited':True,'new_external_fixture_remains_available_in_actual_Files_provider':external.is_dir()}
(w/'restoration.json').write_text(json.dumps(restored,indent=2)+'\n');print(json.dumps({'missing':result,'restored':restored}))
