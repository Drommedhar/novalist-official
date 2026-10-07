from pathlib import Path
import subprocess,shutil,json,hashlib,uuid,zlib,struct
root=Path('<temporary-r13-frames>');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
app=root/'checkout/Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app'
subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
subprocess.run(['xcrun','simctl','install',sim,str(app)],check=True)
data=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())
p=data/'Documents/Audit Simulator Fixture';book=p/'Audit Book Alpha';draft=book/'Drafts/default'
# New synthetic fixture only, while native app is stopped.
image=book/'Images/audit-quadrants.png';image.parent.mkdir(exist_ok=True)
colors=[(240,25,35),(25,220,55),(25,55,235),(245,225,20)]
raw=b''.join(b'\0'+b''.join(bytes(colors[(y//128)*2+x//128]) for x in range(256)) for y in range(256))
def chunk(kind,body):return struct.pack('>I',len(body))+kind+body+struct.pack('>I',zlib.crc32(kind+body))
image.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',256,256,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))
scenes=json.loads((draft/'scenes.json').read_text());chapter=next(iter(scenes['chapters']));items=scenes['chapters'][chapter];sid=str(uuid.uuid4());filename='scene-r13-image.novalist'
items.append({'id':sid,'title':'Audit R13 Frame Image','order':len(items)+1,'fileName':filename,'chapterGuid':chapter,'wordCount':4,'excludeFromExport':False})
(draft/'scenes.json').write_text(json.dumps(scenes,indent=2))
scene=next(draft.glob('Chapters/*'))/filename
scene.write_text('<!--nv v=1 id='+sid+' -->\n<p>Audit nested scene image</p><p><img src="Images/audit-quadrants.png" data-nv-src="Images/audit-quadrants.png" alt="Audit red green blue yellow quadrants" width="256" height="256"></p>')
mid='map-audit-r13-frames';maps=draft/'Maps';maps.mkdir(exist_ok=True)
mapfile=maps/(mid+'.json')
mapdata={'id':mid,'name':'Audit R13 Quadrants','fileName':mid+'.json','version':2,'layers':[{'id':'audit-image-layer','name':'Audit Image','opacity':1,'locked':False,'hidden':False,'expanded':True,'images':[{'id':'audit-quadrants','path':'Images/audit-quadrants.png','x':0,'y':0,'width':600,'height':600,'rotation':0}],'children':[]}],'pins':[],'labels':[],'initialView':{'zoom':1,'panX':0,'panY':0}}
mapfile.write_text(json.dumps(mapdata,indent=2))
project=json.loads((p/'.novalist/project.json').read_text());project['books'][0]['maps'].append({'id':mid,'name':mapdata['name'],'fileName':mid+'.json','createdAt':'2026-10-07T10:00:00Z'});(p/'.novalist/project.json').write_text(json.dumps(project,indent=2))
record={'fixture':'synthetic quadrants; scene and map fixture added while app stopped','image_dimensions':[256,256],'expected_quadrants_rgb':colors,'files':{str(f.relative_to(p)):hashlib.sha256(f.read_bytes()).hexdigest() for f in [image,scene,mapfile]},'scene_relative_reference':'Images/audit-quadrants.png','map_book_relative_reference':'Images/audit-quadrants.png','prior_synthetic_data_backed_up':True}
(root/'fixture-before.json').write_text(json.dumps(record,indent=2)+'\n')
subprocess.run(['xcrun','simctl','launch',sim,'com.novalist.app'],check=True)
print(json.dumps(record,indent=2))
h=root/'harness';shutil.copytree('/private/tmp/novalist-audit-xctest/AuditHarness.xcodeproj',h/'AuditHarness.xcodeproj',dirs_exist_ok=True)
