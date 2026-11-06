import assert from 'node:assert/strict'
import test from 'node:test'
import { buildSync } from 'esbuild'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { loadRendererSource } from './renderer-source-loader.mjs'
import { SceneNotesDocument } from '../src/renderer/src/shell/sceneNotesDocument.ts'
import { normalizeChecklist } from '../src/renderer/src/shell/sceneChecklist.ts'
import * as pendingWrites from '../src/renderer/src/stores/pendingWrites.ts'

const output = fileURLToPath(new URL('../out/shared-scene-notes-tests/notes.cjs', import.meta.url))
buildSync({
  stdin: { contents: "export * from './src/renderer/src/collaboration/SharedMarkdownSession'; export * from './src/renderer/src/shell/sharedSceneNotesDocument'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, platform: 'node', format: 'cjs', packages: 'external', outfile: output
})
const require = createRequire(import.meta.url)
const Y = require('yjs')
const { SharedMarkdownSession, SharedSceneNotesDocument } = require(output)
const turn = () => new Promise(resolve => setImmediate(resolve))

function sharedNotes(initial = '- [ ] First\n- [ ] Second\n') {
  const server = new Y.Doc()
  server.getText('prose').insert(0, initial)
  const listeners = new Set()
  const writes = []
  let offline = false
  const connection = {
    read: async () => Y.encodeStateAsUpdate(server),
    write: async update => {
      if (offline) throw new Error('offline')
      writes.push(update)
      Y.applyUpdate(server, update)
      for (const receive of listeners) receive(update)
    },
    subscribe: receive => { listeners.add(receive); return () => listeners.delete(receive) }
  }
  const remote = new Y.Doc()
  Y.applyUpdate(remote, Y.encodeStateAsUpdate(server))
  return {
    server, remote, connection, writes, listeners,
    set offline(value) { offline = value },
    publishRemote(edit) {
      const previous = Y.encodeStateVector(remote)
      edit(remote.getText('prose'))
      const update = Y.encodeStateAsUpdate(remote, previous)
      Y.applyUpdate(server, update)
      for (const receive of listeners) receive(update)
    },
    dispose() { server.destroy(); remote.destroy() }
  }
}

test('shared checklist changes merge with concurrent remote prose and other checkbox edits', async () => {
  const h = sharedNotes()
  const session = new SharedMarkdownSession(await h.connection.read(), h.connection, () => {})
  try {
    // Both replicas edit the same initial checkpoint before seeing each other.
    session.update(session.text.replace('[ ] First', '[x] First'))
    h.publishRemote(text => {
      text.insert(text.toString().indexOf('First') + 5, ' with remote detail')
      const second = text.toString().lastIndexOf('[ ]') + 1
      text.delete(second, 1)
      text.insert(second, 'x')
    })
    await session.flush()
    assert.equal(session.text, '- [x] First with remote detail\n- [x] Second\n')
    assert.equal(h.server.getText('prose').toString(), session.text)
    assert.equal(session.dirty, false)
  } finally { session.detach(); h.dispose() }
})

test('remote shared text publishes changes without scheduling a whole-text save', async () => {
  const h = sharedNotes()
  const notifications = []
  const session = new SharedMarkdownSession(await h.connection.read(), h.connection, failed => notifications.push(failed))
  try {
    h.publishRemote(text => text.insert(0, 'Remote introduction\n'))
    assert.ok(notifications.length > 0)
    assert.ok(session.text.startsWith('Remote introduction\n'))
    assert.equal(session.dirty, false)
    await session.flush()
    assert.equal(h.writes.length, 0)
  } finally { session.detach(); h.dispose() }
})

test('failed shared checklist saves retain deltas while receiving remote edits and can retry', async () => {
  const h = sharedNotes()
  const session = new SharedMarkdownSession(await h.connection.read(), h.connection, () => {})
  try {
    h.offline = true
    session.update(session.text.replace('[ ] First', '[x] First'))
    await assert.rejects(session.flush(), /waiting to be saved/)
    assert.equal(session.dirty, true)
    h.publishRemote(text => text.insert(text.length, '- [ ] Remote addition\n'))
    session.update(session.text.replace('[ ] Second', '[x] Second'))
    await turn()
    h.offline = false
    await session.flush()
    assert.equal(h.server.getText('prose').toString(), '- [x] First\n- [x] Second\n- [ ] Remote addition\n')
    assert.equal(session.dirty, false)
  } finally { session.detach(); h.dispose() }
})

test('retained shared notes flush remains usable after the editing surface closes', async () => {
  const h = sharedNotes()
  const session = new SharedMarkdownSession(await h.connection.read(), h.connection, () => {})
  try {
    h.offline = true
    session.update(session.text + '- [ ] Added before close\n')
    await assert.rejects(session.flush())
    session.detach()
    assert.equal(h.listeners.size, 0)
    h.offline = false
    await session.flush()
    assert.ok(h.server.getText('prose').toString().endsWith('- [ ] Added before close\n'))
    assert.equal(session.dirty, false)
  } finally { h.dispose() }
})

test('the checklist document observes incoming edits and refreshes without dropping local failed saves', async () => {
  const h = sharedNotes()
  const document = new SharedSceneNotesDocument(async changed => new SharedMarkdownSession(await h.connection.read(), h.connection, changed))
  const snapshots = []
  const stop = document.subscribe(() => snapshots.push(document.snapshot()))
  try {
    await document.load()
    assert.equal(document.state.loaded, true)
    h.offline = true
    document.update(document.state.text.replace('[ ] First', '[x] First'))
    await assert.rejects(document.flush())
    assert.ok(document.state.error)
    h.publishRemote(text => text.insert(text.length, 'Remote prose\n'))
    await document.load()
    assert.equal(document.state.text, '- [x] First\n- [ ] Second\nRemote prose\n')
    assert.equal(document.dirty, true)
    assert.ok(document.state.error, 'refreshing shared text must keep the failed-save retry visible')
    assert.ok(snapshots.some(snapshot => snapshot.text.includes('Remote prose')))
    h.offline = false
    await document.flush()
    assert.equal(h.server.getText('prose').toString(), document.state.text)
    assert.equal(document.state.error, null)
  } finally { stop(); document.dispose(); h.dispose() }
})

test('a cancelled shared load releases its notification subscription without displaying stale notes', async () => {
  const h = sharedNotes()
  let release
  const document = new SharedSceneNotesDocument(changed => new Promise(resolve => {
    release = async () => resolve(new SharedMarkdownSession(await h.connection.read(), h.connection, changed))
  }))
  try {
    const loading = document.load()
    document.dispose()
    await release()
    await loading
    assert.equal(document.state.loaded, false)
    assert.equal(h.listeners.size, 0)
  } finally { h.dispose() }
})

test('a clean shared document can load again after a React effect cleanup', async () => {
  const h = sharedNotes()
  const document = new SharedSceneNotesDocument(async changed => new SharedMarkdownSession(await h.connection.read(), h.connection, changed))
  try {
    await document.load()
    document.dispose()
    await document.load()
    document.update(document.state.text.replace('[ ] First', '[x] First'))
    await document.flush()
    assert.ok(h.server.getText('prose').toString().startsWith('- [x] First'))
    assert.equal(h.listeners.size, 1)
  } finally { document.dispose(); h.dispose() }
})

test('floating notes route shared checklists through CRDT and retain the live document across recovery', async () => {
  const h = sharedNotes('- Plain bullet\n')
  const binding = { sessionId: 'session', itemId: 'scene-notes' }
  const context = { projectPath: '/shared-notes', bookId: 'book', draftId: 'draft', epoch: 1, chapterGuid: 'chapter', sceneId: 'scene', title: 'Scene' }
  const state = { projectPath: context.projectPath, activeBookId: 'book', activeDraftId: 'draft',
    chapters: [{ guid: 'chapter', scenes: [{ id: 'scene', sharedProse: { sessionId: binding.sessionId, fields: { notes: binding.itemId } } }] }] }
  let effects = []
  const { useSceneNotesDocument } = loadRendererSource('src/shell/useSceneNotesDocument.ts', {
    react: { useEffect: effect => effects.push(effect), useMemo: create => create(), useSyncExternalStore: (_subscribe, snapshot) => snapshot() },
    '../rpc/client': { rpc: { request: async method => { throw new Error(`Shared notes must not call ${method}`) } } },
    '../stores/pendingWrites': pendingWrites,
    '../stores/projectStore': { useProjectStore: Object.assign(select => select(state), { getState: () => state }) },
    './sceneNotesDocument': { SceneNotesDocument },
    './sharedSceneNotesDocument': { SharedSceneNotesDocument },
    '../collaboration/sharedMarkdown': { openSharedMarkdown: async (actual, changed) => {
      assert.equal(actual.sessionId, binding.sessionId)
      assert.equal(actual.itemId, binding.itemId)
      return new SharedMarkdownSession(await h.connection.read(), h.connection, changed)
    } },
    './sceneChecklist': { normalizeChecklist }
  })
  const mount = async epoch => {
    effects = []
    const result = useSceneNotesDocument({ ...context, epoch })
    const cleanups = effects.map(effect => effect())
    await turn()
    return { ...result, unmount: () => cleanups.forEach(cleanup => cleanup?.()) }
  }
  let current
  try {
    current = await mount(1)
    assert.equal(current.document.state.text, '- Plain bullet\n')
    assert.equal(h.writes.length, 0, 'opening shared notes must not rewrite or normalize prose')
    h.offline = true
    current.document.update('- [x] Plain bullet\n')
    await assert.rejects(current.document.flush())
    const previous = current.document
    current.unmount()
    await turn()
    current = await mount(2)
    assert.equal(current.document, previous)
    h.publishRemote(text => text.insert(text.length, 'Remote continuation\n'))
    current.document.update(current.document.state.text + '- [ ] Later edit\n')
    h.offline = false
    await pendingWrites.flushPendingWrites()
    assert.equal(h.server.getText('prose').toString(), '- [x] Plain bullet\nRemote continuation\n- [ ] Later edit\n')
    current.unmount()
    current = null
    assert.equal(h.listeners.size, 0)
  } finally {
    h.offline = false
    current?.unmount()
    await pendingWrites.flushPendingWrites()
    h.dispose()
  }
})
