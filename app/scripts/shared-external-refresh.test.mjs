import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

function fixture(shared = false) {
  let resolveRead, writes = 0
  const requests = []
  let state = { isLoaded: true, workspaceEpoch: 1, projectPath: '/project', activeBookId: 'book', activeDraftId: 'draft',
    chapters: [{ scenes: [{ id: 'scene', ...(shared ? { sharedProse: { sessionId: 'session', fields: {} } } : {}) }] }],
    editors: {}, sceneHashes: {} }
  const { refreshExternalScenes } = loadRendererSource('src/stores/externalSceneRefresh.ts', {
    '../rpc/client': { rpc: { request: (method, args) => { requests.push({ method, args }); return new Promise(resolve => { resolveRead = resolve }) } } },
    './pendingWrites': { capturePendingWrites() {} },
    './projectStore': { mirror: () => ({}), useProjectStore: { getState: () => state, setState: patch => { writes++; state = { ...state, ...(typeof patch === 'function' ? patch(state) : patch) } } } },
    './projectPersistence': { stripHtml: value => value },
    './manuscriptStore': { useManuscriptStore: { getState: () => ({ sections: [] }) }, applyExternalManuscriptScene() { writes++ } }
  })
  return { refreshExternalScenes, requests, writes: () => writes,
    startSharing: () => { state = { ...state, chapters: [{ scenes: [{ id: 'scene', sharedProse: { sessionId: 'session', fields: {} } }] }] } },
    respond: () => resolveRead({ counts: [{ sceneId: 'scene', wordCount: 1 }], changes: [] }) }
}

test('application focus leaves live shared prose to collaboration instead of reconciling a stale disk snapshot', async () => {
  const f = fixture(true)
  const pending = f.refreshExternalScenes()
  const count = f.requests.length
  if (count) f.respond()
  await pending
  assert.equal(count, 0)
  assert.equal(f.writes(), 0)
})

test('a disk refresh started before sharing cannot publish into the newly shared workspace', async () => {
  const f = fixture()
  const pending = f.refreshExternalScenes()
  assert.equal(f.requests.length, 1)
  f.startSharing()
  f.respond()
  await pending
  assert.equal(f.writes(), 0)
})

test('ordinary local projects still adopt external word-count updates', async () => {
  const f = fixture()
  const pending = f.refreshExternalScenes()
  f.respond()
  await pending
  assert.equal(f.writes(), 1)
})
