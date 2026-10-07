from pathlib import Path
import hashlib,json,shutil,subprocess
w=Path(__file__).parent;s=json.loads((w/'private-state.json').read_text());sim=s['simulator']
app=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip());provider=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.audit.fixtures','data'],text=True).strip())
local=Path(s['local_path']);external=Path(s['external_path']);backup=w/'external-before-unavailable'
assert local == app/'Documents'/s['name'];assert external == provider/'Documents'/s['name'];assert not external.is_symlink();assert external.is_dir() and not backup.exists()
assert not any(p.startswith(s['name']+'/') for p in json.loads((w/'documents-before.json').read_text()))
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();hs=lambda v:hashlib.sha256(v.encode()).hexdigest()
def tree(p):return {str(f.relative_to(p)):h(f) for f in sorted(p.rglob('*')) if f.is_file()}
registry=app/'Library/owned-project-paths.json';bookmarks=app/'Library/security-bookmarks.json';settings=app/'Library/settings.json';reg=json.loads(registry.read_text());bm=json.loads(bookmarks.read_text());recent=json.loads(settings.read_text())['recentProjects'];e=next(r for r in recent if r['projectId']==s['external_id']);l=next(r for r in recent if r['projectId']==s['local_id']);assert e['path']==str(external) and l['path']==str(local);assert str(external) in bm and str(external) not in reg and str(local) in reg
subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],check=True)
shutil.copytree(external,backup);assert tree(external)==tree(backup)
for name in ['owned-project-paths.json','security-bookmarks.json','settings.json']:shutil.copy2(app/'Library'/name,w/('private-before-'+name))
(w/'local-before-unavailable.json').write_text(json.dumps(tree(local),indent=2)+'\n');(w/'external-before-unavailable.json').write_text(json.dumps(tree(external),indent=2)+'\n')
obs=app/'Documents/NovalistAudit/m03-resolution.jsonl';before=obs.read_text() if obs.exists() else '';(w/'observations-before.jsonl').write_text(before)
record={'external_path_sha256':hs(str(external)),'local_path_sha256':hs(str(local)),'external_opened_by_actual_picker':True,'external_recent_identity_matches':True,'external_bookmark_present':True,'external_bookmark_sha256':hs(bm[str(external)]),'local_owned':True,'external_unowned':True,'matching_documents_relative_path':s['name'],'registry_sha256_before':h(registry),'local_file_count':len(tree(local)),'external_file_count':len(tree(external)),'external_backup_matches':True,'external_inode_before':external.stat().st_ino,'app_terminated_before_removal':True,'observer_lines_before':len(before.splitlines())}
shutil.rmtree(external)
assert not external.exists();assert h(registry)==record['registry_sha256_before'];assert tree(local)==json.loads((w/'local-before-unavailable.json').read_text());assert h(bookmarks)==h(w/'private-before-security-bookmarks.json');assert h(settings)==h(w/'private-before-settings.json')
record.update({'external_path_absent_after_actual_removal':True,'local_hashes_unchanged_by_removal':True,'registry_unchanged_by_removal':True,'bookmarks_unchanged_by_removal':True,'recents_unchanged_by_removal':True,'only_new_external_project_directory_removed':True})
(w/'unavailability.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
