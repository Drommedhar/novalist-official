const { app, BrowserWindow, contentTracing, session } = require('electron');
const path = require('node:path');
const started = performance.now();
const probe = global.__p01 = { samples: [], events: {}, blockedRequests: [] };
const capture = () => {
  if (!app.isReady()) return;
  probe.samples.push({ elapsedMs: performance.now() - started, processes: app.getAppMetrics() });
};
app.whenReady().then(() => {
  session.defaultSession.webRequest.onBeforeRequest({ urls: ['http://*/*', 'https://*/*', 'ws://*/*', 'wss://*/*'] }, (details, callback) => {
    probe.blockedRequests.push(details.url);
    callback({ cancel: true });
  });
  if (!process.env.P01_TRACE) probe.timer = setInterval(capture, 50);
});
if (process.env.P01_TRACE) {
  const traceReady = app.whenReady().then(() => contentTracing.startRecording({ included_categories: ['v8', 'v8.execute', 'devtools.timeline', 'disabled-by-default-v8.compile'] }));
  const original = BrowserWindow.prototype.loadFile;
  BrowserWindow.prototype.loadFile = async function (...args) {
    await traceReady;
    return original.apply(this, args);
  };
}
app.on('browser-window-created', (_event, win) => {
  win.once('ready-to-show', () => { probe.events.readyToShowMs = performance.now() - started; });
  win.webContents.once('dom-ready', () => {
    probe.rendererPid = win.webContents.getOSProcessId();
    win.webContents.executeJavaScript(`new Promise(resolve => {
      const wait = () => {
        if (window.novalistRpc && window.novalistStores?.shell.getState().backendVersion && document.querySelector('.shell')) {
          requestAnimationFrame(() => requestAnimationFrame(() => resolve({ readyMs: performance.now(), navigation: performance.getEntriesByType('navigation')[0]?.toJSON(), paints: performance.getEntriesByType('paint').map(e => e.toJSON()) })));
        } else requestAnimationFrame(wait);
      }; wait();
    })`).then(result => {
      probe.events.shellAndBackendReadyMs = performance.now() - started;
      probe.renderer = result;
      capture();
    }).catch(error => { probe.error = String(error); });
  });
});
app.on('will-quit', () => clearInterval(probe.timer));
require(path.join(__dirname, process.env.P01_VARIANT, 'main/index.js'));
