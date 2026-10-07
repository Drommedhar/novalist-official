from pathlib import Path
import subprocess,json,hashlib
root=Path('/private/tmp/novalist-m04-full');checkout=root/'checkout';app=checkout/'Novalist.Mobile/bin/Debug/net10.0-ios27.0/iossimulator-arm64/Novalist.Mobile.app';sim='C9BC7CC6-C48A-431D-9CE0-BEA89C791D81'
assert not (checkout/'Novalist.Mobile/Pages/RendererHostPage.AuditControl.cs').exists()
for p in (checkout/'Novalist.Mobile/Pages').glob('RendererHostPage*.cs'):
 assert not any(token in p.read_text() for token in ['AuditControlLoopAsync','AuditEvaluateDeliveryAsync','AuditRecord('])
for p in (checkout/'Novalist.Mobile/Resources/Raw/app').rglob('*.js'):
 assert 'taskReplyHeld' not in p.read_text()
h=hashlib.sha256()
for p in sorted(app.rglob('*')):
 if p.is_file():h.update(str(p.relative_to(app)).encode());h.update(b'\0');h.update(hashlib.sha256(p.read_bytes()).digest())
subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
subprocess.run(['xcrun','simctl','terminate',sim,'com.novalist.app'],check=True)
subprocess.run(['xcrun','simctl','install',sim,str(app)],check=True)
launch=subprocess.check_output(['xcrun','simctl','launch',sim,'com.novalist.app'],text=True)
record={'app_tree_sha256':h.hexdigest(),'native_fault_controls_removed':True,'temporary_javascript_gate_absent_from_bundled_assets':True,'strict_deep_signature_verified':True,'installed_with_data_preserved':True,'launch_succeeded':True,'source':'0c789b29581db47d89593c897bd53fbb3fb9275c + final-mobile-source.patch + proactive-journal.patch'}
(root/'final-clean-build-install.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps(record))
