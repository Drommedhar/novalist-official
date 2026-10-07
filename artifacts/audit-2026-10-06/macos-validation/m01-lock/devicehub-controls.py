from pathlib import Path
exec((Path(__file__).resolve().parent/'devicehub-select.py').read_text().split("print(json.dumps({'target'")[0])
assert val(attr(row,'AXSelected')) is True
assert val(attr(window,'AXTitle')).startswith('Novalist-Screenshots-iPhone-2026-10-07')
AX.AXUIElementSetAttributeValue(app,string('AXFrontmost'),P.in_dll(CF,'kCFBooleanTrue').value)
AX.AXUIElementPerformAction(window,string('AXRaise'))
menus=array(attr(attr(app,'AXMenuBar'),'AXChildren'));controls=[m for m in menus if val(attr(m,'AXTitle'))=='Controls'];assert len(controls)==1
assert AX.AXUIElementPerformAction(controls[0],string('AXPress'))==0
import time;time.sleep(0.15)
items=[c for menu in array(attr(controls[0],'AXChildren')) for c in array(attr(menu,'AXChildren'))]
print(json.dumps({'window':val(attr(window,'AXTitle')),'selected_target':identifier,'controls':[info(e) for e in items if val(attr(e,'AXTitle'))]},indent=2))
if __import__('sys').argv[-1]=='lock':
 matches=[e for e in items if val(attr(e,'AXTitle'))=='Lock'];assert len(matches)==1
 lock=matches[0];assert val(attr(lock,'AXEnabled')) is True
 assert val(attr(row,'AXSelected')) is True and val(attr(window,'AXTitle')).startswith('Novalist-Screenshots-iPhone-2026-10-07')
 print('SENDING_REAL_DEVICEHUB_LOCK')
 result=AX.AXUIElementPerformAction(lock,string('AXPress'));assert result==0,result
 print('REAL_DEVICEHUB_LOCK_SENT')
else:
 AX.AXUIElementPerformAction(controls[0],string('AXCancel'))
