import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

function fixture() {
  const shell = { mobileLayout: 'phone', mobileTab: 'manuscript', mainView: 'write', setMobileTab(tab) { this.mobileTab = tab }, setMainView(view) { this.mainView = view } }
  const project = { isLoaded: true, activeEditorPaneId: 'pane', openSceneId: 'scene', async closeTab() { this.openSceneId = null }, async closeProject() { this.isLoaded = false } }
  const module = loadRendererSource('src/mobile/backNavigation.ts', {
    '../stores/shellStore': { useShellStore: { getState: () => shell } },
    '../stores/projectStore': { useProjectStore: { getState: () => project } }
  })
  return { shell, project, back: module.handleMobileBack }
}

test('Back closes the editor through its save-aware action before leaving the project', async () => {
  const f = fixture()
  await f.back()
  assert.equal(f.project.openSceneId, null)
  assert.equal(f.project.isLoaded, true)
})

test('Back retains an editor whose save fails', async () => {
  const f = fixture()
  f.project.closeTab = async () => { throw new Error('save failed') }
  await assert.rejects(f.back(), /save failed/)
  assert.equal(f.project.openSceneId, 'scene')
  assert.equal(f.project.isLoaded, true)
})

test('Back returns to dashboard before closing the project', async () => {
  const f = fixture()
  f.project.openSceneId = null
  await f.back()
  assert.equal(f.shell.mobileTab, 'dashboard')
  assert.equal(f.project.isLoaded, true)
  await f.back()
  assert.equal(f.project.isLoaded, false)
})

test('tablet Back returns to dashboard without closing an open scene', async () => {
  const f = fixture()
  f.shell.mobileLayout = 'tablet'
  f.shell.mainView = 'codex'
  await f.back()
  assert.equal(f.shell.mainView, 'dashboard')
  assert.equal(f.project.openSceneId, 'scene')
  assert.equal(f.project.isLoaded, true)
})
