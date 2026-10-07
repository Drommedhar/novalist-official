from pathlib import Path
import subprocess,json,hashlib,sys
u=sys.argv[2];root=Path(subprocess.check_output(['xcrun','simctl','get_app_container',u,'com.novalist.audit.fixtures','data'],text=True).strip())/'Documents/MovedParent'
marker=b'Synthetic M02 manuscript fixture.'
projects={}
for label in ['A','B']:
 p=root/('Audit Project '+label);m=json.loads((p/'.novalist/project.json').read_text());m={k.lower():v for k,v in m.items()};matches=[]
 for f in p.rglob('*'):
  if f.is_file() and f.suffix.lower() in ['.html','.md','.txt','.json','.novalist'] and marker in f.read_bytes():
   matches.append({'path_suffix':f.suffix,'relative_path_sha256':hashlib.sha256(f.relative_to(p).as_posix().encode()).hexdigest(),'file_sha256':hashlib.sha256(f.read_bytes()).hexdigest()})
 projects[label]={'project_id_sha256':hashlib.sha256(m['id'].encode()).hexdigest(),'active_book_id_sha256':hashlib.sha256(m['activebookid'].encode()).hexdigest(),'matching_marker_files':matches,'marker_file_count':len(matches)}
out=Path(sys.argv[1]);out.write_text(json.dumps({'expected_synthetic_marker_sha256':hashlib.sha256(marker).hexdigest(),'projects':projects},indent=2)+'\n');print(json.dumps(projects))
