from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import datetime, hashlib, json, subprocess, threading, time

WORK = Path(__file__).parent
SID = 'C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
APPID = 'com.novalist.app'
records = []

class Checkpoint(BaseHTTPRequestHandler):
    def do_GET(self):
        if not self.path.startswith('/checkpoint/'):
            self.send_error(404); return
        name = self.path.rsplit('/',1)[1]
        output = subprocess.check_output(['xcrun','simctl','spawn',SID,'launchctl','list'], text=True, timeout=10)
        candidates = [line.split() for line in output.splitlines() if 'UIKitApplication:com.novalist.app[' in line]
        pids = [int(parts[0]) for parts in candidates if parts[0].isdigit()]
        pid = pids[0] if len(pids) == 1 else 0
        started = subprocess.check_output(['ps','-p',str(pid),'-o','lstart='], text=True).strip() if pid else None
        record = {'checkpoint':name,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'pid':pid,'process_start':started,'native_launchctl_labels':[parts[2] for parts in candidates]}
        records.append(record)
        (WORK/'checkpoints.json').write_text(json.dumps(records,indent=2)+'\n')
        body = json.dumps(record).encode()
        self.send_response(200)
        self.send_header('Content-Type','application/json')
        self.send_header('Content-Length',str(len(body)))
        self.end_headers()
        self.wfile.write(body)
    def log_message(self, *_): pass

server = ThreadingHTTPServer(('127.0.0.1',41097), Checkpoint)
threading.Thread(target=server.serve_forever,daemon=True).start()
command = ['xcodebuild','test','-collect-test-diagnostics','never','-project',str(WORK/'AuditHarness.xcodeproj'),'-scheme','AuditUITests','-destination',f'platform=iOS Simulator,id={SID}','-derivedDataPath',str(WORK/'DerivedData'),'-parallel-testing-enabled','NO','-resultBundlePath',str(WORK/'retry.xcresult'),'CODE_SIGNING_ALLOWED=NO']
start = time.monotonic()
try:
    with (WORK/'retry.log').open('w') as log:
        process = subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT)
        (WORK/'live.json').write_text(json.dumps({'xcodebuild_pid':process.pid,'command':command},indent=2)+'\n')
        result = process.wait(timeout=360)
finally:
    server.shutdown()
duration = time.monotonic()-start
expected = ['before-first-start','first-listening','backgrounded','foreground-stopped','retry-listening','explicitly-stopped']
same_process = len(records) == len(expected) and [r['checkpoint'] for r in records] == expected and len({(r['pid'],r['process_start']) for r in records}) == 1 and records[0]['pid'] > 0
container = Path(subprocess.check_output(['xcrun','simctl','get_app_container',SID,APPID,'data'],text=True).strip())
before = json.loads((WORK/'documents-before-private.json').read_text())
after = {str(p.relative_to(container/'Documents')):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((container/'Documents').rglob('*')) if p.is_file()}
project_before = {p:h for p,h in before.items() if p.startswith('Audit Simulator Fixture/')}
project_after = {p:h for p,h in after.items() if p.startswith('Audit Simulator Fixture/')}
project_preserved = project_before == project_after
record = {'xcodebuild_exit':result,'command_seconds':duration,'checkpoint_order_exact': [r['checkpoint'] for r in records] == expected,'all_checkpoints_same_pid_and_start':same_process,'synthetic_project_files_before':len(project_before),'synthetic_project_hashes_unchanged':project_preserved,'all_original_document_hashes_unchanged':all(after.get(p)==h for p,h in before.items()),'document_file_count_after':len(after),'native_probes':False,'status':'passed' if result==0 and same_process and project_preserved else 'failed'}
(WORK/'result.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps(record,indent=2))
print('\n'.join(line for line in (WORK/'retry.log').read_text().splitlines() if 'M06_' in line or 'error:' in line or 'Test Case' in line))
raise SystemExit(0 if record['status']=='passed' else 1)
