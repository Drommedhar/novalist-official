import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'
import * as pendingWrites from '../src/renderer/src/stores/pendingWrites.ts'

function fixture({ saved = '', live = 'Freshly typed prose', hasScene = true, failSave = false } = {}) {
  const events = []
  let disk = saved, stopPersistence
  const pane = { chapterGuid: 'chapter', sceneId: hasScene ? 'scene' : null, html: saved, isDirty: false }
  const editorRef = { current: { capturePendingContentChange: () => {
    events.push('capture')
    return live === saved ? null : { html: live, plainText: live }
  } } }
  const state = {
    isLoaded: true, closingProject: false, editors: { editor: pane }, recentProjects: [], failSave,
    onEditorContentChanged(id, html) {
      assert.equal(id, 'editor')
      events.push('buffer')
      pane.html = html; pane.isDirty = true
    },
    async flushPane(id) {
      assert.equal(id, 'editor')
      if (!pane.isDirty) return
      events.push('save')
      if (state.failSave) throw new Error('write unavailable')
      disk = pane.html; pane.isDirty = false
    },
    async flushPendingSave() { await state.flushPane('editor') },
    applyState(next) { Object.assign(state, next) }
  }
  const set = patch => {
    Object.assign(state, patch)
    if (patch.closingProject) {
      // Model a synchronous subscriber disposing its iframe at the close
      // boundary. Its last edit must already be captured before any await.
      events.push('unmount')
      editorRef.current = null
      stopPersistence?.()
    }
  }
  const store = { getState: () => state, setState: set }
  const { useEditorPersistence } = loadRendererSource('src/views/editor/useEditorSynchronization.ts', {
    react: { useEffect: effect => { stopPersistence = effect() } },
    '../../stores/editorBridgeStore': {}, '../../stores/shellStore': {},
    '../../stores/projectStore': { useProjectStore: store, editorPane: (state, id) => state.editors[id] },
    '../../stores/pendingWrites': pendingWrites,
    '../../collaboration/sharedEditors': { captureSharedEditor: () => false }
  })
  useEditorPersistence({ pane: 'editor', openSceneId: pane.sceneId, editorRef, lastReportedHtmlRef: { current: saved } })
  const { createProjectLibraryActions } = loadRendererSource('src/stores/projectLibraryActions.ts', {
    './pendingWrites': pendingWrites,
    './projectStore': { useProjectStore: store },
    './projectSceneActions': { runSceneContext: action => action() },
    './settingsStore': { useSettingsStore: { getState: () => ({ load: async () => {} }) } },
    '../rpc/client': { rpc: { request: async method => {
      if (method === 'project/close') { events.push('close'); return { isLoaded: false } }
      if (method === 'project/recent') return []
      throw new Error(`Unexpected RPC ${method}`)
    } } }
  })
  Object.assign(state, createProjectLibraryActions(set, () => state))
  return { state, pane, events, disk: () => disk, dispose: () => stopPersistence?.() }
}

test('Library captures fresh iframe edits before publishing the closing state', async () => {
  const f = fixture()
  try {
    await f.state.closeProject()
    assert.equal(f.disk(), 'Freshly typed prose')
    assert.deepEqual(f.events, ['capture', 'buffer', 'unmount', 'save', 'close'])
    assert.equal(f.state.isLoaded, false)
  } finally { f.dispose() }
})

test('Library preserves an intentionally emptied scene before unmount', async () => {
  const f = fixture({ saved: 'Old prose', live: '' })
  try { await f.state.closeProject(); assert.equal(f.disk(), '') }
  finally { f.dispose() }
})

test('failed save leaves the project open and its captured draft available for retry', async () => {
  const f = fixture({ failSave: true })
  try {
    await assert.rejects(f.state.closeProject(), /write unavailable/)
    assert.equal(f.state.isLoaded, true)
    assert.equal(f.state.closingProject, false)
    assert.equal(f.pane.html, 'Freshly typed prose')
    assert.equal(f.pane.isDirty, true)
    assert.equal(f.events.includes('close'), false)
    f.state.failSave = false
    await f.state.closeProject()
    assert.equal(f.disk(), 'Freshly typed prose')
    assert.equal(f.state.isLoaded, false)
  } finally { f.dispose() }
})

test('a second Library action cannot start another close while one is active', async () => {
  const f = fixture()
  try {
    f.state.closingProject = true
    await f.state.closeProject()
    assert.deepEqual(f.events, [])
  } finally { f.dispose() }
})

test('a retained iframe cannot capture into a pane with no current scene', async () => {
  const f = fixture({ hasScene: false })
  try {
    await f.state.closeProject()
    assert.equal(f.disk(), '')
    assert.deepEqual(f.events, ['unmount', 'close'])
  } finally { f.dispose() }
})


test('Library keeps the backend project open until pending collaboration writes finish', async () => {
  const f = fixture()
  let release
  const blocked = new Promise(resolve => { release = resolve })
  const stopSharedWrite = pendingWrites.registerPendingWrite(async () => {
    f.events.push('shared-save')
    await blocked
    f.events.push('shared-saved')
  })
  try {
    const closing = f.state.closeProject()
    await new Promise(setImmediate)
    assert.equal(f.state.isLoaded, true)
    assert.equal(f.state.closingProject, true)
    assert.equal(f.events.includes('close'), false)
    release()
    await closing
    assert.ok(f.events.indexOf('shared-saved') < f.events.indexOf('close'))
    assert.equal(f.disk(), 'Freshly typed prose')
    assert.equal(f.state.isLoaded, false)
  } finally { release(); stopSharedWrite(); f.dispose() }
})
