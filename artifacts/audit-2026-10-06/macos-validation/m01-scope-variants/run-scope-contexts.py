from pathlib import Path
import sys,json,runpy,time,subprocess,datetime
from control import w,sid,canonical
lib=runpy.run_path(str(w/'scope-lib.py'));kind=sys.argv[1];c=lib['config'](kind);observe,show,nav,native=lib['observe'],lib['show'],lib['nav'],lib['native'];p=w/(kind+'-scope.json');r=json.loads(p.read_text());assert r['status']=='held-native-edit-terminated-awaiting-scope-probes'
original=lib['scope_for'](c['path'],c['bookId'],c['draftId']);expected_journal=r['held_edit']['journal_hash'];baseline=r['baseline_disk'];r['wrong_contexts']=[]
def save():p.write_text(json.dumps(r,indent=2)+'\n')
def assert_unrecovered(scope,label):
 first=show(kind);time.sleep(1.1);last=observe(kind)
 for o in [first,last]:
  assert o['scope']==scope,(label,o['scope'],scope)
  assert not o['workspaceBusy'] and o['gate'] is None
  assert len(o['journal'])==1 and o['journal_hash']==expected_journal
  assert o[kind]['present'] and not o[kind]['domMarker']
  assert o['disk']==baseline,(label,'Disk payload changed')
  if kind=='manuscript':assert len(o[kind]['storeMatches'])==1 and not o[kind]['storeMatches'][0]['marker']
 r['wrong_contexts'].append({'label':label,'first':first,'stable_after_1_1_seconds':last,'same_journal_hash':expected_journal});save()
try:
 r['wrong_book_native_test']=native(kind+'-wrong-book');save()
 assert_unrecovered(lib['scope_for'](c['path'],c['otherBookId'],'draft-default'),'same-project-other-book')
 nav('novalistStores.project.getState().openProject('+json.dumps(c['copyPath'])+','+json.dumps(c['bookId'])+')')
 assert_unrecovered(lib['scope_for'](c['copyPath'],c['bookId'],c['draftId']),'other-project-same-book-draft-resource-ids')
 nav('novalistStores.project.getState().openProject('+json.dumps(c['path'])+','+json.dumps(c['bookId'])+')')
 assert_unrecovered(lib['scope_for'](c['path'],c['bookId'],c['otherDraftId']),'same-project-book-other-draft')
 nav('novalistStores.project.getState().switchDraft('+json.dumps(c['draftId'])+')')
 end=time.monotonic()+10
 while time.monotonic()<end:
  correct=observe(kind)
  if not correct['journal'] and any(x['marker_count'] for x in correct['disk']):break
  time.sleep(.1)
 assert correct['scope']==original and not correct['journal'];marked=[x for x in correct['disk'] if x['marker_count']];assert len(marked)==1 and marked[0]['marker_count']==1
 if kind=='manuscript':assert marked[0]['path'].startswith('Audit M01 Scope Variants/'+c['bookFolder']+'/Drafts/'+c['draftFolder']+'/')
 else:assert marked[0]['projectId']==c['projectId'] and marked[0]['researchId']==c['researchId'] and marked[0]['item_count']==1 and marked[0]['payload']['title']==c['researchTitle']
 visible=show(kind);assert visible[kind]['domMarker'] and not visible['journal'];r['returned_to_exact_owner']=visible;r['recovery_native_test']=native(kind+'-recovery');save()
 subprocess.run(['xcrun','simctl','io',sid,'screenshot',str(w/(kind+'-recovered.png'))],check=True,capture_output=True)
 if kind=='research':
  r['same_project_shared_visibility']=[]
  for label,expr,scope in [
   ('other-book','novalistStores.project.getState().switchBook('+json.dumps(c['otherBookId'])+')',lib['scope_for'](c['path'],c['otherBookId'],'draft-default')),
   ('other-draft','novalistStores.project.getState().switchBook('+json.dumps(c['bookId'])+'); await novalistStores.project.getState().switchDraft('+json.dumps(c['otherDraftId'])+')',lib['scope_for'](c['path'],c['bookId'],c['otherDraftId']))]:
   nav(expr);o=show(kind);assert o['scope']==scope and o[kind]['domMarker'] and not o['journal'];assert o['disk']==visible['disk'];r['same_project_shared_visibility'].append({'label':label,'observation':o});save()
  nav('novalistStores.project.getState().switchDraft('+json.dumps(c['draftId'])+')');assert show(kind)['scope']==original
 r['status']='passed-simulator-supporting-scope-isolation';r['completed_at']=datetime.datetime.now(datetime.timezone.utc).isoformat();save();print(r['status'])
except Exception as error:
 r['status']='failed';r['error']=type(error).__name__+': '+str(error)
 try:r['failure_observation']=observe(kind)
 except Exception as probe:r['observer_error']=str(probe)
 save();raise
