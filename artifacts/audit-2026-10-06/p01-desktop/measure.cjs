const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const root = path.resolve(__dirname, '../../..');
const { _electron: electron } = require(path.join(root, 'app/node_modules/@playwright/test'));
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
async function run(variant, label, trace = false) {
  const dir = path.join(__dirname, 'runs', label);
  fs.mkdirSync(dir, { recursive: true });
  if (fs.existsSync(path.join(dir, 'profile'))) throw new Error('A fresh profile is required: ' + label);
  const env = { ...process.env, P01_VARIANT: variant, NOVALIST_NO_SPLASH: '1', NOVALIST_NO_CLIPBOARD: '1', NOVALIST_EXTENSIONS_DISABLED: '1', NOVALIST_SETTINGS_DIR: path.join(dir, 'settings'), NOVALIST_BACKEND_PATH: path.join(root, 'Novalist.Backend/bin/Debug/net8.0/Novalist.Backend.exe'), NODE_PATH: path.join(root, 'app/node_modules') };
  delete env.ELECTRON_RUN_AS_NODE;
  if (trace) env.P01_TRACE = '1';
  const externalStart = performance.now();
  const app = await electron.launch({ executablePath: path.join(root, 'app/node_modules/electron/dist/electron.exe'), args: [path.join(__dirname, 'probe-main.cjs'), `--user-data-dir=${path.join(dir, 'profile')}`], env, timeout: 30000 });
  let result;
  try {
    const page = await app.firstWindow();
    await page.waitForFunction(() => !!window.novalistStores?.shell.getState().backendVersion, undefined, { timeout: 30000 });
    for (let count = 0; count < 600; count++) {
      if (await app.evaluate(() => !!global.__p01.renderer)) break;
      await delay(50);
    }
    const observedReadyMs = performance.now() - externalStart;
    await delay(2000);
    result = await app.evaluate(() => { const p = global.__p01; clearInterval(p.timer); const { timer, ...value } = p; return value; });
    if (!result.renderer) throw new Error('Renderer readiness was not recorded');
    result.observedExternalReadyMs = observedReadyMs;
    result.variant = variant;
    result.label = label;
    result.traced = trace;
    result.versions = await app.evaluate(() => process.versions);
    if (trace) await app.evaluate(async ({ contentTracing }, file) => contentTracing.stopRecording(file), path.join(dir, 'trace.json'));
    const processes = result.samples.flatMap(sample => sample.processes).filter(p => p.pid === result.rendererPid);
    result.rendererPeakWorkingSetKiB = Math.max(...processes.map(p => p.memory.peakWorkingSetSize));
    result.rendererMaxSampledPrivateKiB = Math.max(...processes.map(p => p.memory.privateBytes));
    result.electronMaxSampledWorkingSetKiB = Math.max(...result.samples.map(s => s.processes.reduce((sum, p) => sum + p.memory.workingSetSize, 0)));
    fs.writeFileSync(path.join(dir, 'metrics.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify({ variant, label, ready: result.events.shellAndBackendReadyMs, rendererReady: result.renderer.readyMs, peakKiB: result.rendererPeakWorkingSetKiB, privateKiB: result.rendererMaxSampledPrivateKiB }));
  } finally {
    await app.evaluate(({ app, BrowserWindow }) => {
      app.removeAllListeners('window-all-closed');
      const quit = app.quit.bind(app);
      app.quit = () => { setImmediate(quit); };
      for (const win of BrowserWindow.getAllWindows()) win.destroy();
    }).catch(() => {});
    await app.close();
  }
  return result;
}
async function main() {
  const mode = process.argv[2] || 'pilot';
  const output = { date: new Date().toISOString(), mode, machine: { platform: os.platform(), release: os.release(), architecture: os.arch(), cpu: os.cpus()[0]?.model, logicalProcessors: os.cpus().length, memoryBytes: os.totalmem() }, runs: [] };
  const repetitions = mode === 'pilot' ? 1 : mode === 'trace' ? 3 : 5;
  for (let i = 0; i < repetitions; i++) {
    const order = i % 2 ? ['fixed', 'original'] : ['original', 'fixed'];
    for (const variant of order) output.runs.push(await run(variant, `${mode}-${i + 1}-${variant}`, mode === 'trace' || mode === 'pilot'));
  }
  fs.writeFileSync(path.join(__dirname, `${mode}.json`), JSON.stringify(output, null, 2));
}
main().catch(error => { console.error(error); process.exitCode = 1; });
