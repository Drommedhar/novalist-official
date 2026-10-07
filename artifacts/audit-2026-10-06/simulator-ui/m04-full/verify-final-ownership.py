from pathlib import Path
import subprocess,json,re
sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81';root=Path('/private/tmp/novalist-m04-full')
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())/'Documents'
project=data/'Audit Simulator Fixture';book=project/'Audit Book Alpha';index=json.loads((book/'Drafts/default/scenes.json').read_text());scenes=[s for group in index['chapters'].values() for s in group]
meta=json.loads((project/'.novalist/project.json').read_text());rows=[]
for tag in ['EVALFIXC','TIMEOUTA','BACKENDA','WEBKITA']:
 marker='M04'+tag+'26';title='Audit M04 '+tag;task='Audit M04 task '+tag
 hits=[p for p in book.glob('Drafts/default/Chapters/**/*.novalist') if p.is_file() and marker in p.read_text()]
 assert len(hits)==1
 content=hits[0].read_text();sceneid=re.search(r'\bid=([^\s>]+)',content).group(1)
 matching=[s for s in scenes if s['id']==sceneid and s['title']==title]
 taskcount=sum(t.get('text')==task for t in meta.get('tasks',[]))
 assert len(matching)==1 and taskcount==1
 rows.append({'case':tag,'unique_active_default_draft_scene_marker':True,'scene_index_title_matches_expected':True,'task_count':taskcount})
(root/'final-disk-ownership.json').write_text(json.dumps(rows,indent=2)+'\n')
print(json.dumps(rows))
