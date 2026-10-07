from pathlib import Path
import time,sys
w=Path(__file__).parent;exec((w/'ax-read.py').read_text())
AX.AXUIElementSetAttributeValue.argtypes=[P,P,P];AX.AXUIElementSetAttributeValue.restype=C.c_int
AX.AXUIElementPerformAction.argtypes=[P,P];AX.AXUIElementPerformAction.restype=C.c_int
assert AX.AXIsProcessTrusted(),'Existing AX permission required; no prompt.'
pids=subprocess.check_output(['pgrep','-x','DeviceHub'],text=True).split();assert len(pids)==1;app=AX.AXUIElementCreateApplication(int(pids[0]));windows=array(attr(app,'AXWindows'));assert len(windows)==1;window=windows[0]
def walk(e,path=()):
 yield e,path
 for child in array(attr(e,'AXChildren')):yield from walk(child,path+(e,))
udid='6D872EE9-8FBE-4B2D-AED4-917146C77351';rows=[]
for e,path in walk(window):
 if val(attr(e,'AXIdentifier'))=='TableRow.Device.'+udid:
  row=[x for x in path if val(attr(x,'AXRole'))=='AXRow'][-1]
  if row not in rows:rows.append(row)
assert len(rows)==1;row=rows[0]
if val(attr(row,'AXSelected')) is not True:
 assert AX.AXUIElementSetAttributeValue(row,string('AXSelected'),P.in_dll(CF,'kCFBooleanTrue').value)==0
 time.sleep(.5)
assert val(attr(row,'AXSelected')) is True
menu=attr(app,'AXMenuBar');sounds=[e for e,_ in walk(menu) if val(attr(e,'AXTitle'))=='Sound'];assert len(sounds)==1
items=[c for m in array(attr(sounds[0],'AXChildren')) for c in array(attr(m,'AXChildren'))]
start=next(i for i,e in enumerate(items) if val(attr(e,'AXTitle'))=='Input');end=next(i for i,e in enumerate(items) if val(attr(e,'AXTitle'))=='Output')
inputs=[e for e in items[start+1:end] if val(attr(e,'AXTitle'))];marked=[val(attr(e,'AXTitle')) for e in inputs if val(attr(e,'AXMenuItemMarkChar'))];assert len(marked)==1,marked
mode=sys.argv[1]
if mode=='none':
 assert json.loads((w/'input-before.json').read_text())['previous_simulator_input']==marked[0];(w/'input-before.json').write_text(json.dumps({'selected_simulator':udid,'previous_simulator_input':marked[0],'host_settings_or_permissions_changed':False},indent=2)+'\n');target='None'
elif mode=='restore':target=json.loads((w/'input-before.json').read_text())['previous_simulator_input']
else:target='None'
if mode in ['none','restore'] and marked[0]!=target:
 choices=[e for e in inputs if val(attr(e,'AXTitle'))==target];assert len(choices)==1
 assert AX.AXUIElementSetAttributeValue(app,string('AXFrontmost'),P.in_dll(CF,'kCFBooleanTrue').value)==0
 device=[e for e,_ in walk(menu) if val(attr(e,'AXRole'))=='AXMenuBarItem' and val(attr(e,'AXTitle'))=='Device'];assert len(device)==1
 assert AX.AXUIElementPerformAction(device[0],string('AXPress'))==0;time.sleep(.2)
 assert AX.AXUIElementPerformAction(sounds[0],string('AXPress'))==0;time.sleep(.2)
 assert AX.AXUIElementPerformAction(choices[0],string('AXPick'))==0
 time.sleep(.4)
sounds=[e for e,_ in walk(attr(app,'AXMenuBar')) if val(attr(e,'AXTitle'))=='Sound'];items=[c for m in array(attr(sounds[0],'AXChildren')) for c in array(attr(m,'AXChildren'))];start=next(i for i,e in enumerate(items) if val(attr(e,'AXTitle'))=='Input');end=next(i for i,e in enumerate(items) if val(attr(e,'AXTitle'))=='Output');inputs=[e for e in items[start+1:end] if val(attr(e,'AXTitle'))]
marked=[val(attr(e,'AXTitle')) for e in inputs if val(attr(e,'AXMenuItemMarkChar'))];assert marked==[target],marked
record={'selected_simulator':udid,'selected_row_verified':True,'selected_simulator_input':target,'input_none_verified':target=='None','only_simulator_input_control_used':True,'host_microphone_permission_or_global_audio_changes':False}
(w/('input-'+mode+'.json')).write_text(json.dumps(record,indent=2)+'\n');print(json.dumps(record))
