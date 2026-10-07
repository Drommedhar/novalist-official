const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const read = file => JSON.parse(fs.readFileSync(path.join(__dirname, file), 'utf8'));
const measured = read('measured.json');
const trace = read('trace.json');
const builds = read('builds.json');
const sha = data => crypto.createHash('sha256').update(data).digest('hex');
function statistics(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const mean = values.reduce((sum, value) => sum + value, 0) / values.length;
  return { n: values.length, values, median: sorted[Math.floor(sorted.length / 2)], mean, min: sorted[0], max: sorted.at(-1) };
}
const selectors = {
  shellAndBackendReadyFromProbeMs: run => run.events.shellAndBackendReadyMs,
  launcherObservedReadyMs: run => run.observedExternalReadyMs,
  navigationToShellAndBackendReadyMs: run => run.renderer.readyMs,
  domContentLoadedMs: run => run.renderer.navigation.domContentLoadedEventEnd,
  firstContentfulPaintMs: run => run.renderer.paints.find(paint => paint.name === 'first-contentful-paint').startTime,
  rendererPeakWorkingSetMiB: run => run.rendererPeakWorkingSetKiB / 1024,
  rendererMaxSampledPrivateMiB: run => run.rendererMaxSampledPrivateKiB / 1024,
  electronMaxSampledWorkingSetMiB: run => run.electronMaxSampledWorkingSetKiB / 1024
};
const metrics = {};
for (const [key, selector] of Object.entries(selectors)) {
  const original = statistics(measured.runs.filter(run => run.variant === 'original').map(selector));
  const fixed = statistics(measured.runs.filter(run => run.variant === 'fixed').map(selector));
  metrics[key] = { original, fixed, medianReduction: original.median - fixed.median, medianReductionPercent: 100 * (original.median - fixed.median) / original.median };
}
const traceRuns = trace.runs.map(run => {
  const events = read(`runs/${run.label}/trace.json`).traceEvents.filter(event => event.pid === run.rendererPid && event.ph === 'X' && event.args?.data?.url?.endsWith('/' + builds.variants[run.variant].entry));
  const selected = events.filter(event => ['v8.parseOnBackground', 'v8.streamingCompile.complete', 'v8.compileModule'].includes(event.name));
  const parse = selected.filter(event => event.name === 'v8.parseOnBackground');
  if (parse.length !== 1) throw new Error(`Expected one entry parse event in ${run.label}`);
  return { label: run.label, variant: run.variant, entryBackgroundParsingMs: parse[0].dur / 1000, events: selected };
});
const originalParse = statistics(traceRuns.filter(run => run.variant === 'original').map(run => run.entryBackgroundParsingMs));
const fixedParse = statistics(traceRuns.filter(run => run.variant === 'fixed').map(run => run.entryBackgroundParsingMs));
metrics.entryBackgroundParsingTraceMs = { original: originalParse, fixed: fixedParse, medianReduction: originalParse.median - fixedParse.median, medianReductionPercent: 100 * (originalParse.median - fixedParse.median) / originalParse.median };
const normalized = Object.fromEntries(Object.entries(builds.variants).map(([variant, build]) => {
  const source = fs.readFileSync(path.join(__dirname, variant, 'renderer/assets', build.entry), 'utf8');
  const withoutImages = source.replace(/^const image\d+ = .*new URL\([^\n]+\n/gm, '').replace(/const manualImages = \{[\s\S]*?\n\};/, 'const manualImages = {};').replace(/\bimage\d+\b/g, 'imageGenerated').replace(/harper-[\w-]+\.js/g, 'harper-CHUNK.js');
  return [variant, { sha256: sha(withoutImages), bytes: Buffer.byteLength(withoutImages) }];
}));
const summary = {
  finding: 'P01', date: new Date().toISOString(), status: 'desktop-measured-native-pending', machine: measured.machine,
  electron: measured.runs[0].versions.electron, chromium: measured.runs[0].versions.chrome,
  buildControl: { ...builds, normalization: 'Remove manualImages map and its emitted URL declarations; normalize Rollup image-number binding names shifted by the added declarations and the resulting Harper chunk hash reference.', normalizedEntryExcludingManualImageDeclarations: normalized, normalizedRemainingEntryBytesIdentical: normalized.original.sha256 === normalized.fixed.sha256 },
  method: [
    'Both renderer builds use the same current working-tree source and documentation. Only the manual plugin varies: original plugin from recorded HEAD vs fixed current plugin. Main/preload bytes are identical; backend executable is shared. Outputs do not replace the normal app build.',
    'One discarded instrumentation pilot per variant, then five untraced runs per variant with alternating pair order. Each run gets a newly created Chromium profile and backend settings directory; no project is opened. Splash/update checks and extensions are disabled equally. External browser requests are blocked. OS file caches are retained.',
    'The readiness probe begins before requiring the copied compiled main module. It waits for the real shell, backend version and RPC client, then two animation frames. Launcher-observed time includes process creation and Playwright/inspector polling; navigation-relative readiness is also retained.',
    'Untraced memory samples use app.getAppMetrics every 50 ms from app-ready until two seconds after readiness is observed. Renderer memory is matched to the real window process PID. PeakWorkingSetSize is the OS-reported process peak; sampled aggregate Electron working sets can double-count shared pages and exclude the separately spawned .NET backend.',
    'Three separate fresh-profile trace runs per variant begin Chromium contentTracing before BrowserWindow.loadFile. The report selects the entry URL and renderer PID v8.parseOnBackground complete event. Its wall duration is a background parsing/streaming-compile trace interval, not a pure parser CPU total. Nested events are not added. Traced timings/memory are excluded from the untraced comparison.',
    'Separate untimed offline acceptance opens all 11 image-bearing manual pages through the real Help UI, awaits each img.decode(), requires nonzero natural dimensions and verifies every emitted asset URL is a local file URL. Browser offline mode and HTTP(S)/WS cancellation stay enabled.'
  ],
  metrics, traceRuns,
  offlineHelp: read('offline-help.json'),
  limitations: [
    'Fresh profile and process are not an OS-cold-cache benchmark. Builds and discarded pilots warm system file caches; no global cache flush or machine restart was performed.',
    'Five timing/memory repetitions and three trace repetitions on one Windows development machine are descriptive, not a statistical performance guarantee. Launch differences are small and one untraced fixed run was slower than its original counterpart.',
    'This runs production-built desktop renderer assets in an unpackaged Electron executable with a Debug .NET backend and instrumentation. Installer/ASAR startup, macOS, Linux performance and physical iPhone/iPad launch/memory remain unmeasured.',
    'All runs start at the empty project library. Peak renderer memory is measured before opening Help; lazy screenshot decoding can allocate later when those pages are visited.',
    'Current source is held constant between variants; results measure the help-image packaging change under this fixture, not an aggregate before/after audit product benchmark.'
  ],
  sources: ['https://www.electronjs.org/docs/latest/api/structures/memory-info/', 'https://www.electronjs.org/docs/latest/api/structures/process-metric', 'https://www.electronjs.org/docs/latest/api/content-tracing'],
  evidence: ['builds.json', 'measured.json', 'trace.json', 'runs/*/metrics.json', 'runs/trace-*/trace.json', 'offline-help.json'],
  commands: ['node artifacts/audit-2026-10-06/p01-desktop/build-variants.cjs', 'node artifacts/audit-2026-10-06/p01-desktop/measure.cjs measured', 'node artifacts/audit-2026-10-06/p01-desktop/measure.cjs trace', 'node artifacts/audit-2026-10-06/p01-desktop/offline-help.cjs', 'node artifacts/audit-2026-10-06/p01-desktop/analyze.cjs']
};
fs.writeFileSync(path.join(__dirname, 'summary.json'), JSON.stringify(summary, null, 2));
console.log(JSON.stringify({ identicalNormalizedRemainingEntry: summary.buildControl.normalizedRemainingEntryBytesIdentical, metrics: Object.fromEntries(Object.entries(metrics).map(([key, value]) => [key, { original: value.original.median, fixed: value.fixed.median, reductionPercent: value.medianReductionPercent }])) }, null, 2));
