"""Two real simulator installs, with bookshelf-only launches between repairs."""
import hashlib, json, os, pathlib, subprocess, time
out=pathlib.Path(__file__).resolve().parent
sim=os.environ['NOVALIST_AUDIT_SIMULATOR']
app=pathlib.Path(os.environ['NOVALIST_AUDIT_APP'])
project_name='Audit Research Media'
project_id='audit-r13-project-20261007'
run=lambda *args: subprocess.check_output(args,text=True).strip()
def tree_hash(root):
    files={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}
    return hashlib.sha256(json.dumps(files,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def state():
    container=pathlib.Path(run('xcrun','simctl','get_app_container',sim,'com.novalist.app','data'))
    project=container/'Documents'/project_name
    manifest=project/'.novalist/project.json'
    registry=json.loads((container/'Library/owned-project-paths.json').read_text())
    settings=json.loads((container/'Library/settings.json').read_text())
    recent=next(x for x in settings['recentProjects'] if x['projectId']==project_id)
    metadata=json.loads(manifest.read_text())
    return container,project,registry,recent,metadata
expected_app=tree_hash(app)
records=[]
for step in range(3):
    before=pathlib.Path(run('xcrun','simctl','get_app_container',sim,'com.novalist.app','data'))
    subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],capture_output=True)
    subprocess.run(['xcrun','simctl','install',sim,str(app)],check=True)
    after=pathlib.Path(run('xcrun','simctl','get_app_container',sim,'com.novalist.app','data'))
    assert before!=after, 'No actual container relocation occurred'
    assert not before.exists(), 'Old container remains reachable'
    installed=pathlib.Path(run('xcrun','simctl','get_app_container',sim,'com.novalist.app','app'))
    assert tree_hash(installed)==expected_app, 'Installed app bytes changed'
    subprocess.run(['xcrun','simctl','launch',sim,'com.novalist.app'],check=True,capture_output=True)
    deadline=time.monotonic()+25
    while True:
        container,project,registry,recent,metadata=state()
        entry=registry.get(str(project))
        if recent['path']==str(project) and entry=={'RelativePath':project_name,'ProjectId':project_id}:break
        if time.monotonic()>deadline:raise AssertionError('Bookshelf repair did not persist both recent and exact current alias')
        time.sleep(.1)
    assert metadata['id']==project_id
    record={'step':step,'role':'establish-clean-fixed-start' if step==0 else 'first-successive-move' if step==1 else 'second-successive-move',
      'actual_container_changed':True,'old_container_removed':True,'container_token':container.name,
      'installed_app_tree_sha256':expected_app,'recent_matches_current':True,'current_exact_path_registered':True,
      'current_entry':entry,'project_id':metadata['id'],'book_ids':[x['id'] for x in metadata['books']],
      'project_metadata_sha256':hashlib.sha256((project/'.novalist/project.json').read_bytes()).hexdigest(),
      'registry_count':len(registry),'all_prior_exact_aliases_retained':all(x['path'] in registry for x in records)}
    if records:
        assert record['project_metadata_sha256']==records[0]['public']['project_metadata_sha256']
        assert record['all_prior_exact_aliases_retained']
    records.append({'path':str(project),'public':record})
    (out/'relocation-observations.json').write_text(json.dumps([x['public'] for x in records],indent=2)+'\n')
    print(json.dumps(record),flush=True)
    # No click, project open, RecordAsync call or registry/settings edit between installs.
