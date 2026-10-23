import assert from 'node:assert/strict'
import test from 'node:test'
import { buildSync } from 'esbuild'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const output = fileURLToPath(new URL('../out/mobile-collaboration-tests/bridge.cjs', import.meta.url))
buildSync({
  stdin: { contents: "export * from './src/renderer/src/mobile/CollaborationDocumentBridge'; export { ProseDocumentHost as DesktopDocuments } from './src/main/collaboration/ProseDocumentHost'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, platform: 'node', format: 'cjs', packages: 'external', outfile: output
})
const require = createRequire(import.meta.url)
const { JSDOM } = require('jsdom')
const Y = require('yjs')
const { CollaborationDocumentBridge, DesktopDocuments } = require(output)

test('mobile joins a desktop document, exchanges edits and retains formatting', async () => {
  const dom = new JSDOM('<!doctype html>')
  const mobile = new CollaborationDocumentBridge(dom.window)
  const desktop = new DesktopDocuments()
  const writer = new Y.Doc()
  const request = (action, params = {}) => mobile.handle('collaboration/documents', { sessionId: 'session', action, ...params })
  try {
    const initial = desktop.seed('scene', '<p>Hello <strong>world</strong>.</p>')
    await request('open')
    await request('execute', { command: { kind: 'load', id: 'scene', state: Buffer.from(initial).toString('base64') } })
    Y.applyUpdate(writer, initial)
    const before = Y.encodeStateVector(writer)
    writer.getText('prose').insert(0, 'Together: ')
    await request('execute', { command: { kind: 'receive', id: 'scene', state: Buffer.from(Y.encodeStateAsUpdate(writer, before)).toString('base64') } })
    const snapshot = await request('capture', { id: 'scene' })
    desktop.receive('scene', Buffer.from(snapshot.state, 'base64'))
    assert.equal(snapshot.html, '<p>Together: Hello <strong>world</strong>.</p>')
    assert.equal(desktop.project('scene'), snapshot.html)
    await request('close')
    await assert.rejects(request('capture', { id: 'scene' }), /no document host/)
  } finally {
    mobile.reset()
    desktop.dispose()
    writer.destroy()
    dom.window.close()
  }
})
