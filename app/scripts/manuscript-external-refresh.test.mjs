import assert from 'node:assert/strict'
import test from 'node:test'
import { create } from 'zustand'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
const external = { chapterGuid: 'chapter', sceneId: 'scene', html: '<p>External words.</p>', hash: 'external', wordCount: 2 }

function fixture() {
  const requests = []
  const timers = new Map()
  const claims = []
  let timerId = 0
  let project = { sceneConflict: null, applyManuscriptSceneWrite() {} }
  const projectStore = { getState: () => project, setState: patch => { project = { ...project, ...patch } } }
  const source = loadRendererSource('src/stores/manuscriptStore.ts', {
    zustand: { create },
    '../rpc/client': { rpc: { request: (method, args) => new Promise(resolve => { requests.push({ method, args, resolve }) }) } },
    './projectStore': { useProjectStore: projectStore, reportManuscriptEditing: value => claims.push(value) },
    './filterStore': { useFilterStore: { getState: () => ({ filter: {} }) } },
    './pendingWrites': { capturePendingWrites() {} },
    '../i18n': { default: { t: key => key } },
    '../mobile/recovery': { retainMobileSceneRecovery() {} }
  }, {
    window: { novalist: { isMobile: false } },
    setTimeout: callback => { timers.set(++timerId, callback); return timerId },
    clearTimeout: id => timers.delete(id)
  })
  const store = source.useManuscriptStore
  store.setState({ sections: [{ chapterGuid: 'chapter', chapterTitle: 'Chapter', status: 'Outline', act: '', scenes: [{
    sceneId: 'scene', title: 'Scene', html: '<p>Original.</p>', hash: 'original', wordCount: 1,
    synopsis: null, pov: null, goal: null, outcome: null, inactive: false
  }] }] })
  return {
    ...source, projectStore, store, requests, claims,
    scene: () => store.getState().sections[0].scenes[0],
    edit: html => store.getState().onSceneContentChanged('scene', html, html.replace(/<[^>]+>/g, ''), 2),
    runTimers: async () => {
      for (const [id, callback] of [...timers]) { timers.delete(id); callback() }
      await turn()
    },
    respond: async (index, response) => { requests[index].resolve(response); await turn() }
  }
}

test('matching external manuscript prose acknowledges the pending edit without a conflict', () => {
  const f = fixture()
  f.edit(external.html)
  f.applyExternalManuscriptScene(external, 'original')
  assert.equal(f.projectStore.getState().sceneConflict, null)
  assert.equal(f.hasPendingManuscriptWrites(), false)
  assert.equal(f.scene().html, external.html)
  assert.equal(f.scene().hash, 'external')
  assert.equal(f.claims.at(-1).length, 0)
})

test('external manuscript conflicts pause the pending autosave and retain the local payload', async () => {
  const f = fixture()
  f.edit('<p>Local words.</p>')
  f.applyExternalManuscriptScene(external, 'original')
  await f.runTimers()
  assert.equal(f.requests.length, 0)
  assert.equal(f.hasPendingManuscriptWrites(), true)
  assert.equal(f.scene().html, '<p>Local words.</p>')
  assert.equal(f.projectStore.getState().sceneConflict.mine, '<p>Local words.</p>')
  assert.equal(f.projectStore.getState().sceneConflict.theirs, external.html)
})

for (const conflicted of [false, true]) {
  test(`a discarded in-flight manuscript save cannot replace a resolved scene (conflicted=${conflicted})`, async () => {
    const f = fixture()
    f.edit('<p>Old local words.</p>')
    await f.runTimers()
    f.applyExternalManuscriptScene(external, 'original')
    f.store.getState().acceptResolvedScene('scene', external.html, 2, 'external')
    f.projectStore.setState({ sceneConflict: null })
    await f.respond(0, { sceneId: 'scene', conflicted, diskHtml: external.html, hash: 'old-save', wordCount: 3 })
    assert.equal(f.projectStore.getState().sceneConflict, null)
    assert.equal(f.scene().html, external.html)
    assert.equal(f.scene().hash, 'external')
    assert.equal(f.hasPendingManuscriptWrites(), false)
  })
}

test('acknowledging matching disk prose also discards an older in-flight manuscript save', async () => {
  const f = fixture()
  f.edit('<p>Earlier local words.</p>')
  await f.runTimers()
  f.edit(external.html)
  f.applyExternalManuscriptScene(external, 'original')
  await f.respond(0, { sceneId: 'scene', conflicted: true, diskHtml: external.html, hash: 'external', wordCount: 2 })
  assert.equal(f.projectStore.getState().sceneConflict, null)
  assert.equal(f.scene().html, external.html)
  assert.equal(f.scene().hash, 'external')
  assert.equal(f.hasPendingManuscriptWrites(), false)
})

test('normal manuscript save acknowledgements advance the base of newer pending edits', async () => {
  const f = fixture()
  f.edit('<p>First version.</p>')
  await f.runTimers()
  f.edit('<p>Second version.</p>')
  await f.runTimers()
  assert.equal(f.requests.length, 1)
  await f.respond(0, { sceneId: 'scene', conflicted: false, diskHtml: null, hash: 'first-save', wordCount: 2 })
  assert.equal(f.scene().html, '<p>Second version.</p>')
  assert.equal(f.hasPendingManuscriptWrites(), true)
  assert.equal(f.requests.length, 2)
  assert.equal(f.requests[1].args[4], 'first-save')
  await f.respond(1, { sceneId: 'scene', conflicted: false, diskHtml: null, hash: 'second-save', wordCount: 2 })
  assert.equal(f.scene().html, '<p>Second version.</p>')
  assert.equal(f.scene().hash, 'second-save')
  assert.equal(f.hasPendingManuscriptWrites(), false)
  assert.equal(f.projectStore.getState().sceneConflict, null)
})

test('resolving a manuscript conflict discards queued old saves while allowing later edits', async () => {
  const f = fixture()
  f.edit('<p>First old edit.</p>')
  await f.runTimers()
  f.edit('<p>Queued old edit.</p>')
  await f.runTimers()
  f.applyExternalManuscriptScene(external, 'original')
  f.store.getState().acceptResolvedScene('scene', external.html, 2, 'external')
  f.projectStore.setState({ sceneConflict: null })
  f.edit('<p>New edit after resolution.</p>')
  await f.runTimers()
  await f.respond(0, { sceneId: 'scene', conflicted: true, diskHtml: external.html, hash: 'external', wordCount: 2 })
  assert.equal(f.requests.length, 2)
  assert.equal(f.requests[1].args[2], '<p>New edit after resolution.</p>')
  assert.equal(f.requests[1].args[4], 'external')
  assert.equal(f.hasPendingManuscriptWrites(), true)
  await f.respond(1, { sceneId: 'scene', conflicted: false, diskHtml: null, hash: 'new-edit', wordCount: 4 })
  assert.equal(f.projectStore.getState().sceneConflict, null)
  assert.equal(f.scene().html, '<p>New edit after resolution.</p>')
  assert.equal(f.scene().hash, 'new-edit')
  assert.equal(f.hasPendingManuscriptWrites(), false)
})
