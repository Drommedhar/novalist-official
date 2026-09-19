import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

for (const next of ['new', 'existing', 'direct'] as const) {
  test(`project switch to ${next} clears every old editor pane`, async () => {
    const h = await launchApp('nl-project-switch-')
    try {
      await seedBook(h, { 'Destination chapter': ['Destination scene'] }, 'Destination')
      const destination = await h.page.evaluate(() => window.novalistStores.project.getState().projectPath!)
      await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
      const book = await seedBook(h, { 'Source chapter': ['Source scene', 'Split scene'] }, 'Source')
      await dismissTour(h.page)
      await h.page.evaluate(async (chapter) => {
        const store = window.novalistStores.project.getState()
        await store.openScene(chapter.guid, chapter.scenes[0].id)
        await store.openSceneInSplit(chapter.guid, chapter.scenes[1].id)
      }, book.chapters[0])
      await expect(h.page.locator('.editor-frame')).toHaveCount(2)

      if (next !== 'direct') {
        await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
        await expect(h.page.locator('.start-screen')).toBeVisible()
      }
      if (next === 'new') {
        // Same create/apply path as CreateProjectDialog.
        await h.page.evaluate(async (dir) => {
          const state = await window.novalistRpc.request('project/create', [dir, 'New project', 'New book'])
          window.novalistStores.project.getState().applyState(state as never)
        }, h.workDir)
      } else {
        await h.page.evaluate((path) => window.novalistStores.project.getState().openProject(path), destination)
      }
      await enterWriting(h.page)
      await expect(h.page.locator('.editor-frame')).toHaveCount(0)
      expect(await h.page.evaluate(() => {
        const s = window.novalistStores.project.getState()
        return { editors: s.editors, scene: s.openSceneId, tabs: s.openTabs, hashes: s.sceneHashes, conflict: s.sceneConflict }
      })).toEqual({ editors: {}, scene: null, tabs: [], hashes: {}, conflict: null })
      if (next !== 'new') {
        await h.page.locator('.binder-scene-row').first().click()
        await expect(h.page.locator('.editor-frame')).toHaveCount(1)
      }
    } finally {
      await h.close()
    }
  })
}

for (const action of ['close', 'open', 'create'] as const) {
  test(`${action} project saves the editor before its pending autosave`, async () => {
    const h = await launchApp('nl-project-save-')
    try {
      await seedBook(h, {}, 'Destination')
      const destination = await h.page.evaluate(() => window.novalistStores.project.getState().projectPath!)
      await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
      const book = await seedBook(h, { Chapter: ['Scene'] }, 'Source')
      await dismissTour(h.page)
      await h.page.evaluate((chapter) =>
        window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id),
      book.chapters[0])
      const editor = h.page.frameLocator('.editor-frame').locator('#editor')
      await expect(editor).toBeVisible()
      const source = await h.page.evaluate(() => window.novalistStores.project.getState().projectPath!)
      if (action === 'create') {
        await h.app.evaluate(({ dialog }, dir) => {
          dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [dir] })
        }, h.workDir)
        await h.page.evaluate(() => window.novalistStores.shell.getState().openDialog('createProject'))
        const dialog = h.page.getByRole('dialog', { name: 'Create New Project' })
        await dialog.getByPlaceholder('My Novel', { exact: true }).fill('Created while editing')
        await dialog.getByRole('button', { name: 'Browse...', exact: true }).click()
        await expect(dialog.locator('.dialog-button.primary')).toBeEnabled()
      }

      // Change the live iframe and leave in the same task: neither the iframe's
      // change debounce nor the two-second autosave has had a chance to run.
      await h.page.evaluate(async ({ action, destination }) => {
        const frame = document.querySelector<HTMLIFrameElement>('.editor-frame')!
        const editor = frame.contentDocument!.querySelector<HTMLElement>('#editor')!
        editor.innerHTML = '<p>Last words before leaving.</p>'
        editor.dispatchEvent(new Event('input', { bubbles: true }))
        const store = window.novalistStores.project.getState()
        if (action === 'close') await store.closeProject()
        else if (action === 'open') await store.openProject(destination)
        else document.querySelector<HTMLButtonElement>('.dialog-card .dialog-button.primary')!.click()
      }, { action, destination })
      if (action === 'create') {
        await expect.poll(() => h.page.evaluate(() =>
          window.novalistStores.project.getState().projectName
        )).toBe('Created while editing')
      }
      await h.page.evaluate((path) => window.novalistStores.project.getState().openProject(path), source)
      const content = await h.rpc<{ html: string }>('scenes/read', [book.chapters[0].guid, book.chapters[0].scenes[0].id])
      expect(content.html).toContain('Last words before leaving.')
    } finally {
      await h.close()
    }
  })
}
