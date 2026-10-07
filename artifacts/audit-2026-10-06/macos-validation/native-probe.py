import base64
import json
import os
import runpy
import sys
from pathlib import Path

root = Path(__file__).resolve().parent
call = runpy.run_path(str(root / 'audit-control-client.py'))['call']
simulator = os.environ['NOVALIST_AUDIT_SIMULATOR']

def evaluate(expression):
    script = 'btoa(unescape(encodeURIComponent(JSON.stringify(' + expression + '))))'
    response = call(simulator, 'eval', script)
    if not response['ok']:
        raise RuntimeError(response)
    return json.loads(base64.b64decode(response['result']).decode())

if __name__ == '__main__':
    if len(sys.argv) > 1:
        print(json.dumps(call(simulator, sys.argv[1]), indent=2))
    else:
        print(json.dumps(evaluate(sys.stdin.read()), indent=2))
