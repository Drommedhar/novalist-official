from pathlib import Path
import subprocess,shutil,json,uuid,hashlib
w=Path(__file__).resolve().parent;o=json.loads((w/'owner.json').read_text());sid='4F155C1B-8F9C-4919-8BD7-1CFAD764B61B';subprocess.run(['xcrun','simctl','terminate',sid,'com.novalist.app'],check=True);p=Path(o['path']);a=p/o['bookFolder']/'Drafts'/o['draftFolder'];b=p/o['otherBookFolder']/'Drafts/default';shutil.copytree(a,b,dirs_exist_ok=True)
copy=p.parent/'Audit M01 Scope Variants Copy';assert not copy.exists();shutil.copytree(p,copy);meta=json.loads((copy/'.novalist/project.json').read_text());meta['id']=str(uuid.uuid4());meta['name']=copy.name;meta['activeBookId']=o['bookId'];(copy/'.novalist/project.json').write_text(json.dumps(meta,indent=2)+'\n');o.update(copyPath=str(copy),copyProjectId=meta['id']);(w/'owner.json').write_text(json.dumps(o,indent=2)+'\n')
files={}
for base in [p,copy]:
 for f in sorted(base.rglob('*')):
  if f.is_file():files[str(f.relative_to(p.parent))]=hashlib.sha256(f.read_bytes()).hexdigest()
(w/'new-fixtures-baseline.json').write_text(json.dumps(files,indent=2)+'\n')
(w/'fixture-configuration.json').write_text(json.dumps({'new_project_created_native_ui':True,'second_book_created_production_action':True,'alternate_draft_created_production_clone_action':True,'second_book_baseline_cloned_while_stopped':True,'same_scene_and_chapter_ids_in_all_contexts':True,'distinct_copy_project_id':meta['id'],'original_project_id':o['projectId'],'copy_retains_book_draft_scene_and_research_ids':True,'journals_recents_registry_modified':False},indent=2)+'\n')
print('Two synthetic project fixtures prepared;',len(files),'baseline files; app remains stopped.')
