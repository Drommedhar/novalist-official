import assert from 'node:assert/strict'
import test from 'node:test'
import { build } from 'esbuild'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const output = fileURLToPath(new URL('../out/shared-scene-notes-tests/editor.cjs', import.meta.url))
await build({
  stdin: { contents: "export * from './src/renderer/src/shell/MarkdownEditor'; export * from './src/renderer/src/collaboration/SharedMarkdownSession'; export * from './src/renderer/src/shell/sharedSceneNotesDocument'; export * from './src/renderer/src/stores/pendingWrites'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, platform: 'node', format: 'cjs', packages: 'external', outfile: output, jsx: 'automatic', loader: { '.css': 'empty' },
  plugins: [{ name: 'isolated-editor-services', setup(build) {
    build.onResolve({ filter: /^(react-i18next|.*\/sharedMarkdown|.*\/SharedEditorPresence)$/ }, args => ({ path: args.path, namespace: 'editor-services' }))
    build.onLoad({ filter: /.*/, namespace: 'editor-services' }, args => ({ contents: args.path === 'react-i18next'
      ? 'export const useTranslation = () => ({ t: key => key })'
      : args.path.endsWith('/sharedMarkdown')
        ? 'export const openSharedMarkdown = () => { throw new Error("The notes editor must reuse its existing document") }'
        : 'export class SharedEditorPresence { close() {} }' }))
  } }]
})
const require = createRequire(import.meta.url)
const { JSDOM } = require('jsdom')
const React = require('react')
const { createRoot } = require('react-dom/client')
const { EditorView } = require('@codemirror/view')
const Y = require('yjs')
const { MarkdownEditor, SharedMarkdownSession, SharedSceneNotesDocument, flushPendingWrites } = require(output)

for (const newline of ['\n', '\r\n']) test(`switching shared notes from Markdown to checklist while offline keeps one live document (${newline.length === 1 ? 'LF' : 'CRLF'})`, async () => {
  const dom = new JSDOM('<div id="notes"></div>', { pretendToBeVisual: true })
  const previous = new Map()
  const globals = { window: dom.window, document: dom.window.document, MutationObserver: dom.window.MutationObserver,
    HTMLElement: dom.window.HTMLElement, Node: dom.window.Node, getComputedStyle: dom.window.getComputedStyle,
    requestAnimationFrame: dom.window.requestAnimationFrame.bind(dom.window), cancelAnimationFrame: dom.window.cancelAnimationFrame.bind(dom.window),
    IS_REACT_ACT_ENVIRONMENT: true }
  for (const [key, value] of Object.entries(globals)) {
    previous.set(key, Object.getOwnPropertyDescriptor(globalThis, key))
    Object.defineProperty(globalThis, key, { configurable: true, writable: true, value })
  }
  const server = new Y.Doc()
  server.getText('prose').insert(0, '- [ ] Original' + newline)
  const listeners = new Set()
  let offline = true
  let opened = 0
  const connection = {
    read: async () => Y.encodeStateAsUpdate(server),
    write: async update => {
      if (offline) throw new Error('offline')
      Y.applyUpdate(server, update)
      for (const receive of listeners) receive(update)
    },
    subscribe: receive => { listeners.add(receive); return () => listeners.delete(receive) }
  }
  const notes = new SharedSceneNotesDocument(async changed => {
    opened++
    return new SharedMarkdownSession(await connection.read(), connection, changed)
  })
  const host = dom.window.document.getElementById('notes')
  const root = createRoot(host)
  try {
    await notes.load()
    await React.act(async () => {
      root.render(React.createElement(MarkdownEditor, { shared: { sessionId: 'session', itemId: 'notes' },
        sharedSession: notes.markdownSession, value: notes.state.text, onChange: () => {} }))
    })
    const content = host.querySelector('.cm-content')
    assert.ok(content, 'the Markdown editor attaches to the existing shared document')
    const editor = EditorView.findFromDOM(content)
    await React.act(async () => {
      editor.dispatch({ changes: { from: editor.state.doc.length, insert: '- [ ] Pending Markdown edit' + newline } })
      await assert.rejects(notes.flush())
    })
    assert.equal(notes.state.text, '- [ ] Original' + newline + '- [ ] Pending Markdown edit' + newline)
    await React.act(async () => root.render(null))
    assert.equal(listeners.size, 1, 'switching modes keeps the notes document subscribed')
    notes.update(notes.state.text.replace('[ ] Original', '[x] Original'))
    assert.equal(notes.state.text, '- [x] Original' + newline + '- [ ] Pending Markdown edit' + newline)
    offline = false
    await notes.flush()
    await flushPendingWrites()
    assert.equal(server.getText('prose').toString(), notes.state.text)
    assert.equal(opened, 1)
    await React.act(async () => {
      root.render(React.createElement(MarkdownEditor, { shared: { sessionId: 'session', itemId: 'notes' },
        sharedSession: notes.markdownSession, value: notes.state.text, onChange: () => {} }))
    })
    assert.ok(host.querySelector('.cm-content').textContent.includes('Pending Markdown edit'))
  } finally {
    offline = false
    await React.act(async () => root.unmount())
    await flushPendingWrites()
    notes.dispose()
    server.destroy()
    dom.window.close()
    for (const [key, descriptor] of previous) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor)
      else delete globalThis[key]
    }
  }
})
