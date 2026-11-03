import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

function harness() {
  const events = new EventTarget()
  const timers = new Map()
  const calls = []
  let frames = []
  let changed
  const document = Object.assign(new EventTarget(), { documentElement: {}, querySelectorAll: () => frames })
  class MutationObserver {
    constructor(callback) { changed = callback }
    observe() {}
    disconnect() { changed = () => {} }
  }
  let nextTimer = 0
  const window = Object.assign(events, {
    novalist: { isMobile: true },
    setTimeout: (action) => { timers.set(++nextTimer, action); return nextTimer },
    clearTimeout: (id) => timers.delete(id),
    setInterval: (action) => { timers.set(++nextTimer, action); return nextTimer },
    clearInterval: (id) => timers.delete(id)
  })
  const rpc = { request(method, args, options) {
    calls.push({ method, args, options })
    if (method === 'backup/isDue') return Promise.resolve(true)
    return new Promise((resolve, reject) => {
      calls.at(-1).finish = resolve
      options?.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true })
    })
  } }
  let cleanup
  const source = loadRendererSource('src/shell/useBackupScheduler.ts', {
    react: { useRef: () => ({ current: null }), useEffect: (effect) => { cleanup = effect() } },
    '../rpc/client': { rpc },
    '../stores/projectStore': { useProjectStore: (selector) => selector({ projectName: 'Novel' }) },
    '../mobile/idleBackups': {
      get startIdleBackups() {
        const activity = loadRendererSource('src/mobile/userActivity.ts', {}, { window, document, MutationObserver })
        return loadRendererSource('src/mobile/idleBackups.ts', {
          '../rpc/client': { rpc }, './userActivity': activity
        }, { window, AbortController }).startIdleBackups
      }
    }
  }, { window })
  source.useBackupScheduler()
  return { calls, window, timers, cleanup: () => cleanup(),
    setFrames(value) { frames = value; changed() },
    frameLoaded() { document.dispatchEvent(new Event('load')) },
    async idle() { const [id, action] = timers.entries().next().value; timers.delete(id); action(); await new Promise(resolve => setImmediate(resolve)) } }
}

test('mobile opening waits for idle; interaction cancels a backup and retries it later', async () => {
  const h = harness()
  try {
    assert.equal(h.calls.length, 0, 'opening must not queue a whole-project backup ahead of scene reads')
    await h.idle()
    assert.equal(h.calls[0].method, 'backup/create')
    h.window.dispatchEvent(new Event('pointerdown'))
    assert.equal(h.calls[0].options.signal.aborted, true)
    await new Promise(resolve => setImmediate(resolve))
    await h.idle()
    assert.equal(h.calls[1].method, 'backup/create')
    h.calls[1].finish(null)
    await new Promise(resolve => setImmediate(resolve))
    await h.idle()
    assert.equal(h.calls[2].method, 'backup/isDue')
  } finally { h.cleanup() }
  assert.equal(h.timers.size, 0)
  assert.equal(h.calls.at(-1).options.signal.aborted, true)
})

test('editor frame interaction cancels backups across frame insertion, reload and removal', async () => {
  const h = harness()
  const frame = { contentDocument: new EventTarget() }
  try {
    h.setFrames([frame])
    await h.idle()
    frame.contentDocument.dispatchEvent(new Event('input'))
    assert.equal(h.calls.at(-1).options.signal.aborted, true)
    await new Promise(resolve => setImmediate(resolve))
    const oldDocument = frame.contentDocument
    frame.contentDocument = new EventTarget()
    h.frameLoaded()
    await h.idle()
    oldDocument.dispatchEvent(new Event('keydown'))
    assert.equal(h.calls.at(-1).options.signal.aborted, false)
    frame.contentDocument.dispatchEvent(new Event('wheel'))
    assert.equal(h.calls.at(-1).options.signal.aborted, true)
    await new Promise(resolve => setImmediate(resolve))
    h.setFrames([])
    await h.idle()
    frame.contentDocument.dispatchEvent(new Event('input'))
    assert.equal(h.calls.at(-1).options.signal.aborted, false)
  } finally { h.cleanup() }
  assert.equal(h.timers.size, 0)
  frame.contentDocument.dispatchEvent(new Event('input'))
  assert.equal(h.timers.size, 0)
})
