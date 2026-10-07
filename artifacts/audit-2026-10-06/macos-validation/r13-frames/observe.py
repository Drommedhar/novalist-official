from pathlib import Path
import json,runpy,os,sys,subprocess
root=Path('<temporary-r13-frames>');repo=Path('<repository>');sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81';os.environ['NOVALIST_AUDIT_SIMULATOR']=sim
js=runpy.run_path(str(repo/'artifacts/audit-2026-10-06/macos-validation/native-probe.py'))['evaluate']
name=sys.argv[1]
expression=sys.stdin.read()
result=js(expression)
(root/(name+'.json')).write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
