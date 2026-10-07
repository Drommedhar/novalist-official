from pathlib import Path
from html.parser import HTMLParser
import subprocess,json,hashlib,re,sys
class Text(HTMLParser):
 def __init__(self):super().__init__();self.data=[]
 def handle_data(self,data):self.data.append(data)
 def handle_starttag(self,tag,attrs):
  if tag in ['p','div','br']:self.data.append('\n')
 def handle_endtag(self,tag):
  if tag in ['p','div']:self.data.append('\n')
base=Path(subprocess.check_output(['xcrun','simctl','get_app_container','4E5429FE-834B-4B50-8BBF-680A2914C06D','com.novalist.audit.fixtures','data'],text=True).strip())/'Documents/MovedParent/Audit Project A'
marker='M05EXPORT20261007ALPHA';files=[f for f in base.rglob('*.novalist') if f.is_file() and marker in f.read_text()];assert len(files)==1
content=files[0].read_bytes();parser=Text();parser.feed(content.decode());text=''.join(parser.data);original=re.sub(r'\s+',' ',text.replace(marker,'')).strip();assert original=='Synthetic M02 manuscript fixture.'
result={'marker_sha256':hashlib.sha256(marker.encode()).hexdigest(),'matching_scene_count':1,'original_imported_text_preserved_after_whitespace_normalization':True,'scene_sha256':hashlib.sha256(content).hexdigest(),'scene_bytes':len(content)}
out=Path(__file__).resolve().parent/sys.argv[1];out.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))
