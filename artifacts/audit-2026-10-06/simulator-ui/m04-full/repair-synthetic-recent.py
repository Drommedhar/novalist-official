from pathlib import Path
import subprocess,json,hashlib
sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81';root=Path('/private/tmp/novalist-m04-full')
container=Path(subprocess.check_output(['xcrun','simctl','get_app_container',sim,'com.novalist.app','data'],text=True).strip())
owned=next(container.rglob('owned-project-paths.json'));registry=json.loads(owned.read_text())
settings=next(p for p in container.rglob('settings.json') if json.loads(p.read_text()).get('recentProjects'))
values=json.loads(settings.read_text());recent=values['recentProjects'][0];candidate=container/'Documents'/'Audit Simulator Fixture'
project=json.loads((candidate/'.novalist/project.json').read_text())
record={'finding':'M03','status':'failed-supporting-simulator-edge','scenario':'Two app container moves with an intervening bookshelf-only launch and no project open.','recent_path_is_registry_key':recent['path'] in registry,'recent_path_exists':Path(recent['path']).exists(),'current_candidate_exists':candidate.exists(),'candidate_project_id_matches_recent':project.get('id')==recent['projectId'],'candidate_relative_path_in_registry':any(v['RelativePath']=='Audit Simulator Fixture' and v['ProjectId']==recent['projectId'] for v in registry.values()),'recent_path_sha256':hashlib.sha256(recent['path'].encode()).hexdigest(),'current_candidate_sha256':hashlib.sha256(str(candidate).encode()).hexdigest(),'observed':'Bookshelf shows generic project card with no book detail although current owned synthetic project exists; recent now references an intermediate container URI absent from the trusted registry.'}
(root/'successive-container-edge.json').write_text(json.dumps(record,indent=2)+'\n')
subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],check=True,stdout=subprocess.DEVNULL)
(root/'synthetic-settings-before-repair.private.json').write_bytes(settings.read_bytes())
recent['path']=str(candidate);settings.write_text(json.dumps(values,indent=2))
subprocess.run(['xcrun','simctl','launch',sim,'com.novalist.app'],check=True,stdout=subprocess.DEVNULL)
print(json.dumps({'observed':record,'fixture_only_repair':'Changed only retained synthetic recent path to the current same-identity project; preserved original settings privately. No production edit.'}))
