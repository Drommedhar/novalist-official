import ctypes as C, json, subprocess
CF=C.CDLL('/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation')
AX=C.CDLL('/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices')
P=C.c_void_p; L=C.c_long
CF.CFStringCreateWithCString.argtypes=[P,C.c_char_p,C.c_uint32];CF.CFStringCreateWithCString.restype=P
CF.CFStringGetCString.argtypes=[P,C.c_char_p,L,C.c_uint32];CF.CFStringGetCString.restype=C.c_bool
CF.CFArrayGetCount.argtypes=[P];CF.CFArrayGetCount.restype=L
CF.CFArrayGetValueAtIndex.argtypes=[P,L];CF.CFArrayGetValueAtIndex.restype=P
CF.CFGetTypeID.argtypes=[P];CF.CFGetTypeID.restype=C.c_ulong
CF.CFStringGetTypeID.restype=C.c_ulong
CF.CFBooleanGetTypeID.restype=C.c_ulong
CF.CFBooleanGetValue.argtypes=[P];CF.CFBooleanGetValue.restype=C.c_bool
AX.AXIsProcessTrusted.restype=C.c_bool
AX.AXUIElementCreateApplication.argtypes=[C.c_int];AX.AXUIElementCreateApplication.restype=P
AX.AXUIElementCopyAttributeValue.argtypes=[P,P,C.POINTER(P)];AX.AXUIElementCopyAttributeValue.restype=C.c_int

def string(s):return CF.CFStringCreateWithCString(None,s.encode(),0x08000100)
def attr(e,k):
 out=P();status=AX.AXUIElementCopyAttributeValue(e,string(k),C.byref(out));return out.value if status==0 else None

def val(v):
 if not v:return None
 t=CF.CFGetTypeID(v)
 if t==CF.CFStringGetTypeID():
  b=C.create_string_buffer(32768);CF.CFStringGetCString(v,b,len(b),0x08000100);return b.value.decode()
 if t==CF.CFBooleanGetTypeID():return CF.CFBooleanGetValue(v)
 return None

def array(v):return [CF.CFArrayGetValueAtIndex(v,i) for i in range(CF.CFArrayGetCount(v))] if v else []
def info(e,depth=0):
 result={k:val(attr(e,k)) for k in ['AXRole','AXTitle','AXIdentifier','AXDocument','AXEnabled','AXFocused','AXMain','AXDescription','AXValue','AXHelp','AXSelected']}
 if depth:result['children']=[info(c,depth-1) for c in array(attr(e,'AXChildren'))]
 return {k:v for k,v in result.items() if v is not None}
assert AX.AXIsProcessTrusted(),'Existing AX permission unavailable; no permission prompt requested.'
pids=subprocess.check_output(['pgrep','-x','DeviceHub'],text=True).split()
result=[]
for pid in pids:
 app=AX.AXUIElementCreateApplication(int(pid))
 menus=array(attr(attr(app,'AXMenuBar'),'AXChildren'))
 result.append({'pid':int(pid),'windows':[info(w,14) for w in array(attr(app,'AXWindows'))], 'menus':[info(m,3) for m in menus]})
print(json.dumps(result,indent=2))
