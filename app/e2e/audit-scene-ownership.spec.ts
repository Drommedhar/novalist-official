import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

async function splitScenes(h: Harness, chapterGuid: string, sceneIds: string[]): Promise<string[]> {
  return h.page.evaluate(async ({ chapterGuid, sceneIds }) => {
    const shell = window.novalistStores.shell.getState()
    shell.setMainView('write')
    const left = window.novalistStores.shell.getState().activePaneId
    await window.novalistStores.project.getState().openSceneIn(left, chapterGuid, sceneIds[0])
    shell.splitActivePane('row')
    const right = window.novalistStores.shell.getState().activePaneId
    await window.novalistStores.project.getState().openSceneIn(right, chapterGuid, sceneIds[1])
    return [left, right]
  }, { chapterGuid, sceneIds })
}

for (const savedFirst of [0, 1]) {
  test(`independent split drafts survive when pane ${savedFirst + 1} saves first`, async () => {
    const h = await launchApp('nl-audit-split-ownership-')
    try {
      const book = await seedBook(h, { Chapter: ['Shared scene'] })
      await dismissTour(h.page)
      const chapter = book.chapters[0]
      const scene = chapter.scenes[0]
      await h.rpc('scenes/write', [chapter.guid, scene.id, '<p>Original</p>', 'Original'])
      const panes = await splitScenes(h, chapter.guid, [scene.id, scene.id])
      const editors = [0, 1].map(index => h.page.locator('.pane-leaf').nth(index).frameLocator('.editor-frame').locator('#editor'))
      for (const editor of editors) await expect(editor).toHaveText('Original')
      const drafts = ['Left independent draft', 'Right independent draft']
      await editors[0].fill(drafts[0])
      await editors[1].fill(drafts[1])
      await expect.poll(() => h.page.evaluate((panes) =>
        panes.map(pane => window.novalistStores.project.getState().editors[pane].isDirty), panes)
      ).toEqual([true, true])
      await h.page.evaluate(pane => window.novalistStores.project.getState().flushPane(pane), panes[savedFirst])
      expect((await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain(drafts[savedFirst])
      await expect(editors[1 - savedFirst]).toHaveText(drafts[1 - savedFirst])
      await h.page.evaluate(pane => window.novalistStores.project.getState().flushPane(pane), panes[1 - savedFirst])
      const dialog = h.page.locator('.scene-conflict-card')
      await expect(dialog).toBeVisible()
      const state = await h.page.evaluate((panes) => {
        const store = window.novalistStores.project.getState()
        return { conflict: store.sceneConflict, panes: panes.map(pane => ({ html: store.editors[pane].html, dirty: store.editors[pane].isDirty })) }
      }, panes)
      expect(state.conflict?.mine).toContain(drafts[1 - savedFirst])
      expect(state.conflict?.theirs).toContain(drafts[savedFirst])
      expect(state.panes[1 - savedFirst].dirty).toBe(true)
      expect(state.panes[0].html).toContain(drafts[0])
      expect(state.panes[1].html).toContain(drafts[1])
      expect((await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain(drafts[savedFirst])
    } finally { await h.close() }
  })
}

test('moving a tab to another pane preserves its original buffer when saving conflicts', async () => {
  const h = await launchApp('nl-audit-move-conflict-')
  try {
    const book = await seedBook(h, { Chapter: ['Source', 'Destination'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    const [source, destination] = chapter.scenes
    await h.rpc('scenes/write', [chapter.guid, source.id, '<p>Original source</p>', 'Original source'])
    await h.rpc('scenes/write', [chapter.guid, destination.id, '<p>Destination prose</p>', 'Destination prose'])
    const panes = await splitScenes(h, chapter.guid, [source.id, destination.id])
    const left = h.page.locator('.pane-leaf').nth(0)
    const editor = left.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('Original source')
    await editor.fill('Unsaved source must stay here')
    await expect.poll(() => h.page.evaluate(pane => window.novalistStores.project.getState().editors[pane].isDirty, panes[0])).toBe(true)
    await h.rpc('scenes/write', [chapter.guid, source.id, '<p>Saved elsewhere</p>', 'Saved elsewhere'])
    await left.locator('.editor-tab.active').click({ button: 'right' })
    await left.locator('.editor-tab-menu button').nth(1).click()
    await expect(h.page.locator('.scene-conflict-card')).toBeVisible()
    await expect(h.page.locator('.pane-leaf')).toHaveCount(2)
    await expect(editor).toHaveText('Unsaved source must stay here')
    await expect(h.page.locator('.pane-leaf').nth(1).frameLocator('.editor-frame').locator('#editor')).toHaveText('Destination prose')
    const state = await h.page.evaluate(panes => panes.map(pane => {
      const editor = window.novalistStores.project.getState().editors[pane]
      return { sceneId: editor.sceneId, dirty: editor.isDirty, tabs: editor.tabs.map(tab => tab.sceneId) }
    }), panes)
    expect(state[0]).toEqual({ sceneId: source.id, dirty: true, tabs: [source.id] })
    expect(state[1]).toEqual({ sceneId: destination.id, dirty: false, tabs: [destination.id] })
    expect((await h.rpc<{ html: string }>('scenes/read', [chapter.guid, source.id])).html).toBe('<p>Saved elsewhere</p>')
  } finally { await h.close() }
})
