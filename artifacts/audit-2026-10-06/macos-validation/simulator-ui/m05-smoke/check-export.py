from pathlib import Path
from html.parser import HTMLParser
import subprocess,json,hashlib,re,sys,zipfile,datetime
class Text(HTMLParser):
 def __init__(self):super().__init__();self.data=[]
 def handle_data(self,data):self.data.append(data)
 def handle_starttag(self,tag,attrs):
  if tag in ['p','div','br']:self.data.append('\n')
 def handle_endtag(self,tag):
  if tag in ['p','div']:self.data.append('\n')
udid='4E5429FE-834B-4B50-8BBF-680A2914C06D'
def container(bundle):return Path(subprocess.check_output(['xcrun','simctl','get_app_container',udid,bundle,'data'],text=True).strip())
fixture=container('com.novalist.audit.fixtures')/'Documents'
file=fixture/'Audit Project A.epub';assert file.is_file()
with zipfile.ZipFile(file) as epub:
 assert epub.testzip() is None
 assert epub.read('mimetype')==b'application/epub+zip'
 assert 'META-INF/container.xml' in epub.namelist()
 marker='M05EXPORT20261007ALPHA'
 chapters=[(n,epub.read(n).decode()) for n in epub.namelist() if n.endswith('.xhtml') and marker.encode() in epub.read(n)]
 assert len(chapters)==1
 parser=Text();parser.feed(chapters[0][1]);text=''.join(parser.data);normalized=re.sub(r'\s+',' ',text.replace(marker,'')).strip();assert 'Synthetic M02 manuscript fixture.' in normalized
cache=container('com.novalist.app')/'Library/Caches/exports';left=[p for p in cache.rglob('*') if p.is_file()];assert len(left)==0
r={'observed_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'destination':'Separate synthetic fixture application Documents, chosen through native Save to Files','file_extension':file.suffix,'file_sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'file_bytes':file.stat().st_size,'zip_integrity':True,'epub_mimetype':'application/epub+zip','epub_container_present':True,'marker_present_in_exactly_one_chapter':True,'marker_sha256':hashlib.sha256(marker.encode()).hexdigest(),'original_imported_sentence_preserved_after_whitespace_normalization':True,'native_export_cache_files_after_completion':len(left)}
out=Path(__file__).resolve().parent/sys.argv[1];out.write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r))
