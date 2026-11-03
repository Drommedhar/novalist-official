import assert from 'node:assert/strict'
import test from 'node:test'
import { create } from 'zustand'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
const sections = (html = '<p>Original.</p>', hash = 'original') => [{
  chapterGuid: 'chapter', chapterTitle: 'Chapter', status: 'Revised', act: '', scenes: [{
    sceneId: 'scene', title: 'Scene', html, hash, wordCount: 2,
    synopsis: null, pov: null, goal: null, outcome: null, inactive: false
  }]
}]

function fixture() {
  const requests = []
  const timers = new Map()
  let nextTimer = 0
  let project = { sceneConflict: null, applyManuscriptSceneWrite() {} }
  const filter = { status: '', stage: 'firstDraft' }
  const pending = loadRendererSource('src/stores/pendingWrites.ts')
  const source = loadRendererSource('src/stores/manuscriptStore.ts', {
    zustand: { create },
    '../rpc/client': { rpc: { request: (method, args) => new Promise((resolve, reject) => requests.push({ method, args, resolve, reject })) } },
    './projectStore': { useProjectStore: { getState: () => project, setState: patch => { project = { ...project, ...patch } } }, reportManuscriptEditing() {} },
    './filterStore': { useFilterStore: { getState: () => ({ filter }) } },
    './pendingWrites': pending,
    '../collaboration/SharedMetadataField': { metadataBinding: () => null },
    '../collaboration/metadataCommands': { writeSharedMetadata() { throw new Error('Local edits cannot use shared metadata') } },
    '../i18n': { default: { t: key => key } },
    '../mobile/recovery': { retainMobileSceneRecovery() {} }
  }, {
    window: { novalist: { isMobile: false } },
    setTimeout: fn => { timers.set(++nextTimer, fn); return nextTimer },
    clearTimeout: id => timers.delete(id)
  })
  const store = source.useManuscriptStore
  store.setState({ sections: sections(), loaded: true })
  return {
    ...source, store, requests, filter, pending,
    scene: () => store.getState().sections[0]?.scenes[0],
    edit: html => store.getState().onSceneContentChanged('scene', html, html.replace(/<[^>]+>/g, ''), 2),
    respond: async (index, value) => { requests[index].resolve(value); await turn() },
    saved: async (index, hash = 'saved') => { requests[index].resolve({ sceneId: 'scene', hash, wordCount: 2, conflicted: false, diskHtml: null }); await turn() }
  }
}

test('filter reload persists the first edit before fetching prose so the next edit cannot erase it', async () => {
  const f = fixture()
  f.edit('<p>Original. First edit.</p>')
  const load = f.store.getState().load()
  await turn()
  assert.equal(f.requests[0].method, 'scenes/write')
  assert.equal(f.requests.length, 1)
  await f.saved(0)
  assert.equal(f.requests[1].method, 'manuscript/get')
  assert.equal(f.requests[1].args[5], 'firstDraft')
  await f.respond(1, sections('<p>Original. First edit.</p>', 'saved'))
  await load
  f.edit(f.scene().html.replace('</p>', ' Second edit.</p>'))
  const save = f.store.getState().flushPendingSave()
  await turn()
  assert.equal(f.requests[2].args[2], '<p>Original. First edit. Second edit.</p>')
  await f.saved(2, 'second')
  await save
})

test('filter reload captures prose still waiting in the mounted iframe debounce', async () => {
  const f = fixture()
  let captured = false
  f.pending.registerPendingWrite(() => {}, () => {
    if (!captured) { captured = true; f.edit('<p>Fresh iframe edit.</p>') }
  })
  const load = f.store.getState().load()
  await turn()
  assert.equal(f.requests[0].method, 'scenes/write')
  assert.equal(f.requests[0].args[2], '<p>Fresh iframe edit.</p>')
  await f.saved(0)
  await f.respond(1, [])
  await load
  assert.equal(f.store.getState().sections.length, 0)
  assert.equal(f.hasPendingManuscriptWrites(), false)
})

test('typing during a filter read retires its stale snapshot and reloads after the new edit saves', async () => {
  const f = fixture()
  const load = f.store.getState().load()
  await turn()
  f.edit('<p>Typed during read.</p>')
  await f.respond(0, sections())
  assert.equal(f.scene().html, '<p>Typed during read.</p>')
  assert.equal(f.requests[1]?.method, 'scenes/write')
  await f.saved(1)
  assert.equal(f.requests[2].method, 'manuscript/get')
  await f.respond(2, sections('<p>Typed during read.</p>', 'saved'))
  await load
  assert.equal(f.scene().html, '<p>Typed during read.</p>')
})

test('an edit made during the filter save is also persisted before a read starts', async () => {
  const f = fixture()
  f.edit('<p>First edit.</p>')
  const load = f.store.getState().load()
  await turn()
  assert.equal(f.requests[0].method, 'scenes/write')
  f.edit('<p>First and second edit.</p>')
  await f.saved(0, 'first')
  assert.equal(f.requests[1].method, 'scenes/write')
  assert.equal(f.requests[1].args[4], 'first')
  await f.saved(1, 'second')
  await f.respond(2, sections('<p>First and second edit.</p>', 'second'))
  await load
  assert.equal(f.scene().html, '<p>First and second edit.</p>')
})

test('an autosave completing during a slow filter read cannot make its stale snapshot current', async () => {
  const f = fixture()
  const load = f.store.getState().load()
  await turn()
  f.edit('<p>Already saved newer words.</p>')
  const save = f.store.getState().flushPendingSave()
  await turn()
  await f.saved(1)
  await save
  assert.equal(f.hasPendingManuscriptWrites(), false)
  await f.respond(0, sections())
  assert.equal(f.scene().html, '<p>Already saved newer words.</p>')
  assert.equal(f.requests[2]?.method, 'manuscript/get')
  await f.respond(2, sections('<p>Already saved newer words.</p>', 'saved'))
  await load
})

test('a failed filter save leaves the visible prose and retryable payload intact', async () => {
  const f = fixture()
  f.edit('<p>Keep these words.</p>')
  const result = f.store.getState().load().catch(error => error)
  await turn()
  assert.equal(f.requests[0].method, 'scenes/write')
  f.requests[0].reject(new Error('Disk full'))
  assert.match((await result).message, /Disk full/)
  assert.equal(f.scene().html, '<p>Keep these words.</p>')
  assert.equal(f.hasPendingManuscriptWrites(), true)
  assert.equal(f.requests.length, 1)
})

test('overlapping filter loads only publish the newest requested scene set', async () => {
  const f = fixture()
  const first = f.store.getState().load()
  await turn()
  f.filter.stage = 'outline'
  const second = f.store.getState().load()
  await turn()
  await f.respond(1, [])
  await second
  await f.respond(0, sections())
  await first
  assert.equal(f.store.getState().sections.length, 0)
})

test('an active Smart List with no matches is empty, while clearing it restores the book', async () => {
  const f = fixture()
  const apply = f.store.getState().applyList('final-scenes')
  await f.respond(0, [])
  // The old implementation reads the whole book for [], exposing the defect.
  if (f.requests[1]) await f.respond(1, sections())
  await apply
  assert.equal(f.store.getState().sections.length, 0)
  assert.equal(f.store.getState().filterListId, 'final-scenes')
  const clear = f.store.getState().applyList('')
  await turn()
  await f.respond(f.requests.length - 1, sections())
  await clear
  assert.equal(f.store.getState().filterListId, '')
  assert.equal(f.scene().sceneId, 'scene')
})

test('a failed Smart List query preserves the previous selection and reports the error', async () => {
  const f = fixture()
  f.store.setState({ filterListId: 'previous-list', composed: ['scene'] })
  const apply = f.store.getState().applyList('unavailable-list').catch(error => error)
  f.requests[0].reject(new Error('Query failed'))
  await turn()
  if (f.requests[1]) await f.respond(1, sections())
  const error = await apply
  assert.equal(f.store.getState().filterListId, 'previous-list')
  assert.match(error.message, /Query failed/)
  assert.equal(f.scene().sceneId, 'scene')
})

test('Show the whole book clears an empty Smart List selection', async () => {
  const f = fixture()
  f.store.setState({ filterListId: 'empty-list', composed: [], sections: [] })
  const load = f.store.getState().compose(null)
  await turn()
  await f.respond(0, sections())
  await load
  assert.equal(f.store.getState().filterListId, '')
  assert.equal(f.store.getState().composed, null)
  assert.equal(f.scene().sceneId, 'scene')
})

test('filter reload drains registered shared prose before querying instead of spinning on pending edits', async () => {
  const f = fixture()
  let captures = 0, flushed = false
  f.store.setState({ sharedEdits: { shared: { chapterGuid: 'chapter', sceneId: 'scene', pending: true, error: false } } })
  f.pending.registerPendingWrite(async () => {
    flushed = true
    f.store.setState({ sharedEdits: {} })
  }, () => {
    if (++captures > 5) throw new Error('Reload is spinning without flushing shared prose')
  })
  const load = f.store.getState().load()
  // Observe the promise immediately so the old spin-loop failure is reported.
  const result = load.then(() => null, error => error)
  await turn()
  assert.equal(flushed, true)
  assert.equal(f.requests[0]?.method, 'manuscript/get')
  await f.respond(0, sections())
  assert.equal(await result, null)
})
