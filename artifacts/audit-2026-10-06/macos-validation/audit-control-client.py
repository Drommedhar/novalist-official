"""Temporary synthetic simulator audit client. Requires the disposable probe build."""
import json
import pathlib
import subprocess
import sys
import time
import uuid


def call(simulator, operation, script=None, timeout=30):
    container = subprocess.check_output([
        'xcrun', 'simctl', 'get_app_container', simulator, 'com.novalist.app', 'data'
    ], text=True).strip()
    channel = pathlib.Path(container) / 'Documents' / 'NovalistAudit'
    command = {'id': str(uuid.uuid4()), 'op': operation}
    if script is not None:
        command['script'] = script
    temporary = channel / 'command.tmp'
    temporary.write_text(json.dumps(command))
    temporary.replace(channel / 'command.json')
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        response_file = channel / 'response.json'
        if response_file.exists():
            response = json.loads(response_file.read_text())
            if response.get('id') == command['id']:
                return response
        time.sleep(0.1)
    raise TimeoutError('Synthetic audit response did not arrive')


if __name__ == '__main__':
    request = json.load(sys.stdin)
    response = call(sys.argv[1], request['op'], request.get('script'))
    print(json.dumps(response, indent=2))
