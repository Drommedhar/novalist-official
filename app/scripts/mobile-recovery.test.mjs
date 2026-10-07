import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

const scope = { projectPath: '/books/project', bookId: 'book', draftId: 'draft' }
const scene = { scope, kind: 'scene', source: 'pane:old', chapterGuid: 'chapter', sceneId: 'scene', html: '<p>Recovered</p>', plainText: 'Recovered', hash: 'original' }

function fixture() {
  const storage = new Map()
  const localStorage = { getItem: (key) => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: (key) => storage.delete(key) }
  const journal = loadRendererSource('src/mobile/recoveryJournal.ts', {}, { localStorage })
  let state = { projectPath: scope.projectPath, activeBookId: scope.bookId, activeDraftId: scope.draftId, drafts: [], editors: {}, chapters: [{ guid: 'chapter', scenes: [{ id: 'scene' }] }], dirtyMap: {}, isLoaded: true, activeEditorPaneId: 'pane' }
  const calls = []
  let disk = { html: '<p>Original</p>', hash: 'original' }
  let research = []
  const manuscript = []
  const store = { getState: () => state, setState: (patch) => { state = { ...state, ...patch } } }
  const rpc = { request: async (method, args) => {
    calls.push({ method, args })
    if (method === 'scenes/read') return disk
    if (method === 'research/list') return research
    if (method === 'research/save') return []
    throw new Error(method)
  } }
  const host = { isMobile: true }
  const recovery = loadRendererSource('src/mobile/recovery.ts', {
    '../i18n': { default: { t: (key) => key } }, '../rpc/client': { rpc },
    '../stores/projectStore': { useProjectStore: store },
    '../stores/manuscriptStore': { pendingManuscriptRecovery: () => manuscript, hasPendingManuscriptWrites: () => false },
    '../stores/hostBridgeStore': { useHostBridgeStore: { getState: () => ({ pushToast: (message) => { throw new Error(message) } }) } },
    './recoveryJournal': journal
  }, { window: { novalist: host } })
  Object.assign(state, {
    openScene: async () => { state.editors.pane = { sceneId: 'scene', chapterGuid: 'chapter', html: disk.html, hash: disk.hash, isDirty: false } },
    onEditorContentChanged: (_pane, html, plainText) => { Object.assign(state.editors.pane, { html, plainText, isDirty: true }) },
    flushPane: async () => {
      const editor = state.editors.pane
      if (editor.hash !== disk.hash) { state.sceneConflict = { mine: editor.html, theirs: disk.html }; return }
      disk = { html: editor.html, hash: 'acknowledged' }
      editor.isDirty = false
      recovery.acknowledgeMobileSceneRecovery('scene', editor.html)
    }
  })
  return { ...journal, ...recovery, host, store, calls, manuscript, storage, disk: () => disk, setDisk: (value) => { disk = value }, setResearch: (value) => { research = value } }
}

test('background capture journals scene and manuscript buffers synchronously', () => {
  const f = fixture()
  f.store.setState({ editors: { pane: { ...scene, isDirty: true } } })
  f.manuscript.push({ chapterGuid: 'chapter', sceneId: 'second', html: 'manuscript', plainText: 'manuscript', hash: 'base' })
  const captured = f.captureMobileRecovery()
  assert.equal(captured.length, 2)
  assert.equal(f.readRecoveryJournal().length, 2)
  assert.equal(f.calls.length, 0)
})

test('a deleted recovery scene remains journaled without blocking a later valid scene', async () => {
  const f = fixture()
  f.recordRecovery({ ...scene, sceneId: 'deleted', source: 'pane:deleted' })
  f.recordRecovery(scene)
  await f.restoreMobileRecovery()
  assert.equal(f.disk().html, scene.html)
  assert.equal(f.readRecoveryJournal().length, 1)
  assert.equal(f.readRecoveryJournal()[0].sceneId, 'deleted')
})

test('research recovery copies with different tags or entity links are not mistaken for duplicates', async () => {
  const f = fixture()
  const base = { id: 'note', title: 'Source', type: 'Note', content: 'base', tags: [], entityRefs: [] }
  const draft = { ...base, content: 'recovered', tags: ['retained tag'], entityRefs: ['linked entity'] }
  f.recordRecovery({ scope, kind: 'research', source: 'note', base, draft })
  f.setResearch([{ ...base, content: 'external' }, { ...draft, id: 'previous-copy', title: 'Source (mobileRecovery.recovered)', tags: [], entityRefs: [] }])
  await f.restoreMobileRecovery()
  const save = f.calls.find((call) => call.method === 'research/save')
  assert.deepEqual([...save.args[4]], draft.tags)
  assert.deepEqual([...save.args[5]], draft.entityRefs)
})


test('an older recovery acknowledgement retains a newer background journal for the same source', async () => {
  const f = fixture()
  f.recordRecovery(scene)
  f.store.setState({ flushPane: async () => {
    f.recordRecovery({ ...scene, html: '<p>A newer unsaved edit</p>', plainText: 'A newer unsaved edit' })
    f.acknowledgeMobileSceneRecovery('scene', scene.html)
  } })
  await f.restoreMobileRecovery()
  assert.equal(f.readRecoveryJournal().length, 1)
  assert.equal(f.readRecoveryJournal()[0].html, '<p>A newer unsaved edit</p>')
})


test('recovery refuses a different book or draft without issuing a write', async () => {
  const f = fixture()
  f.recordRecovery(scene)
  f.store.setState({ activeDraftId: 'other' })
  await f.restoreMobileRecovery()
  assert.equal(f.calls.length, 0)
  assert.equal(f.readRecoveryJournal().length, 1)
})

test('unchanged disk restores through a checked scene write and retires its journal', async () => {
  const f = fixture()
  f.recordRecovery(scene)
  await f.restoreMobileRecovery()
  assert.equal(f.disk().html, scene.html)
  assert.equal(f.readRecoveryJournal().length, 0)
})

test('changed disk opens the existing conflict path and keeps the recovery payload', async () => {
  const f = fixture()
  f.recordRecovery(scene)
  f.setDisk({ html: '<p>Changed elsewhere</p>', hash: 'external' })
  await f.restoreMobileRecovery()
  assert.equal(f.disk().html, '<p>Changed elsewhere</p>')
  assert.equal(f.store.getState().sceneConflict.mine, scene.html)
  assert.equal(f.readRecoveryJournal().length, 1)
  f.acknowledgeMobileSceneRecovery('scene', '<p>Changed elsewhere</p>', scene.html)
  assert.equal(f.readRecoveryJournal().length, 0)
})

test('changed research preserves the disk item and creates a recovered copy', async () => {
  const f = fixture()
  const base = { id: 'note', title: 'Source', type: 'Note', content: 'base', tags: [], entityRefs: [] }
  f.recordRecovery({ scope, kind: 'research', source: 'note', base, draft: { ...base, content: 'recovered' } })
  f.setResearch([{ ...base, content: 'edited elsewhere' }])
  await f.restoreMobileRecovery()
  const save = f.calls.find((call) => call.method === 'research/save')
  assert.equal(save.args[0], null)
  assert.equal(save.args[3], 'recovered')
  assert.equal(f.readRecoveryJournal().length, 0)
})

test('the journal refuses oversized replacement while preserving the prior payload', () => {
  const f = fixture()
  f.recordRecovery(scene)
  assert.throws(() => f.recordRecovery({ ...scene, html: 'x'.repeat(2_000_001) }), /journal is full/)
  assert.equal(f.readRecoveryJournal()[0].html, scene.html)
})

test('journal entries produced in this session are not replayed over live edits', async () => {
  const f = fixture()
  await f.restoreMobileRecovery()
  f.recordRecovery(scene)
  await f.restoreMobileRecovery()
  assert.equal(f.calls.length, 0)
})

test('trusted project relocation remaps the journal only within the same book and draft', async () => {
  const f = fixture()
  const old = { ...scene, scope: { ...scope, projectPath: '/old-container/books/project' } }
  f.recordRecovery(old)
  f.host.resolveStoredProjectPath = async (path) => path === old.scope.projectPath ? scope.projectPath : null
  await f.restoreMobileRecovery()
  assert.equal(f.disk().html, scene.html)
  assert.equal(f.readRecoveryJournal().length, 0)
})

test('similar external path suffixes do not authorize journal remapping', async () => {
  const f = fixture()
  const old = { ...scene, scope: { ...scope, projectPath: '/external/books/project' } }
  f.recordRecovery(old)
  f.host.resolveStoredProjectPath = async () => null
  await f.restoreMobileRecovery()
  assert.equal(f.calls.length, 0)
  assert.equal(f.readRecoveryJournal()[0].scope.projectPath, old.scope.projectPath)
})

test('journal remapping cannot overwrite newer target-scope captures', () => {
  const f = fixture()
  const old = { ...scene, scope: { ...scope, projectPath: '/old-container/books/project' } }
  f.recordRecovery(old)
  f.recordRecovery({ ...scene, html: '<p>Newer capture</p>' })
  assert.equal(f.remapRecoveryEntry(old, scope), null)
  assert.equal(f.readRecoveryJournal().length, 2)
  assert.equal(f.readRecoveryJournal()[1].html, '<p>Newer capture</p>')
})

test('a workspace change during trusted path resolution prevents recovery writes', async () => {
  const f = fixture()
  const old = { ...scene, scope: { ...scope, projectPath: '/old-container/books/project' } }
  f.recordRecovery(old)
  f.host.resolveStoredProjectPath = async () => {
    f.store.setState({ activeDraftId: 'different' })
    return scope.projectPath
  }
  await f.restoreMobileRecovery()
  assert.equal(f.calls.length, 0)
  assert.equal(f.readRecoveryJournal()[0].scope.projectPath, old.scope.projectPath)
})
