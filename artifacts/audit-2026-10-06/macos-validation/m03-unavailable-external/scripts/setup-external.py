from pathlib import Path
import subprocess,json,hashlib,shutil,uuid
w=Path(__file__).parent;sim='6D872EE9-8FBE-4B2D-AED4-917146C77351'
def container(bundle):return Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,bundle,'data'],text=True).strip())
app=container('com.novalist.app');provider=container('com.novalist.audit.fixtures');name='M03 Unavailable Suffix';local=app/'Documents'/name;external=provider/'Documents'/name
assert local.is_dir() and not external.exists();assert not any(p.startswith(name+'/') for p in json.loads((w/'documents-before.json').read_text()))
local_meta=json.loads((local/'.novalist/project.json').read_text());registry=json.loads((app/'Library/owned-project-paths.json').read_text());assert str(local) in registry and str(external) not in registry
shutil.copytree(local,external);p=external/'.novalist/project.json';m=json.loads(p.read_text());m['id']='m03-external-'+uuid.uuid4().hex;m['name']='M03 External Unavailable';m['books'][0]['id']='m03-book-'+uuid.uuid4().hex;m['books'][0]['name']='M03 External Proof';m['activeBookId']=m['books'][0]['id'];p.write_text(json.dumps(m,indent=2)+'\n')
h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();hs=lambda s:hashlib.sha256(s.encode()).hexdigest()
state={'simulator':sim,'name':name,'local_path':str(local),'external_path':str(external),'local_id':local_meta['id'],'local_book_id':local_meta['activeBookId'],'external_id':m['id'],'external_book_id':m['activeBookId']};(w/'private-state.json').write_text(json.dumps(state,indent=2)+'\n')
record={'local_created_through_native_ui':True,'matching_documents_relative_path':name,'different_project_and_book_ids':local_meta['id']!=m['id'] and local_meta['activeBookId']!=m['activeBookId'],'local_project_id_sha256':hs(local_meta['id']),'local_book_id_sha256':hs(local_meta['activeBookId']),'external_project_id_sha256':hs(m['id']),'external_book_id_sha256':hs(m['activeBookId']),'local_owned':str(local) in registry,'external_unowned':str(external) not in registry,'external_path_sha256':hs(str(external)),'registry_unchanged_by_setup':h(app/'Library/owned-project-paths.json')==h(app/'Library/owned-project-paths.json'),'fixture_previously_absent':True}
(w/'fixture-setup.json').write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
