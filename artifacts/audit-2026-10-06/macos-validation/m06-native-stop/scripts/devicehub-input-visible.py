from pathlib import Path
import time
w=Path(__file__).parent;exec((w/'devicehub-input.py').read_text().split('mode=sys.argv[1]')[0])
assert marked==['Use System Settings']
assert AX.AXUIElementSetAttributeValue(app,string('AXFrontmost'),P.in_dll(CF,'kCFBooleanTrue').value)==0
menus=attr(app,'AXMenuBar');device=[e for e,_ in walk(menus) if val(attr(e,'AXRole'))=='AXMenuBarItem' and val(attr(e,'AXTitle'))=='Device'];assert len(device)==1
assert AX.AXUIElementPerformAction(device[0],string('AXPress'))==0;time.sleep(.2)
assert AX.AXUIElementPerformAction(sounds[0],string('AXPress'))==0;time.sleep(.2)
choices=[e for e in inputs if val(attr(e,'AXTitle'))=='None'];assert len(choices)==1
class Pair(C.Structure):_fields_=[('x',C.c_double),('y',C.c_double)]
AX.AXValueGetValue.argtypes=[P,C.c_int,P];AX.AXValueGetValue.restype=C.c_bool
position=Pair();size=Pair();assert AX.AXValueGetValue(attr(choices[0],'AXPosition'),1,C.byref(position));assert AX.AXValueGetValue(attr(choices[0],'AXSize'),2,C.byref(size));assert size.x>0 and size.y>0
x=int(position.x+size.x/2);y=int(position.y+size.y/2);print(json.dumps({'only_selected_simulator':udid,'visible_input_none_control_center':[x,y],'control_size':[size.x,size.y]}),flush=True)
subprocess.run(['/opt/homebrew/bin/cliclick','c:'+str(x)+','+str(y)],check=True);time.sleep(.5)
