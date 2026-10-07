const assert = require('node:assert/strict');
const { writeFileSync, readFileSync } = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require('../../app/node_modules/@playwright/test');

(async () => {
  const ledger = JSON.parse(readFileSync(path.join(__dirname, 'implementation-progress.json'), 'utf8'));
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
    const errors = [];
    const requests = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => { if (/^https?:/.test(request.url())) requests.push(request.url()); });
    await page.goto(pathToFileURL(path.join(__dirname, 'implementation.html')).href);
    assert.equal(await page.locator('.finding').count(), 73);
    const downloading = page.waitForEvent('download');
    await page.getByRole('link', { name: 'Download ledger', exact: true }).click();
    const downloaded = await downloading;
    const stream = await downloaded.createReadStream();
    const chunks = [];
    for await (const chunk of stream) chunks.push(chunk);
    const downloadedLedger = JSON.parse(Buffer.concat(chunks).toString());
    assert.equal(downloadedLedger.findings.length, 73);
    assert.ok(downloadedLedger.findings.every(item => item.validation.length > 0));
    assert.deepEqual(downloadedLedger, ledger);
    assert.equal(await page.locator('#performance tbody tr').count(), 4);
    assert.equal(await page.locator('#native pre').count(), 3);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: path.join(__dirname, 'implementation-desktop.png') });
    for (const status of ['verified', 'device-deferred', 'native-pending', 'verification-pending']) {
      await page.locator('#status').selectOption(status);
      assert.equal(await page.locator('.finding:visible').count(), ledger.findings.filter(item => item.status === status).length);
    }
    const nativeCases = JSON.parse(readFileSync(path.join(__dirname, 'macos-validation/native-acceptance.json'), 'utf8'));
    const deferredIds = ledger.findings.filter(item => item.status === 'device-deferred').map(item => item.id).sort();
    assert.deepEqual(deferredIds, ledger.acceptance_scope.device_deferred_findings.toSorted());
    assert.deepEqual(deferredIds, nativeCases.scope.device_deferred_findings.toSorted());
    for (const finding of nativeCases.findings) {
      for (const scenario of finding.scenarios) {
        if (scenario.acceptance_disposition === 'deferred') {
          assert.equal(scenario.status, 'not-run');
          assert.equal(scenario.required_for_current_scope, false);
          assert.ok(scenario.required_environment.startsWith('physical-'));
        } else if (ledger.goal_status === 'complete') {
          assert.equal(scenario.status, 'passed', `${scenario.id} must pass before completion`);
        }
      }
    }
    if (ledger.goal_status === 'complete') {
      assert.ok(ledger.findings.every(item => ['verified', 'device-deferred'].includes(item.status) && item.pending.length === 0));
      assert.equal(nativeCases.overall_status, 'complete-with-device-deferrals');
    }
    await page.locator('#status').selectOption('');
    await page.locator('#search').fill('A12');
    assert.equal(await page.locator('.finding:visible').count(), 1);
    await page.locator('#expand').click();
    assert.equal(await page.locator('#A12').getAttribute('open'), '');
    await page.evaluate(() => { location.hash = 'M01'; });
    await page.waitForFunction(() => document.getElementById('M01').open);
    assert.equal(await page.locator('.finding:visible').count(), 73);
    await page.locator('#collapse').click();
    await page.evaluate(() => window.dispatchEvent(new Event('beforeprint')));
    assert.equal(await page.locator('.finding[open]').count(), 73);
    await page.evaluate(() => window.dispatchEvent(new Event('afterprint')));
    assert.equal(await page.locator('.finding[open]').count(), 0);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.evaluate(() => { location.hash = ''; window.scrollTo(0, 0); });
    await page.screenshot({ path: path.join(__dirname, 'implementation-mobile.png') });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
    await page.locator('#A01 summary').click();
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
    assert.deepEqual(errors, []);
    assert.deepEqual(requests, []);
    writeFileSync(path.join(__dirname, 'implementation-validation.json'), JSON.stringify({ result: 'passed', findings: 73, goalScope: ledger.goal_status, deviceDeferredFindings: deferredIds, checks: ['downloaded ledger matches source with original acceptance for all 73 findings', 'deferred device cases remain not-run outside current scope', 'completion requires passing nondeferred native scenarios', 'performance table', 'Mac compile/simulator/device handoff', 'all status filters', 'search', 'expand/collapse', 'deep links', 'print expansion/restoration', '390px layout', 'no horizontal overflow', 'no JS errors', 'offline report'] }, null, 2));
    process.stdout.write('Implementation report validated in Chromium at desktop and mobile sizes.\n');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
