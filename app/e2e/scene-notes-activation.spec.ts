import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

for (const fullscreen of [false, true]) {
  test(`opening notes preserves the ${fullscreen ? 'fullscreen' : 'normal'} writing window`, async () => {
    const h = await launchApp('nl-notes-activation-')
    try {
      const book = await seedBook(h, { Opening: ['Arrival'] })
      await dismissTour(h.page)
      await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), book.chapters[0])
      if (fullscreen) {
        await h.app.evaluate(({ BrowserWindow }) => new Promise<void>((resolve) => {
          const main = BrowserWindow.getAllWindows()[0]
          main.once('enter-full-screen', () => resolve())
          main.setFullScreen(true)
        }))
      }
      await h.app.evaluate(({ BrowserWindow }) => {
        const main = BrowserWindow.getAllWindows()[0] as Electron.BrowserWindow & { notesVisibilityEvents: string[] }
        main.notesVisibilityEvents = []
        main.on('hide', () => main.notesVisibilityEvents.push('hide'))
        main.on('minimize', () => main.notesVisibilityEvents.push('minimize'))
        main.on('leave-full-screen', () => main.notesVisibilityEvents.push('leave-full-screen'))
      })
      const opened = h.app.waitForEvent('window')
      await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
      const notes = await opened
      await expect(notes.getByRole('heading', { name: 'Arrival', exact: true })).toBeVisible()
      const nativeNotes = await h.app.browserWindow(notes)
      expect(await nativeNotes.evaluate((win) => ({
        owned: !!win.getParentWindow(), globalTop: win.isAlwaysOnTop(), allSpaces: win.isVisibleOnAllWorkspaces(),
        fullscreenable: win.isFullScreenable(), minimizable: win.isMinimizable(), maximizable: win.isMaximizable()
      }))).toEqual({ owned: true, globalTop: false, allSpaces: false, fullscreenable: false, minimizable: false, maximizable: false })
      await expect(notes.getByText('Saved', { exact: true })).toHaveCount(0)
      await expect(notes.locator('.scene-notes-titlebar')).toHaveCSS('-webkit-app-region', 'drag')
      await expect(notes.getByRole('button', { name: 'Hide scene notes', exact: true })).toHaveCSS('-webkit-app-region', 'no-drag')
      if (await notes.evaluate(() => window.novalist.material !== 'opaque')) {
        await expect(notes.locator('body')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
        await expect(notes.locator('.scene-notes-window')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
      }
      await notes.getByRole('textbox', { name: 'Add a scene goal', exact: true }).fill('Keep writing visible')
      await notes.getByRole('button', { name: 'Add a scene goal', exact: true }).click()
      if (!fullscreen) await notes.screenshot({ path: test.info().outputPath('notes-panel.png') })
      const visibility = () => h.app.evaluate(async ({ app, BrowserWindow }) => {
        const main = BrowserWindow.getAllWindows().find((win) => !win.webContents.getURL().includes('sceneNotes='))! as Electron.BrowserWindow & { notesVisibilityEvents: string[] }
        return { visible: main.isVisible(), minimized: main.isMinimized(), fullscreen: main.isFullScreen(),
          appHidden: process.platform === 'darwin' && app.isHidden(), events: main.notesVisibilityEvents,
          documentVisibility: await main.webContents.executeJavaScript('document.visibilityState') }
      })
      expect(await visibility()).toEqual({ visible: true, minimized: false, fullscreen, appHidden: false, events: [], documentVisibility: 'visible' })
      await notes.getByRole('button', { name: 'Hide scene notes', exact: true }).click()
      await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
      await expect(notes.getByRole('checkbox', { name: 'Keep writing visible', exact: true })).toBeVisible()
      expect(await visibility()).toEqual({ visible: true, minimized: false, fullscreen, appHidden: false, events: [], documentVisibility: 'visible' })
    } finally { await h.close() }
  })
}
