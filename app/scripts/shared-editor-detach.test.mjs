import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { build } from 'esbuild'
import { JSDOM } from 'jsdom'
import { loadRendererSource } from './renderer-source-loader.mjs'

const require = createRequire(import.meta.url)
const Y = require('yjs')

const result = await build({
  stdin: { contents: "export * from './src/renderer/src/collaboration/SharedEditorSession'; export * from './src/renderer/src/collaboration/remoteSelections'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, write: false, platform: 'node', format: 'cjs', packages: 'external', loader: { '.css': 'text' }
})
const module = { exports: {} }
new Function('require', 'module', 'exports', result.outputFiles[0].text)(require, module, module.exports)
const { SharedEditorSession, RemoteSelections } = module.exports

function fixture(t) {
  const dom = new JSDOM('<iframe></iframe>')
  const frame = dom.window.document.querySelector('iframe')
  const window = frame.contentWindow
  window.document.body.innerHTML = '<div id="editor"><p>Original.</p></div>'
  const highlights = new Map()
  window.CSS = { highlights }
  window.Highlight = class { constructor(...ranges) { this.ranges = ranges } }
  const originalObserver = Object.getOwnPropertyDescriptor(globalThis, 'MutationObserver')
  Object.defineProperty(globalThis, 'MutationObserver', { configurable: true, value: window.MutationObserver })
  t.after(() => {
    dom.window.close()
    if (originalObserver) Object.defineProperty(globalThis, 'MutationObserver', originalObserver)
    else delete globalThis.MutationObserver
  })
  const root = window.document.getElementById('editor')
  return { frame, window, root, highlights, surface: {
    root, document: window.document,
    sanitizeSceneFragment(html) { const template = window.document.createElement('template'); template.innerHTML = html; return template.content },
    toDisplayImages() {}, getContent: () => root.innerHTML, getPlainText: () => root.textContent,
    flushPendingContentChange() {}
  } }
}

test('retaining a removed shared editor captures its final prose and completes the pending save without window CSS', async t => {
  const f = fixture(t)
  const server = new Y.Doc()
  t.after(() => server.destroy())
  server.getText('prose').insert(0, 'Original.')
  server.getText('prose').insert(9, '\n', { block: JSON.stringify([{ id: 'paragraph', tag: 'p', attributes: {} }]) })
  let subscribed = true
  const statuses = []
  const session = new SharedEditorSession(f.surface, Y.encodeStateAsUpdate(server), {
    read: async () => Y.encodeStateAsUpdate(server),
    write: async update => Y.applyUpdate(server, update),
    subscribe: () => () => { subscribed = false }
  }, status => statuses.push(status))
  const pending = loadRendererSource('src/stores/pendingWrites.ts')
  const retained = loadRendererSource('src/collaboration/sharedDocuments.ts', {
    '../rpc/client': { rpc: { onNotification() {} } }, '../stores/pendingWrites': pending
  })
  // React passive cleanup runs after removing the iframe: its DOM references
  // still hold the last edit, but the old Window has lost its CSS namespace.
  f.root.querySelector('p').textContent = 'Original. Last edit before switching views.'
  f.frame.remove()
  f.window.CSS = undefined
  assert.doesNotThrow(() => retained.retainSharedDocument({ sessionId: 'session', itemId: 'scene' }, 'generation', session))
  assert.equal(subscribed, false)
  await pending.flushPendingWrites()
  assert.equal(server.getText('prose').toString(), 'Original. Last edit before switching views.\n')
  assert.equal(statuses.at(-1).html, '<p>Original. Last edit before switching views.</p>')
  assert.equal(statuses.at(-1).pending, false)
  assert.equal(retained.hasRetainedDocument({ sessionId: 'session', itemId: 'scene' }), false)
})

test('presence renders live selections but stops resolving DOM ranges after the iframe loses its CSS namespace', t => {
  const f = fixture(t)
  const range = f.window.document.createRange()
  range.selectNodeContents(f.root.querySelector('p'))
  range.getBoundingClientRect = () => ({ left: 12, top: 34, height: 18 })
  let detached = false
  const presence = new RemoteSelections(f.window, () => {
    assert.equal(detached, false, 'teardown must not measure detached DOM ranges')
    return range
  })
  presence.show({ id: 'writer', name: 'Other writer', selection: { anchor: [], focus: [] } })
  assert.equal(f.highlights.size, 1)
  assert.equal([...f.highlights.values()][0].ranges[0], range)
  const marker = f.window.document.querySelector('.nv-collaboration-caret')
  assert.equal(marker.textContent, 'Other writer')
  assert.equal(marker.style.top, '34px')
  detached = true
  f.frame.remove()
  f.window.CSS = undefined
  assert.doesNotThrow(() => presence.render())
  assert.doesNotThrow(() => presence.destroy())
  assert.equal(f.window.document.querySelector('.nv-collaboration-caret'), null)
  assert.equal(f.window.document.querySelector('style'), null)
})
