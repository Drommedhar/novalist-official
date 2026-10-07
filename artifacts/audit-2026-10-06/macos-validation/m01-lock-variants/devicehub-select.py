from pathlib import Path
exec((Path(__file__).resolve().parent / 'inspect-simulator-ax.py').read_text().split('assert AX.AXIsProcessTrusted()')[0])
AX.AXUIElementCopyActionNames.argtypes=[P,C.POINTER(P)];AX.AXUIElementCopyActionNames.restype=C.c_int
AX.AXUIElementIsAttributeSettable.argtypes=[P,P,C.POINTER(C.c_bool)];AX.AXUIElementIsAttributeSettable.restype=C.c_int
AX.AXUIElementSetAttributeValue.argtypes=[P,P,P];AX.AXUIElementSetAttributeValue.restype=C.c_int
AX.AXUIElementPerformAction.argtypes=[P,P];AX.AXUIElementPerformAction.restype=C.c_int
identifier='TableRow.Device.4F155C1B-8F9C-4919-8BD7-1CFAD764B61B'
assert AX.AXIsProcessTrusted()
pids=subprocess.check_output(['pgrep','-x','DeviceHub'],text=True).split();assert len(pids)==1
app=AX.AXUIElementCreateApplication(int(pids[0]));windows=array(attr(app,'AXWindows'));assert len(windows)==1
window=windows[0]
def walk(e,path=()):
 yield e,path
 for child in array(attr(e,'AXChildren')):yield from walk(child,path+(e,))
targets=[]
for e,path in walk(window):
 if val(attr(e,'AXIdentifier'))==identifier:
  rows=[p for p in path if val(attr(p,'AXRole'))=='AXRow'];assert rows
  if rows[-1] not in targets:targets.append(rows[-1])
assert len(targets)==1,targets
row=targets[0];actions=P();AX.AXUIElementCopyActionNames(row,C.byref(actions));settable=C.c_bool()
status=AX.AXUIElementIsAttributeSettable(row,string('AXSelected'),C.byref(settable))
print(json.dumps({'target':identifier,'row_actions':[val(v) for v in array(actions.value)],'selected_settable':settable.value,'settable_status':status,'selected_before':val(attr(row,'AXSelected'))}))
if __import__('sys').argv[-1]=='select':
 assert status==0 and settable.value
 result=AX.AXUIElementSetAttributeValue(row,string('AXSelected'),P.in_dll(CF,'kCFBooleanTrue').value);assert result==0,result
 import time;time.sleep(0.4)
 title=val(attr(window,'AXTitle'));assert title.startswith('Novalist-Audit-iPad'),title
 print(json.dumps({'selected_after':val(attr(row,'AXSelected')),'window_title':title,'action':'select exact Max row only'}))
