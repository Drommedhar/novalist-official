from pathlib import Path
import subprocess,json,hashlib,sys
u=sys.argv[3]
p=Path(subprocess.check_output(['xcrun','simctl','get_app_container',u,'com.novalist.audit.fixtures','data'],text=True).strip())/'Documents'
parent=p/sys.argv[1]
records={}
for label in ['A','B']:
 f=parent/('Audit Project '+label)/'.novalist/project.json'
 d=json.loads(f.read_text());low={k.lower():v for k,v in d.items()};pid=low.get('id');assert pid
 books=low.get('books',[]);ids=[]
 for b in books:
  lb={k.lower():v for k,v in b.items()};ids.append(lb.get('id'))
 record={'project_id_sha256':hashlib.sha256(pid.encode()).hexdigest(),'book_ids_sha256':hashlib.sha256(json.dumps(ids,sort_keys=True).encode()).hexdigest(),'project_metadata_file_sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'book_count':len(books),'metadata_present':True}
 records[label]=record
out=Path(sys.argv[2])
out.write_text(json.dumps({'parent_fixture_basename':sys.argv[1],'old_parent_exists':(p/'FixtureParent').exists(),'moved_parent_exists':(p/'MovedParent').exists(),'projects':records},indent=2)+'\n');print(json.dumps(records))
