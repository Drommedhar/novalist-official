import { test } from 'node:test'
import assert from 'node:assert/strict'
import { loadRendererSource } from './renderer-source-loader.mjs'
import { SceneNotesDocument } from '../src/renderer/src/shell/sceneNotesDocument.ts'
import { SharedSceneNotesDocument } from '../src/renderer/src/shell/sharedSceneNotesDocument.ts'
import { normalizeChecklist } from '../src/renderer/src/shell/sceneChecklist.ts'
import * as pendingWrites from '../src/renderer/src/stores/pendingWrites.ts'

const turn = () => new Promise((resolve) => setImmediate(resolve))
function harness() {
  let effects = []
  let disk = 'saved'
  let offline = false
  const context = { projectPath: '/notes-hook', bookId: 'book', draftId: 'draft', epoch: 1, chapterGuid: 'chapter', sceneId: 'scene', title: 'Scene' }
  const state = { projectPath: context.projectPath, activeBookId: context.bookId, activeDraftId: context.draftId,
    chapters: [{ guid: 'chapter', scenes: [{ id: 'scene' }] }] }
  const { useSceneNotesDocument } = loadRendererSource('src/shell/useSceneNotesDocument.ts', {
    react: { useEffect: (effect) => effects.push(effect), useMemo: (create) => create(), useSyncExternalStore: (_subscribe, snapshot) => snapshot() },
    '../rpc/client': { rpc: { request: async (method, args) => {
      if (method === 'scenes/getMeta') return { notes: disk }
      if (offline) throw new Error('offline')
      disk = args[2]
    } } },
    '../stores/pendingWrites': pendingWrites,
    '../stores/projectStore': { useProjectStore: Object.assign(select => select(state), { getState: () => state }) },
    './sceneNotesDocument': { SceneNotesDocument },
    './sharedSceneNotesDocument': { SharedSceneNotesDocument },
    '../collaboration/sharedMarkdown': { openSharedMarkdown: () => { throw new Error('Local notes must not open a shared document') } },
    './sceneChecklist': { normalizeChecklist }
  })
  const mount = async (epoch) => {
    effects = []
    const result = useSceneNotesDocument({ ...context, epoch })
    const cleanups = effects.map((effect) => effect())
    await turn()
    return { ...result, unmount: () => cleanups.forEach((cleanup) => cleanup?.()) }
  }
  return { mount, state, get disk() { return disk }, set offline(value) { offline = value } }
}

test('backend recovery rejoins unsaved notes instead of replaying an older epoch over new edits', async () => {
  const h = harness()
  const old = await h.mount(7)
  h.offline = true
  old.document.update('old unsaved edit')
  await assert.rejects(old.document.flush())
  old.unmount()
  await turn()
  h.offline = false
  const recovered = await h.mount(2)
  recovered.document.update('new recovered edit')
  await recovered.document.flush()
  await pendingWrites.flushPendingWrites()
  assert.equal(h.disk, 'new recovered edit')
  assert.equal(recovered.document, old.document)
  recovered.unmount()
})

test('undo after a failed save supersedes the failed payload before global flush', async () => {
  const h = harness()
  const current = await h.mount(1)
  h.offline = true
  current.document.update('failed edit')
  await assert.rejects(current.document.flush())
  current.document.update('saved')
  h.offline = false
  await current.document.flush()
  await pendingWrites.flushPendingWrites()
  assert.equal(h.disk, 'saved')
  current.unmount()
})
