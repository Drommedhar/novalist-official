from pathlib import Path
import json,re,hashlib
root=Path('/private/tmp/novalist-m04-full');repo=Path('<repository>');out=repo/'artifacts/audit-2026-10-06/simulator-ui/m04-full'
out.mkdir(exist_ok=True)
for p in root.iterdir():
 if not p.is_file() or p.suffix not in ['.json','.swift','.py','.js','.patch','.log'] or '.private.' in p.name:continue
 s=p.read_text()
 if p.suffix=='.log':
  start=s.find("Test Suite 'All tests' started")
  if start>=0:s=s[start:]
  s='\n'.join(line for line in s.splitlines() if not line.startswith('objc['))+'\n'
 s=s.replace(str(repo),'<repository>').replace('<host-user>','<host-user>')
 s=s.replace('<disposable-checkout>/','<disposable-checkout>/').replace('<temporary-harness>/','<temporary-harness>/').replace('<temporary-harness>/','<temporary-harness>/')
 s=s.replace('<temporary-m04>/','<temporary-m04>/').replace('<temporary-m04>/','<temporary-m04>/')
 (out/p.name).write_text(s)
finalpatch=(repo/'artifacts/audit-2026-10-06/macos-validation/final-mobile-source.patch').read_bytes()+(root/'proactive-journal.patch').read_bytes()
(out/'final-effective-source.patch').write_bytes(finalpatch)
rows=[]
tags=['EVALFIXC','TIMEOUTA','BACKENDA','WEBKITA']
for tag in tags:
 record=json.loads((root/(tag+'.json')).read_text())
 assert record['status']=='passed' and record['xcodebuild_exit_code']==0 and record['healthyAfterReload']
 before=record.get('beforeReload',record.get('beforeReloadDisk'));after=record['afterReload']
 assert before['taskCountOnDisk']==1 and after['taskCountOnDisk']==1
 assert after['markerInEditor'] and after['markerOnDisk'] and not after['journalContainsMarker'] and after['replay'] is None
 assert record['beforeArm']['replay']['taskRequestCount']==1 and record['beforeArm']['replay']['taskReplyHeld']==1
 native=record['nativeFailureState'];assert native['nativeEditingPausedAlert'] and native['nativeReloadAvailable'] and native['nativeLaterAvailable']
 if tag!='WEBKITA':assert before['replay']['backendRejected']==1
 else:assert any(e['kind']=='content-process-did-terminate' for e in native['events'])
 clean=json.loads((root/(tag+'-clean-source.json')).read_text());assert not clean['audit_control_source_present'] and clean['bin_obj_cleared']
 cleanlog=(root/(tag+'-clean-build.log')).read_text();assert '0 Fehler' in cleanlog
 testlog=(root/(tag+'.log')).read_text();seconds=float(re.search(r'passed \(([\d.]+) seconds\)',testlog).group(1))
 fail=next(e for e in native['events'] if e['kind']=='bridge-failed');alert=next(e for e in native['events'] if e['kind']=='native-alert-presenting')
 row={'case':record['case'],'status':'passed','test_seconds':seconds,'native_failure_to_alert_seconds':(alert['ticks']-fail['ticks'])/1e9,'source':tag+'.swift','log':tag+'.log','observations':tag+'.json','clean_probe_removal':tag+'-clean-source.json','clean_build':tag+'-clean-build.log'}
 if tag=='TIMEOUTA':row['delivery_deadline_seconds']=record['evaluator_deadline_seconds']
 if tag in ['BACKENDA','WEBKITA']:assert record['immediatelyBeforeFault']['journalSourceKinds']==['manuscript'];row['editor_surface']='full Manuscript, actual landscape UI'
 else:row['editor_surface']='scene, actual portrait UI'
 rows.append(row)
sequence=[]
for previous,next_tag in zip(tags,tags[1:]):
 before=root/(previous+'-clean-build.log');after=root/(next_tag+'-instrumented-source.json')
 assert before.stat().st_mtime<after.stat().st_mtime
 sequence.append({'after_case':previous,'before_case':next_tag,'clean_build_completed_before_next_probe_applied':True})
summary={'finding':'M04','status':'passed-simulator-acceptance','device':'iPhone17ProMax simulator','os':'iOS27.0','baseline_revision':'0c789b29581db47d89593c897bd53fbb3fb9275c','final_source_patch':'final-effective-source.patch','final_source_patch_sha256':hashlib.sha256(finalpatch).hexdigest(),'journal_fix_identity':'proactive-journal-source.json','native_probe_identity':'setup.json','cases':rows,'independent_case_cleanup_order':sequence,'final_cleanup':'final-clean-build-install.json','final_clean_ui_source':'final-clean-smoke.swift','final_clean_ui_log':'final-clean-smoke.log','disk_ownership':'final-disk-ownership.json','regression_found_and_fixed':'evaluator-loss-summary.json','mutation_nonreplay':'Each case used a real To do UI mutation. Backend committed exactlyone task while a temporary receive gate withheld its actual reply, keeping production RPC pending. Native failure and actual Reload did not replay it: exactlyone task persisted and appeared in actual UI afterward.','unsaved_recovery':'EvaluatorNSError case recovered a real scene marker that was absent on disk beforeReload. ActualWebContentdeath case recovered a real fullManuscript marker absent on disk beforekill and beforeReload. Both markers were in the genuine journal and persisted afterReload, then journal entries cleared.','limitations':['Timeout and backend-pump cases began with unsaved/unacknowledged marker but normal backend save reached disk beforeReload; they demonstrate retention without duplicate task replay, not missing-disk journal restoration.','The killed JavaScript context cannot report Promise rejection; its associated process termination, native alert, new healthy page and nonreplay were observed.','WebContentcase installed a temporary outgoing scenes/write gate for determinism; counter was0 at prekill sample. No claim that this gate was consumed. Marker remained absent on disk through nativeReload.','Earlier EVALFIXC inertElementPresent field included unrelated hidden motion nodes; editingpause evidence for that case is actual modal alert plus rejection. Latercases record exact workspaceInert transition false→true→false.','EVALFIXB stopped beforefault because consecutive container moves exposed a separateM03 recent-path registry gap; synthetic-only recentpath repair and originalsettingsprivatebackup documented.','No physical-device acceptance claimed. Archived scripts redact only repository/temporary host paths; production source patches and executed Swift/JavaScript test logic retained.']}
(out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print(json.dumps({'M04':'all5requiredsimulatorcasesproven','cases':len(rows),'final_source_patch_sha256':summary['final_source_patch_sha256']}))
