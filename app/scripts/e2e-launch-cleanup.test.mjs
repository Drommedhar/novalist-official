import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

// The source loader handles named imports; expose Playwright's alias under its
// local name so this fixture never loads Playwright or launches native windows.
const source = readFileSync(new URL('../e2e/harness.ts', import.meta.url), 'utf8')
  .replace('_electron as electron', 'electron')

function fixture({ windowFailure, readyFailure, killFailure, blockedEvaluation = false, ignoreTerm = false, alreadyExited = false } = {}) {
  const events = []
  const timers = new Map()
  const child = Object.assign(new EventEmitter(), {
    exitCode: alreadyExited ? 0 : null,
    signalCode: null,
    kill(signal) {
      events.push(signal)
      if (killFailure) throw killFailure
      if (ignoreTerm && signal === 'SIGTERM') return true
      queueMicrotask(() => { child.signalCode = signal; child.emit('exit', null, signal) })
      return true
    }
  })
  const page = {
    locator: () => ({}),
    waitForFunction: async () => { if (readyFailure) throw readyFailure }
  }
  const app = {
    firstWindow: async () => {
      events.push('window')
      if (windowFailure) throw windowFailure
      return page
    },
    process: () => child,
    evaluate: async () => {
      events.push('dispose-windows')
      if (blockedEvaluation) await new Promise(() => {})
    },
    close: async () => { events.push('close') }
  }
  const harness = loadRendererSource('../../e2e/harness.ts', {
    '@playwright/test': {
      electron: { launch: async () => app },
      expect: () => ({ toBeVisible: async () => { if (readyFailure) throw readyFailure } })
    },
    'node:fs': { mkdtempSync: () => { throw new Error('Tests must use their explicit work directory') } },
    'node:os': { tmpdir: () => '/unused' },
    'node:path': { join },
    './appReady': {
      evaluateWhenReady: async () => {},
      waitForAppReady: async () => { if (readyFailure) throw readyFailure }
    }
  }, {
    process: { env: {} },
    setTimeout(callback, delay) { const id = timers.size + 1; timers.set(id, { callback, delay }); return id },
    clearTimeout(id) { timers.delete(id) }
  }, source)
  return { ...harness, app, page, events, timers }
}

test('failed first window closes the launched app before returning the original error', async () => {
  const failure = new Error('firstWindow timed out')
  const f = fixture({ windowFailure: failure })
  await assert.rejects(f.launchApp('unused', {}, '/temporary-test-profile'), error => error === failure)
  assert.deepEqual(f.events, ['window', 'SIGTERM'])
})

for (const mobile of [false, true]) {
  test(`failed ${mobile ? 'mobile' : 'desktop'} readiness closes the unreturned app`, async () => {
    const failure = new Error('readiness failed')
    const f = fixture({ readyFailure: failure })
    await assert.rejects(f.launchApp('unused', mobile ? { NOVALIST_FORCE_MOBILE: '1' } : {}, '/temporary-test-profile'), error => error === failure)
    assert.deepEqual(f.events, ['window', 'SIGTERM'])
  })
}

test('cleanup failure does not replace the original startup error', async () => {
  const failure = new Error('firstWindow timed out')
  const f = fixture({ windowFailure: failure, killFailure: new Error('process already exited') })
  await assert.rejects(f.launchApp('unused', {}, '/temporary-test-profile'), error => error === failure)
  assert.deepEqual(f.events, ['window', 'SIGTERM'])
})

test('successful startup transfers cleanup ownership to the returned harness', async () => {
  const f = fixture()
  const h = await f.launchApp('unused', {}, '/temporary-test-profile')
  assert.equal(h.app, f.app)
  assert.equal(h.page, f.page)
  assert.deepEqual(f.events, ['window'])
  await h.close()
  assert.deepEqual(f.events, ['window', 'dispose-windows', 'close'])
})


test('failed startup does not wait for an unresponsive Electron main thread', async () => {
  const failure = new Error('firstWindow timed out')
  const f = fixture({ windowFailure: failure, blockedEvaluation: true })
  const rejected = assert.rejects(f.launchApp('unused', {}, '/temporary-test-profile'), error => error === failure)
  await new Promise(setImmediate)
  assert.deepEqual(f.events, ['window', 'SIGTERM'])
  await rejected
  assert.equal(f.timers.size, 0, 'process exit cancels the kill timer')
})

test('startup cleanup escalates when the child ignores SIGTERM', async () => {
  const failure = new Error('firstWindow timed out')
  const f = fixture({ windowFailure: failure, ignoreTerm: true })
  const rejected = assert.rejects(f.launchApp('unused', {}, '/temporary-test-profile'), error => error === failure)
  await new Promise(setImmediate)
  assert.deepEqual(f.events, ['window', 'SIGTERM'])
  assert.equal(f.timers.size, 1)
  const timer = [...f.timers.values()][0]
  assert.equal(timer.delay, 5000)
  timer.callback()
  await rejected
  assert.deepEqual(f.events, ['window', 'SIGTERM', 'SIGKILL'])
  assert.equal(f.timers.size, 0)
})

test('startup cleanup accepts a child that already exited', async () => {
  const failure = new Error('firstWindow timed out')
  const f = fixture({ windowFailure: failure, alreadyExited: true })
  await assert.rejects(f.launchApp('unused', {}, '/temporary-test-profile'), error => error === failure)
  assert.deepEqual(f.events, ['window'])
  assert.equal(f.timers.size, 0)
})
