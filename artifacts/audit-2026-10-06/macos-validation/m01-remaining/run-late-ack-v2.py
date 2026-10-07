from pathlib import Path
import os, runpy, subprocess, time, json, hashlib, shutil

repo = Path('<repository>')
work = Path('<audit-scratch>')
sim = 'C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
os.environ['NOVALIST_AUDIT_SIMULATOR'] = sim
probe = runpy.run_path(str(repo / 'artifacts/audit-2026-10-06/macos-validation/native-probe.py'))
call, js = probe['call'], probe['evaluate']
data = Path(subprocess.check_output(['xcrun', 'simctl', 'get_app_container', sim, 'com.novalist.app', 'data'], text=True).strip()) / 'Documents'
old, new = 'M01OLDC26', 'M01NEWD26'
record = {'source': 'source.json', 'scenario': 'M01-10 older acknowledgement with newer UI edit, followed by M01-05 interrupted background flush', 'simulator': sim, 'old_marker': old, 'new_marker': new}

def save():
    (work / 'late-ack-v2.json').write_text(json.dumps(record, indent=2) + '\n')

def disk():
    result = []
    for path in data.rglob('*.novalist'):
        if not path.is_file(): continue
        raw = path.read_bytes()
        if old.encode() in raw or new.encode() in raw:
            result.append({'relative_path': str(path.relative_to(data)), 'sha256': hashlib.sha256(raw).hexdigest(), 'old': old.encode() in raw, 'new': new.encode() in raw})
    return result

def observation():
    expression = '''(() => {
      const old='M01OLDC26', newer='M01NEWD26';
      const store=window.novalistStores.project.getState();
      const journal=JSON.parse(localStorage.getItem('novalist.mobile.recovery.v1')||'[]');
      return {
        journal: journal.filter(e=>(e.html||'').includes(old)).map(e=>({scope:e.scope,source:e.source,sceneId:e.sceneId,chapterGuid:e.chapterGuid,old:e.html.includes(old),new:e.html.includes(newer),hash:e.hash})),
        editors:Object.entries(store.editors).filter(([id,e])=>(e.html||'').includes(old)).map(([id,e])=>({paneId:id,isDirty:e.isDirty,dirtyMap:store.dirtyMap[e.sceneId],old:e.html.includes(old),new:e.html.includes(newer),sceneId:e.sceneId,chapterGuid:e.chapterGuid,hash:e.hash,bookId:store.activeBookId,draftId:store.activeDraftId,projectPath:store.projectPath})),
        editorContainsNew:Array.from(document.querySelectorAll('iframe')).some(f=>f.contentDocument?.body.innerText.includes(newer)),
        gate:window.auditM01?{firstId:auditM01.firstId,repliesHeld:auditM01.repliesHeld,writesHeld:auditM01.writesHeld,firstReplyReleased:auditM01.firstReplyReleased,writes:auditM01.writes.map(w=>({id:w.id,chapterGuid:w.params[0],sceneId:w.params[1],old:JSON.stringify(w.params).includes(old),new:JSON.stringify(w.params).includes(newer)}))}:null
      };
    })()'''
    return {**js(expression), 'disk': disk()}

assert not disk(), 'Fresh markers already exist'
state = call(sim, 'state')['result']
assert state['rendererReady'] and not state['bridgeFailed']
shutil.copy2(work / 'late-ack-v2.swift', work / 'harness/AuditUITests.swift')
log = work / 'late-ack-v2.log'
with log.open('w') as output:
    process = subprocess.Popen([
        'xcodebuild', 'test', '-project', str(work / 'harness/AuditHarness.xcodeproj'), '-scheme', 'AuditUITests',
        '-destination', 'platform=iOS Simulator,id=' + sim, '-derivedDataPath', str(work / 'harness/DerivedData'),
        '-parallel-testing-enabled', 'NO', '-collect-test-diagnostics', 'never', '-resultBundlePath', str(work / 'late-ack-v2.xcresult'),
        'CODE_SIGNING_ALLOWED=NO'
    ], stdout=output, stderr=subprocess.STDOUT)
    record['xcodebuild_pid'] = process.pid; save()

    def waitlog(marker, timeout=60):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            text = log.read_text()
            if marker in text: return
            if process.poll() is not None or "Test Suite 'All tests' failed" in text:
                raise RuntimeError('XCTest exited before ' + marker)
            time.sleep(0.03)
        raise TimeoutError(marker)

    try:
        waitlog('M01_READY_GATE')
        assert js((work / 'late-ack-gate.js').read_text())
        waitlog('M01_OLD_TYPED')
        deadline = time.monotonic() + 4.5
        while time.monotonic() < deadline:
            snapshot = observation()
            if snapshot['gate']['repliesHeld'] == 1: break
            time.sleep(0.08)
        record['old_reply_held'] = snapshot; save()
        assert snapshot['gate']['repliesHeld'] == 1
        assert len(snapshot['disk']) == 1 and snapshot['disk'][0]['old'] and not snapshot['disk'][0]['new']
        waitlog('M01_NEW_TYPED')
        record['new_before_old_ack'] = observation(); save()
        assert any(e['new'] for e in record['new_before_old_ack']['journal'])
        assert all(e['isDirty'] and e['dirtyMap'] for e in record['new_before_old_ack']['editors'])
        assert js('window.auditM01.releaseFirst()')
        deadline = time.monotonic() + 4
        while time.monotonic() < deadline:
            snapshot = observation()
            if snapshot['gate']['writesHeld'] > 0: break
            time.sleep(0.08)
        record['after_old_ack'] = snapshot; save()
        assert snapshot['gate']['firstReplyReleased'] and snapshot['gate']['writesHeld'] > 0
        assert snapshot['editorContainsNew'] and any(e['new'] for e in snapshot['journal'])
        assert len(snapshot['editors'])==1 and all(e['isDirty'] and e['dirtyMap'] for e in snapshot['editors'])
        entry,editor=snapshot['journal'][0],snapshot['editors'][0]
        assert entry['sceneId']==editor['sceneId']==snapshot['gate']['writes'][0]['sceneId']==snapshot['gate']['writes'][1]['sceneId']
        assert entry['chapterGuid']==editor['chapterGuid']==snapshot['gate']['writes'][0]['chapterGuid']
        assert entry['scope']=={'projectPath':editor['projectPath'],'bookId':editor['bookId'],'draftId':editor['draftId']}
        native_before=call(sim,'state')['result']; record['native_before_home']=native_before
        baseline_ticks=max(e['ticks'] for e in native_before['events'])
        assert not any(e['new'] for e in snapshot['disk'])
        waitlog('M01_BEFORE_HOME')
        deadline = time.monotonic() + 1.2
        while time.monotonic() < deadline:
            native = call(sim, 'state')['result']
            if any(e['kind']=='background-task' and e['ticks']>baseline_ticks for e in native['events']): break
            time.sleep(0.05)
        record['native_background_state'] = native
        fresh_events=[e for e in native['events'] if e['ticks']>baseline_ticks]
        assert any(e['kind']=='background-task' and e['detail']=='granted' for e in fresh_events)
        assert not any(e['kind']=='background-acknowledgement' for e in fresh_events)
        save()
        waitlog('M01_TERMINATED')
        record['disk_after_termination'] = disk(); save()
        assert not any(e['new'] for e in record['disk_after_termination'])
        waitlog('M01_REOPENED_NEW_BUFFER_PASS')
        record['after_relaunch'] = observation(); save()
        assert len(record['after_relaunch']['disk']) == 1 and record['after_relaunch']['disk'][0]['new']
        assert not record['after_relaunch']['journal']
        assert len(record['after_relaunch']['editors'])==1 and not record['after_relaunch']['editors'][0]['isDirty']
        assert record['after_relaunch']['editors'][0]['sceneId']==editor['sceneId']
        record['xcodebuild_exit_code'] = process.wait(timeout=60)
        assert record['xcodebuild_exit_code'] == 0
        record['status'] = 'passed-simulator-supporting-acceptance'
    except Exception as error:
        record['status'] = 'failed'; record['error'] = type(error).__name__ + ': ' + str(error)
        try: record['failure_observation'] = observation()
        except Exception as probe_error: record['probe_error'] = type(probe_error).__name__
        save(); raise
save()
print(json.dumps({'status': record['status'], 'after_old_ack': record['after_old_ack'], 'after_relaunch': record['after_relaunch']}, indent=2))
