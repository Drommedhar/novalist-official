import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('clicking editor iframe prose after the Dashboard restores the focused pane without losing the caret', async () => {
  const h = await launchApp('nl-pane-iframe-focus-')
  try {
    const book = await seedBook(h, { Chapter: ['Opening'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    const prose = 'The lantern lights the room.'
    await dismissTour(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    await h.rpc('scenes/write', [chapter.guid, scene.id, `<p>${prose}</p>`, prose])
    const editorPaneId = await h.page.evaluate(async ({ chapterGuid, sceneId }) => {
      const shell = window.novalistStores.shell.getState()
      shell.goHome()
      shell.splitActivePane('row')
      const paneId = window.novalistStores.shell.getState().activePaneId
      await window.novalistStores.project.getState().openSceneIn(paneId, chapterGuid, sceneId)
      return paneId
    }, { chapterGuid: chapter.guid, sceneId: scene.id })
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText(prose)

    // Clicking the actual Dashboard makes its pane active and removes Write's
    // sidebar. The return click goes inside the iframe, not through its header.
    await h.page.locator('.pane-leaf[data-view="dashboard"] .dashboard-title').click()
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'dashboard')
    await expect(h.page.locator('.binder')).toHaveCount(0)
    await editor.locator('p').click()
    await expect.poll(() => h.page.evaluate(() => {
      const shell = window.novalistStores.shell.getState()
      const project = window.novalistStores.project.getState()
      return {
        view: shell.mainView,
        mode: shell.mode,
        pane: shell.activePaneId,
        editor: project.activeEditorPaneId,
        scene: project.openSceneId
      }
    })).toEqual({ view: 'write', mode: 'write', pane: editorPaneId, editor: editorPaneId, scene: scene.id })
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'Write')
    await expect(h.page.locator('.binder-scene-row.active .binder-scene-title')).toHaveText('Opening')

    // Restoring the sidebar must keep the click's caret in this document so the
    // next keystrokes edit exactly where the writer placed it.
    const caret = await editor.evaluate((element) => {
      const selection = element.ownerDocument.getSelection()!
      const prefix = element.ownerDocument.createRange()
      prefix.selectNodeContents(element)
      prefix.setEnd(selection.anchorNode!, selection.anchorOffset)
      return {
        inside: element.contains(selection.anchorNode),
        collapsed: selection.isCollapsed,
        offset: prefix.toString().length,
        text: element.textContent ?? ''
      }
    })
    expect(caret.inside).toBe(true)
    expect(caret.collapsed).toBe(true)
    await h.page.keyboard.type(' More light.')
    const edited = `${caret.text.slice(0, caret.offset)} More light.${caret.text.slice(caret.offset)}`
    await expect(editor).toHaveText(edited)
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().openScenePlainText)).toContain('More light.')
    await expect(h.page.locator('.pane-leaf[data-view="dashboard"]')).toHaveCount(1)

    // Background settings reach an iframe that still remembers its last focused
    // element. They must not take focus back from the Dashboard.
    await h.page.locator('.pane-leaf[data-view="dashboard"] .dashboard-title').click()
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'dashboard')
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { spellCheckEnabled: false }))
    await expect.poll(() => editor.evaluate((element) => (element as HTMLElement).spellcheck)).toBe(false)
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'dashboard')
    await expect(h.page.locator('.binder')).toHaveCount(0)

    // Keyboard/accessibility entry produces focus without a pointer event.
    await editor.evaluate((element) => (element as HTMLElement).focus())
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'write')
    await expect(h.page.locator('.binder-scene-row.active .binder-scene-title')).toHaveText('Opening')
    expect(await h.page.evaluate(() => window.novalistStores.shell.getState().activePaneId)).toBe(editorPaneId)
    await h.page.keyboard.type(' Focus returned.')
    await expect(editor).toContainText('Focus returned.')
  } finally {
    await h.close()
  }
})

test('clicking between editor iframes follows each scene and routes typing to that pane', async () => {
  const h = await launchApp('nl-two-iframe-focus-')
  try {
    const book = await seedBook(h, { Chapter: ['Left scene', 'Right scene'] })
    const chapter = book.chapters[0]
    await dismissTour(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    for (const scene of chapter.scenes) {
      await h.rpc('scenes/write', [chapter.guid, scene.id, `<p>${scene.title} prose.</p>`, `${scene.title} prose.`])
    }
    const paneIds = await h.page.evaluate(async ({ chapterGuid, sceneIds }) => {
      const shell = window.novalistStores.shell.getState()
      shell.setMainView('write')
      const left = window.novalistStores.shell.getState().activePaneId
      await window.novalistStores.project.getState().openSceneIn(left, chapterGuid, sceneIds[0])
      shell.splitActivePane('row')
      const right = window.novalistStores.shell.getState().activePaneId
      await window.novalistStores.project.getState().openSceneIn(right, chapterGuid, sceneIds[1])
      return [left, right]
    }, { chapterGuid: chapter.guid, sceneIds: chapter.scenes.map((scene) => scene.id) })
    const editors = [0, 1].map((index) => h.page.locator('.pane-leaf').nth(index).frameLocator('.editor-frame').locator('#editor'))
    await expect(editors[0]).toHaveText('Left scene prose.')
    await expect(editors[1]).toHaveText('Right scene prose.')

    for (const [turn, index] of [0, 1, 0].entries()) {
      await editors[index].locator('p').click()
      await expect.poll(() => h.page.evaluate(() => ({
        pane: window.novalistStores.shell.getState().activePaneId,
        editor: window.novalistStores.project.getState().activeEditorPaneId,
        scene: window.novalistStores.project.getState().openSceneId
      }))).toEqual({ pane: paneIds[index], editor: paneIds[index], scene: chapter.scenes[index].id })
      await expect(h.page.locator('.binder-scene-row.active .binder-scene-title')).toHaveText(chapter.scenes[index].title)
      const marker = ` Keystroke${turn}`
      await h.page.keyboard.type(marker)
      await expect(editors[index]).toContainText(marker)
      await expect(editors[1 - index]).not.toContainText(marker)
    }
  } finally {
    await h.close()
  }
})

test('switching from a split Dashboard to Write shows and opens scenes in the new pane', async () => {
  const h = await launchApp('nl-dashboard-split-')
  try {
    await seedBook(h, { Chapter: ['Opening'] })
    await dismissTour(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    const newPaneId = await h.page.evaluate(() => {
      const shell = window.novalistStores.shell.getState()
      shell.goHome()
      shell.splitActivePane('row')
      return window.novalistStores.shell.getState().activePaneId
    })
    await h.page.locator('.mode-rail-item[data-mode="write"]').click()
    await expect(h.page.locator('.binder-scene-title', { hasText: 'Opening' })).toBeVisible()
    await h.page.locator('.binder-scene-title', { hasText: 'Opening' }).click()
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'write')
    await expect(h.page.locator('.pane-leaf[data-view="write"] .editor-frame')).toBeVisible()
    expect(await h.page.evaluate(() => window.novalistStores.shell.getState().activePaneId)).toBe(newPaneId)
    await expect(h.page.locator('.pane-leaf[data-view="dashboard"]')).toHaveCount(1)
  } finally {
    await h.close()
  }
})

test('split pane navigation keeps the scene binder and mode panel in sync', async () => {
  const h = await launchApp('nl-pane-navigation-')
  try {
    await seedBook(h, { Chapter: ['Opening'] })
    await dismissTour(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    await h.page.evaluate(() => {
      const shell = window.novalistStores.shell.getState()
      shell.goHome()
      shell.splitActivePane('row')
    })
    await expect(h.page.locator('.pane-leaf[data-view="dashboard"]')).toHaveCount(2)

    // Follow the reported path through the pane's own picker.
    await h.page.locator('.pane-leaf').last().locator('.pane-header-view').click()
    await h.page.getByRole('menuitemradio', { name: 'Editor', exact: true }).click()
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'write')
    await expect(h.page.locator('.binder-scene-title', { hasText: 'Opening' })).toBeVisible()
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'Write')

    // Picking a view from another mode must update the left panel too.
    await h.page.locator('.pane-leaf').first().locator('.pane-header-view').click()
    await h.page.getByRole('menuitemradio', { name: 'Codex', exact: true }).click()
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'World')
    await expect(h.page.locator('.mode-panel-row[data-view="codex"]')).toHaveAttribute('aria-current', 'true')
    await expect(h.page.locator('.binder')).toHaveCount(0)

    await h.page.locator('.pane-leaf[data-view="write"] .pane-header-view').focus()
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'Write')
    await expect(h.page.locator('.binder-scene-title', { hasText: 'Opening' })).toBeVisible()
    await h.page.evaluate(() => window.novalistStores.shell.getState().saveLayout('Mixed panes'))

    // Closing the active pane selects a surviving view and its mode together.
    await h.page.locator('.pane-leaf[data-view="write"]').getByRole('button', { name: 'Close pane', exact: true }).click()
    await expect(h.page.locator('.pane-leaf')).toHaveCount(1)
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'World')
    await expect(h.page.locator('.binder')).toHaveCount(0)

    // A saved arrangement selects its first pane, including that pane's mode.
    await h.page.locator('.mode-rail-item[data-mode="write"]').click()
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'Write')
    await h.page.evaluate(() => window.novalistStores.shell.getState().applyLayout('Mixed panes'))
    await expect(h.page.locator('.pane-leaf')).toHaveCount(2)
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'codex')
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'World')

    // Splitting a named, inactive pane selects the newly created copy.
    await h.page.evaluate(() => {
      const shell = window.novalistStores.shell.getState()
      if (shell.panes.kind !== 'split') throw new Error('Expected the saved split layout')
      shell.splitPaneById(shell.panes.children[1].id, 'column')
    })
    await expect(h.page.locator('.pane-leaf')).toHaveCount(3)
    await expect(h.page.locator('.pane-leaf.active')).toHaveAttribute('data-view', 'write')
    await expect(h.page.locator('.mode-panel')).toHaveAttribute('aria-label', 'Write')
    await expect(h.page.locator('.binder-scene-title', { hasText: 'Opening' })).toBeVisible()
  } finally {
    await h.close()
  }
})
