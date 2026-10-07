from pathlib import Path
import sys,json,subprocess,shutil,time,runpy,datetime
from control import w,sid,js,canonical
lib=runpy.run_path(str(w/'scope-lib.py'));kind=sys.argv[1];c=lib['config'](kind);observe=lib['observe'];record={'source_revision':'560839ac27746ec31c948d19c91ae3813eb5ec37','kind':kind,'config':c,'status':'running','baseline_disk':lib['disk'](kind)}
assert not any(x['marker_count'] for x in record['baseline_disk'])
def save():(w/(kind+'-scope.json')).write_text(json.dumps(record,indent=2)+'\n')
name=kind+'-edit';log=w/(name+'.log');shutil.copyfile(w/(name+'.swift'),w/'harness/AuditUITests.swift');save()
try:
 with log.open('w') as f:
  p=subprocess.Popen(['xcodebuild','test','-project',str(w/'harness/AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination','platform=iOS Simulator,id='+sid,'-derivedDataPath',str(w/'harness/DerivedData'),'-parallel-testing-enabled','NO','-collect-test-diagnostics','never','-resultBundlePath',str(w/(name+'.xcresult')),'CODE_SIGNING_ALLOWED=NO'],stdout=f,stderr=subprocess.STDOUT)
  end=time.monotonic()+90
  for flag in ['M01_SCOPE_GATE_READY','M01_SCOPE_EDIT_DONE']:
   while flag not in log.read_text():
    if p.poll() is not None:raise RuntimeError('Native test ended before '+flag)
    if time.monotonic()>end:raise TimeoutError(flag)
    time.sleep(.04)
   if flag=='M01_SCOPE_GATE_READY':record['gate_installed']=js((w/'held-write-gate.js').read_text().replace('__CONFIG__',json.dumps(c)));save()
  record['edit_observed_at']=datetime.datetime.now(datetime.timezone.utc).isoformat();record['native_edit_exit']=p.wait(timeout=30);assert p.returncode==0
  end=time.monotonic()+8
  while time.monotonic()<end:
   state=observe(kind)
   if state['gate']['held']:break
   time.sleep(.05)
  record['held_edit']=state;save();assert len(state['journal'])==1 and state[kind]['domMarker'] and not any(e['marker_count'] for e in state['disk'])
  original=lib['scope_for'](c['path'],c['bookId'],c['draftId']);assert state['scope']==original and state['journal'][0]['scope']==original
  assert state['gate']['held'] and all(e['marker'] and e['scopeAtWrite']==original for e in state['gate']['held'])
  assert state['journal'][0]['source']==('manuscript:'+c['sceneId'] if kind=='manuscript' else c['researchId'])
  if kind=='manuscript':assert any(e['sceneId']==c['sceneId'] and e['dirty'] for e in state['gate']['editingClaims'])
  record['terminated_at']=datetime.datetime.now(datetime.timezone.utc).isoformat();subprocess.run(['xcrun','simctl','terminate',sid,'com.novalist.app'],check=True);assert not any(e['marker_count'] for e in lib['disk'](kind))
  meta=Path(c['path'])/'.novalist/project.json';data=json.loads(meta.read_text());book=next(b for b in data['books'] if b['id']==c['bookId']);before={'activeBookId':data['activeBookId'],'activeDraftId':book['activeDraftId']};data['activeBookId']=c['otherBookId'];book['activeDraftId']=c['otherDraftId'];meta.write_text(json.dumps(data,indent=2)+'\n')
  record['stopped_fixture_activation']={'before':before,'after':{'activeBookId':data['activeBookId'],'activeDraftId':book['activeDraftId']},'only_fixture_active_pointers_changed':True,'journal_and_registry_changed':False};record['status']='held-native-edit-terminated-awaiting-scope-probes';save()
except Exception as error:
 record['status']='failed';record['error']=type(error).__name__+': '+str(error);save();raise
print(record['status'])
