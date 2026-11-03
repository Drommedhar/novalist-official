import { test, expect, type Page } from '@playwright/test'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

async function recordMainErrors(h: Harness): Promise<string> {
  const path = join(h.workDir, 'main-errors.log')
  await h.app.evaluate(({ dialog }, path) => {
    const { appendFileSync } = process.getBuiltinModule('node:fs')
    process.on('uncaughtExceptionMonitor', error => appendFileSync(path, `${error.stack ?? error}\n`))
    // Capture Electron's fatal-error dialog without leaving a modal blocking
    // the test desktop. The production exception handler still runs normally.
    dialog.showErrorBox = (title, content) => appendFileSync(path, `${title}\n${content}\n`)
  }, path)
  return path
}

async function openNotes(h: Harness): Promise<Page> {
  const opened = h.app.waitForEvent('window')
  await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
  const notes = await opened
  await expect(notes.getByRole('heading', { name: 'Arrival' })).toBeVisible()
  return notes
}

test('expanded note details and completed group survive reload and app restart per scene', async () => {
  let h = await launchApp('nl-notes-restart-')
  const errors = await recordMainErrors(h)
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await dismissTour(h.page)
    const text = '- Parent\n  Parent details\n  - Child\n    Child details\n- [x] Finished\n  Finished details'
    for (const entry of chapter.scenes) await h.rpc('scenes/setNotes', [chapter.guid, entry.id, text])
    const openScene = () => h.page.evaluate(({ chapterGuid, sceneId }) => window.novalistStores.project.getState().openScene(chapterGuid, sceneId), { chapterGuid: chapter.guid, sceneId: scene.id })
    await openScene()
    let notes = await openNotes(h)
    await notes.locator('summary').filter({ hasText: /^Parent/ }).click()
    await notes.locator('summary').filter({ hasText: /^Child/ }).click()
    await notes.locator('.scene-checklist-completed > summary').click()
    await expect(notes.locator('.scene-checklist-completed')).toHaveAttribute('open', '')
    await expect(notes.getByText('Child details', { exact: true })).toBeVisible()
    await notes.reload()
    await expect(notes.getByText('Child details', { exact: true })).toBeVisible()
    await expect(notes.getByRole('checkbox', { name: 'Finished', exact: true })).toBeVisible()
    // Another scene with identical headings owns independent disclosure state.
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[1].id), chapter)
    await expect(notes.getByRole('heading', { name: 'Departure' })).toBeVisible()
    await expect(notes.getByText('Parent details', { exact: true })).toBeHidden()
    await openScene()
    await expect(notes.getByText('Child details', { exact: true })).toBeVisible()
    const projectPath = await h.page.evaluate(() => window.novalistStores.project.getState().projectPath)
    const workDir = h.workDir
    await h.close()
    h = await launchApp('nl-notes-restart-', {}, workDir)
    await recordMainErrors(h)
    await h.rpc('project/open', [projectPath])
    await openScene()
    notes = await openNotes(h)
    await expect(notes.getByText('Child details', { exact: true })).toBeVisible()
    await expect(notes.getByRole('checkbox', { name: 'Finished', exact: true })).toBeVisible()
    await notes.locator('summary').filter({ hasText: /^Parent/ }).click()
    await notes.reload()
    await expect(notes.getByRole('heading', { name: 'Arrival' })).toBeVisible()
    await expect(notes.getByText('Parent details', { exact: true })).toBeHidden()
    expect(existsSync(errors) ? readFileSync(errors, 'utf8') : '').toBe('')
  } finally { await h.close().catch(() => {}) }
})

for (const action of ['quit', 'close-main'] as const) for (const pinned of [true, false]) {
  test(`closing Novalist with notes open has no main-process exception (${action}, pinned=${pinned})`, async () => {
    const h = await launchApp('nl-notes-close-')
    const errors = await recordMainErrors(h)
    try {
      const book = await seedBook(h, { Opening: ['Arrival'] })
      const chapter = book.chapters[0]
      await dismissTour(h.page)
      await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
      const notes = await openNotes(h)
      if (!pinned) await notes.getByRole('button', { name: 'Pinned', exact: true }).click()
      const projectPath = await h.page.evaluate(() => window.novalistStores.project.getState().projectPath!)
      await notes.evaluate(() => {
        const control = window as typeof window & { finishNotesWrite(): void; notesWriteStarted: boolean }
        const pending = new Promise<void>(resolve => { control.finishNotesWrite = resolve })
        const request = window.novalistRpc.request.bind(window.novalistRpc)
        window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
          if (method === 'scenes/setNotes') { control.notesWriteStarted = true; await pending }
          return request<T>(method, params)
        }
      })
      await notes.getByRole('textbox', { name: 'Add a scene goal', exact: true }).fill('Preserve pending notes at shutdown')
      await notes.getByRole('button', { name: 'Add a scene goal', exact: true }).click()
      await notes.waitForFunction(() => (window as typeof window & { notesWriteStarted: boolean }).notesWriteStarted)
      const closed = Promise.all([h.page.waitForEvent('close', { timeout: 30_000 }), notes.waitForEvent('close', { timeout: 30_000 })])
      await h.app.evaluate(({ app, BrowserWindow }, action) => {
        const main = BrowserWindow.getAllWindows().find(win => !win.webContents.getURL().includes('sceneNotes='))!
        setImmediate(() => action === 'quit' ? app.quit() : main.close())
      }, action)
      await expect(notes.locator('.window-closing-dialog')).toBeVisible()
      await notes.evaluate(() => (window as typeof window & { finishNotesWrite(): void }).finishNotesWrite())
      await closed
      expect(existsSync(errors) ? readFileSync(errors, 'utf8') : '').toBe('')
      const manifests = readdirSync(projectPath, { recursive: true }).filter(path => typeof path === 'string' && path.endsWith('scenes.json'))
      expect(manifests.some(path => readFileSync(join(projectPath, String(path)), 'utf8').includes('Preserve pending notes at shutdown'))).toBe(true)
    } finally { await h.close().catch(() => {}) }
  })
}
