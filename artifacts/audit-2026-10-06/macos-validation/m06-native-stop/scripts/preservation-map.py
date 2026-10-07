from pathlib import Path
import hashlib,json,subprocess
w=Path(__file__).parent;sim='6D872EE9-8FBE-4B2D-AED4-917146C77351'
h=lambda b:hashlib.sha256(b).hexdigest()
before=json.loads((w/'documents-before.json').read_text())
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
entries={}
for relative,content_before in sorted(before.items()):
 p=data/relative;assert p.is_file(), 'Original document is absent'
 content_after=h(p.read_bytes());assert content_after==content_before,'Original document changed'
 path_hash=h(relative.encode('utf-8'));assert path_hash not in entries
 entries[path_hash]={'before_sha256':content_before,'after_sha256':content_after}
current_files=[p for p in data.rglob('*') if p.is_file()]
record={'scope':'All117 task-start Documents files; final supplementary verification is read-only and performs no app or simulator UI action.','relative_path_hash_algorithm':'SHA256 of exact Documents-relative POSIX path encoded as UTF-8; no path names are published.','content_hash_algorithm':'SHA256 of exact file bytes.','baseline_private_manifest_sha256':h((w/'documents-before.json').read_bytes()),'original_file_count':len(before),'verified_file_count':len(entries),'current_total_file_count':len(current_files),'all_original_files_present_and_byte_identical':True,'entries':entries}
(w/'preservation-hashes.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps({'original_files':len(before),'verified_files':len(entries),'current_total_files':len(current_files),'all_equal':True}))
