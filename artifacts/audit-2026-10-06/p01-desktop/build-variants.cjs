const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '../../..');
const app = path.join(root, 'app');
const sha = value => crypto.createHash('sha256').update(value).digest('hex');
const auditedRevision = 'ba8b6d328b64ded6e923baa25ce086d7e9e799e4';
const original = execFileSync('git', ['show', `${auditedRevision}:app/build/manual-plugin.ts`], { cwd: root });
const fixed = fs.readFileSync(path.join(app, 'build/manual-plugin.ts'));
const metadata = { date: new Date().toISOString(), auditedRevision, head: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(), variants: {} };
for (const [variant, source] of [['original', original], ['fixed', fixed]]) {
  const dir = path.join(__dirname, variant);
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'manual-plugin.ts'), source);
  fs.cpSync(path.join(app, 'out/main'), path.join(dir, 'main'), { recursive: true });
  fs.cpSync(path.join(app, 'out/preload'), path.join(dir, 'preload'), { recursive: true });
  const literal = value => JSON.stringify(value.replaceAll('\\', '/'));
  fs.writeFileSync(path.join(dir, 'electron.vite.config.ts'), `import react from ${literal(path.join(app, 'node_modules/@vitejs/plugin-react/dist/index.js'))}\nimport { manualPlugin } from './manual-plugin'\nexport default { renderer: { root: ${literal(path.join(app, 'src/renderer'))}, plugins: [react(), manualPlugin(${literal(root)})], build: { outDir: ${literal(path.join(dir, 'renderer'))} } } }\n`);
  const output = execFileSync(process.execPath, [path.join(app, 'node_modules/electron-vite/bin/electron-vite.js'), 'build', '--config', path.join(dir, 'electron.vite.config.ts')], { cwd: app, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024 });
  fs.writeFileSync(path.join(dir, 'build.log'), output);
  const assets = fs.readdirSync(path.join(dir, 'renderer/assets'));
  const entry = assets.find(file => /^index-.*\.js$/.test(file));
  const bytes = fs.readFileSync(path.join(dir, 'renderer/assets', entry));
  metadata.variants[variant] = { pluginSha256: sha(source), mainSha256: sha(fs.readFileSync(path.join(dir, 'main/index.js'))), preloadSha256: sha(fs.readFileSync(path.join(dir, 'preload/index.js'))), entry, entryBytes: bytes.length, entrySha256: sha(bytes), emittedPng: assets.filter(file => file.endsWith('.png')) };
  process.stdout.write(`${variant} ${bytes.length}\n`);
}
fs.writeFileSync(path.join(__dirname, 'builds.json'), JSON.stringify(metadata, null, 2));
