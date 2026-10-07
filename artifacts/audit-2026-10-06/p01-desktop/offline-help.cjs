const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../../..');
const { _electron: electron } = require(path.join(root, 'app/node_modules/@playwright/test'));
async function main() {
  const dir = fs.mkdtempSync(path.join(__dirname, 'offline-help-'));
  const env = { ...process.env, P01_VARIANT: 'fixed', NOVALIST_NO_SPLASH: '1', NOVALIST_NO_CLIPBOARD: '1', NOVALIST_EXTENSIONS_DISABLED: '1', NOVALIST_SETTINGS_DIR: path.join(dir, 'settings'), NOVALIST_BACKEND_PATH: path.join(root, 'Novalist.Backend/bin/Debug/net8.0/Novalist.Backend.exe'), NODE_PATH: path.join(root, 'app/node_modules') };
  delete env.ELECTRON_RUN_AS_NODE;
  const app = await electron.launch({ executablePath: path.join(root, 'app/node_modules/electron/dist/electron.exe'), args: [path.join(__dirname, 'probe-main.cjs'), `--user-data-dir=${path.join(dir, 'profile')}`], env });
  try {
    await app.context().setOffline(true);
    const page = await app.firstWindow();
    await page.waitForFunction(() => !!window.novalistStores?.shell.getState().backendVersion);
    await page.evaluate(() => { const shell = window.novalistStores.shell.getState(); shell.setTourOpen(false); shell.setHelpOpen(true); });
    const result = [];
    const manual = path.join(root, 'docs/manual');
    for (const file of fs.readdirSync(manual).filter(file => file.endsWith('.md')).sort()) {
      const markdown = fs.readFileSync(path.join(manual, file), 'utf8');
      const images = [...markdown.matchAll(/!\[[^\]]*\]\(images\/([^)]*)\)/g)].map(match => match[1]);
      if (!images.length) continue;
      const title = markdown.match(/^# (.+)$/m)[1].trim();
      await page.locator('.help-page-item').filter({ has: page.locator('.help-result-page', { hasText: new RegExp('^' + title.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '$') }) }).click();
      await page.locator('.help-main h1').filter({ hasText: title }).waitFor();
      const decoded = await page.locator('.help-image').evaluateAll(async elements => Promise.all(elements.map(async image => {
        await image.decode();
        return { url: image.currentSrc, width: image.naturalWidth, height: image.naturalHeight, complete: image.complete };
      })));
      assert.equal(decoded.length, images.length, 'Image count on ' + file);
      decoded.forEach((image, index) => {
        assert.ok(image.complete && image.width > 0 && image.height > 0, 'Decoded image dimensions');
        assert.ok(image.url.startsWith('file:') && image.url.includes('/fixed/renderer/assets/'), 'Built local asset URL');
        result.push({ page: file, original: images[index], ...image });
      });
    }
    const expected = JSON.parse(fs.readFileSync(path.join(__dirname, 'builds.json'))).variants.fixed.emittedPng;
    assert.equal(new Set(result.map(image => image.url)).size, expected.length);
    assert.equal(result.length, 11);
    fs.writeFileSync(path.join(__dirname, 'offline-help.json'), JSON.stringify({ date: new Date().toISOString(), status: 'passed', externalRequestsBlocked: true, browserOffline: true, uniqueImages: result.length, images: result, blockedRequests: await app.evaluate(() => global.__p01.blockedRequests) }, null, 2));
    console.log('All 11 Help images decoded from emitted local assets while offline.');
  } finally {
    await app.evaluate(({ app, BrowserWindow }) => { app.removeAllListeners('window-all-closed'); const quit = app.quit.bind(app); app.quit = () => setImmediate(quit); for (const win of BrowserWindow.getAllWindows()) win.destroy(); }).catch(() => {});
    await app.close();
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
