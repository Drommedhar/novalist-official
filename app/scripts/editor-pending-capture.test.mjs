import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

test('captured typing stays clean after its delayed report without resetting content or dropping later edits', () => {
  let capture, resets = 0, html = '<p>Saved.</p>'
  const updates = []
  const state = {
    editors: { pane: { sceneId: 'scene', html, plainText: 'Saved.' } },
    onEditorContentChanged(pane, nextHtml, plainText) {
      updates.push({ pane, html: nextHtml, plainText })
      state.editors[pane] = { sceneId: 'scene', html: nextHtml, plainText, isDirty: true }
    }
  }
  const live = {
    setSceneContext() {},
    setContent(value) { resets++; html = value },
    getContent: () => html,
    getPlainText: () => 'Saved. Typed now.'
  }
  const hooks = loadRendererSource('src/views/editor/useEditorSynchronization.ts', {
    react: { useEffect: effect => effect(), useLayoutEffect: effect => effect() },
    '../../stores/editorBridgeStore': {},
    '../../stores/shellStore': {},
    '../../stores/projectStore': {
      editorPane: (store, pane) => store.editors[pane],
      useProjectStore: { getState: () => state }
    },
    '../../stores/pendingWrites': {
      registerPendingWrite(_flush, takeSnapshot) { capture = takeSnapshot; return () => {} }
    }
  })
  const model = {
    editorRef: { current: live }, pane: 'pane', openSceneId: 'scene', sceneHtml: html,
    lastReportedHtmlRef: { current: null }, loadingRef: { current: false },
    ownAnnotationsRef: { current: 0 }, annotationsRevision: 0,
    loadAnnotations: async () => false, pushEntityNames: async () => {}
  }
  hooks.useEditorScene(model)
  hooks.useEditorPersistence(model)
  html = '<p>Saved. Typed now.</p>'
  capture()
  hooks.useEditorScene({ ...model, sceneHtml: state.editors.pane.html })
  capture()
  assert.equal(resets, 1, 'capturing must preserve the live DOM, selection and undo history')
  assert.deepEqual(updates, [{ pane: 'pane', html: '<p>Saved. Typed now.</p>', plainText: 'Saved. Typed now.' }])

  const { createEditorContentMessages } = loadRendererSource('src/views/editor/editorContentMessages.ts', {
    '../../stores/editorBridgeStore': {},
    '../../stores/projectStore': { useProjectStore: { getState: () => state } },
    '../../stores/shellStore': { useShellStore: { getState: () => ({ focusMode: false }) } }
  })
  const handlers = createEditorContentMessages(model)
  state.editors.pane.isDirty = false
  handlers.contentChanged({ html, plainText: live.getPlainText() })
  assert.equal(state.editors.pane.isDirty, false, 'the delayed report must not dirty the saved capture')
  assert.equal(updates.length, 1)

  html = '<p>Saved. Typed now. Still editing.</p>'
  handlers.contentChanged({ html, plainText: 'Saved. Typed now. Still editing.' })
  assert.equal(state.editors.pane.isDirty, true)
  assert.equal(state.editors.pane.html, html)
  assert.equal(updates.length, 2, 'a different edit must still reach the store')
})
