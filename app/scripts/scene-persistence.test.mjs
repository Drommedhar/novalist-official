import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise((resolve) => setImmediate(resolve))
const editor = (html, hash = 'hash-0') => ({ chapterGuid: 'c', sceneId: 's', html, plainText: html, hash, isDirty: true, tabs: [{ chapterGuid: 'c', sceneId: 's' }] })

function fixture(isMobile = false) {
  let state = { projectPath: '/synthetic/project', activeBookId: 'book', activeDraftId: 'draft', drafts: [], workspaceEpoch: 1, sceneHashes: { s: 'hash-0' }, sceneConflict: null, activeEditorPaneId: 'a', editors: {}, dirtyMap: {}, chapters: [] }
  const store = { getState: () => state, setState: (next) => { state = { ...state, ...(typeof next === 'function' ? next(state) : next) } } }
  const helpers = { useProjectStore: store, mirror: () => ({}),
    mapEditors: (editors, fn) => Object.fromEntries(Object.entries(editors).map(([id, value]) => [id, fn(value)])),
    editorPane: (s, pane) => s.editors[pane] ?? { tabs: [] },
    patchEditor: (s, pane, patch) => ({ editors: { ...s.editors, [pane]: { ...s.editors[pane], ...patch } } }),
    targetEditorPane: () => 'a', writePaneIds: () => ['a', 'b'], EMPTY_EDITOR: { tabs: [] } }
  let disk = 'base', hash = 'hash-0', serial = 0
  const requests = []
  const rpc = { request: async (method, args) => {
    requests.push({ method, args })
    if (method === 'scenes/write') {
      if (args[4] !== hash) return { conflicted: true, diskHtml: disk, hash }
      disk = args[2]; hash = `hash-${++serial}`
      return { conflicted: false, hash, wordCount: 1 }
    }
    if (method === 'scenes/read') return { html: 'next', hash: 'next-hash' }
    throw new Error(method)
  } }
  const queue = loadRendererSource('src/stores/sceneWriteQueue.ts')
  const timers = []
  const persistence = loadRendererSource('src/stores/projectPersistence.ts', {
    '../rpc/client': { rpc }, './projectStore': helpers, './sceneWriteQueue': queue
  }, { setTimeout: (fn) => (timers.push(fn), timers.length), clearTimeout() {} })
  const shell = { unsavedGuards: {}, setPaneView() {}, setActivePane() {}, panes: {} }
  const storage = new Map()
  const journal = loadRendererSource('src/mobile/recoveryJournal.ts', {}, { localStorage: {
    getItem: (key) => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: (key) => storage.delete(key)
  } })
  const recovery = loadRendererSource('src/mobile/recovery.ts', {
    '../i18n': { default: { t: (key) => key } }, '../rpc/client': { rpc },
    '../stores/projectStore': { useProjectStore: store },
    '../stores/manuscriptStore': { pendingManuscriptRecovery: () => [], hasPendingManuscriptWrites: () => false },
    '../stores/hostBridgeStore': { useHostBridgeStore: { getState: () => ({ pushToast: (message) => { throw new Error(message) } }) } },
    './recoveryJournal': journal
  }, { window: { novalist: { isMobile } } })
  const actions = loadRendererSource('src/stores/projectEditorActions.ts', {
    '../rpc/client': { rpc }, './shellStore': { useShellStore: { getState: () => shell }, paneLeaves: () => [] },
    './projectStore': helpers, './projectPersistence': persistence, './sceneWriteQueue': queue,
    './pendingWrites': { capturePendingWrites() {} }, './projectSceneActions': { runSceneContext: (fn) => fn() },
    '../mobile/recovery': recovery
  }).createProjectEditorActions(store.setState, store.getState)
  store.setState(actions)
  return { store, rpc, persistence, timers, requests, disk: () => disk, readJournal: journal.readRecoveryJournal }
}

test('mobile scene edits survive in the journal before autosave or a failure notification', () => {
  const f = fixture(true)
  f.store.setState({ editors: { a: editor('base'), b: { ...editor('second'), sceneId: 'other' } } })
  f.store.getState().onEditorContentChanged('a', '<p>First pending edit</p>', 'First pending edit')
  f.store.getState().onEditorContentChanged('b', '<p>Second pending edit</p>', 'Second pending edit')
  f.store.getState().onEditorContentChanged('a', '<p>Newest pending edit</p>', 'Newest pending edit')
  const entries = f.readJournal()
  assert.equal(entries.length, 2)
  assert.equal(entries.find((entry) => entry.source === 'pane:a').html, '<p>Newest pending edit</p>')
  assert.equal(entries.find((entry) => entry.source === 'pane:b').sceneId, 'other')
  assert.equal(entries[0].scope.draftId, 'draft')
  assert.equal(entries[0].hash, 'hash-0')
  assert.equal(f.requests.length, 0)
  assert.equal(f.disk(), 'base')
})

test('desktop scene edits do not create a mobile recovery journal', () => {
  const f = fixture()
  f.store.setState({ editors: { a: editor('base') } })
  f.store.getState().onEditorContentChanged('a', 'desktop edit', 'desktop edit')
  assert.equal(f.readJournal().length, 0)
})

test('divergent split panes retain their own base and produce a conflict', async () => {
  const f = fixture()
  f.store.setState({ editors: { a: editor('first'), b: editor('second') } })
  await f.persistence.flushEditor(f.store.getState().editors.a)
  await f.persistence.flushEditor(f.store.getState().editors.b)
  assert.equal(f.disk(), 'first')
  assert.equal(f.store.getState().sceneConflict.mine, 'second')
  assert.equal(f.store.getState().editors.b.isDirty, true)
})

for (const operation of ['open', 'close']) test(`refused save preserves the visible dirty editor on ${operation}`, async () => {
  const f = fixture()
  f.store.setState({ editors: { a: editor('mine', 'stale') } })
  if (operation === 'open') await f.store.getState().openSceneIn('a', 'c', 'next')
  else await f.store.getState().closeTab('a', 's')
  f.store.getState().dismissSceneConflict()
  assert.equal(f.store.getState().editors.a.sceneId, 's')
  assert.equal(f.store.getState().editors.a.html, 'mine')
  assert.equal(f.store.getState().editors.a.isDirty, true)
})

test('overlapping autosaves serialize and preserve a newer dirty edit until acknowledged', async () => {
  const f = fixture()
  const request = f.rpc.request
  const releases = []
  f.rpc.request = (method, args) => method === 'scenes/write' ? new Promise((resolve) => { releases.push(() => request(method, args).then(resolve)) }) : request(method, args)
  f.store.setState({ editors: { a: editor('one') } })
  f.persistence.scheduleSave('a', 'c', 's', 'one', 'one')
  f.timers.shift()()
  await turn()
  f.store.setState({ editors: { a: editor('two') } })
  f.persistence.scheduleSave('a', 'c', 's', 'two', 'two')
  f.timers.shift()()
  await turn()
  assert.equal(releases.length, 1)
  await releases.shift()()
  await turn()
  assert.equal(f.store.getState().editors.a.html, 'two')
  assert.equal(f.store.getState().editors.a.isDirty, true)
  await releases.shift()()
  await f.persistence.autosaveWrites.get('a')
  assert.equal(f.disk(), 'two')
  assert.equal(f.store.getState().sceneConflict, null)
  assert.equal(f.store.getState().editors.a.isDirty, false)
})

test('a failed save retains the dirty buffer and the next explicit flush can retry', async () => {
  const f = fixture()
  const request = f.rpc.request
  f.store.setState({ editors: { a: editor('mine') } })
  f.rpc.request = async () => { throw new Error('offline') }
  await assert.rejects(f.store.getState().flushPane('a'), /offline/)
  assert.equal(f.store.getState().editors.a.isDirty, true)
  f.rpc.request = request
  await f.store.getState().flushPane('a')
  assert.equal(f.disk(), 'mine')
})
