import { test, expect, type Page } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

async function enterWrite(page: Page, tablet: boolean): Promise<void> {
  await page.evaluate((key) => {
    (window as unknown as { __novalistTab(key: string): void }).__novalistTab(key)
  }, tablet ? 'write' : 'manuscript')
}

for (const tablet of [true, false]) {
  const layout = tablet ? 'tablet' : 'compact'
  const env = { NOVALIST_FORCE_MOBILE: '1', NOVALIST_FORCE_TABLET: tablet ? '1' : '0' }

  test(`${layout}: desktop to-dos can be viewed, created and completed on mobile`, async () => {
    const h = await launchApp('nl-mobile-tasks-', env)
    try {
      const book = await seedBook(h, { Chapter: ['Scene'] })
      await dismissTour(h.page)
      await resizeWindow(h, tablet ? 1180 : 393, tablet ? 820 : 852)
      // Seed through the same API used by the desktop Inbox, before opening
      // the mobile UI. Mobile must display these existing project tasks.
      await h.rpc('tasks/save', [null, 'Check the dates', 'Revision'])
      const chapter = book.chapters[0]
      const sceneId = chapter.scenes[0].id
      await h.rpc('scenes/setNotes', [chapter.guid, sceneId, 'A separate scene note.'])
      await enterWrite(h.page, tablet)
      await h.page.locator('.binder-scene-title', { hasText: 'Scene' }).click()
      await h.page.getByRole('button', { name: 'Inspector', exact: true }).click()

      const sheet = h.page.locator('.mobile-sheet')
      await expect(sheet.getByRole('button', { name: 'To do', exact: true })).toBeVisible()
      await sheet.getByRole('button', { name: 'To do', exact: true }).click()
      const tasks = sheet.locator('.tasks-panel')
      await expect(tasks.getByLabel('Check the dates', { exact: true })).not.toBeChecked()
      await tasks.getByPlaceholder('What needs doing').fill('Read the whole book aloud')
      await tasks.getByPlaceholder('List (optional)').fill('Revision')
      await tasks.locator('.tasks-add button').click()
      await expect(tasks.getByLabel('Read the whole book aloud', { exact: true })).toBeVisible()
      // Completion waits for the RPC response. Assert the eventual state
      // instead of check(), which requires a synchronous checkbox update.
      await tasks.getByLabel('Check the dates', { exact: true }).click()
      await expect(tasks.getByLabel('Check the dates', { exact: true })).toBeChecked()
      await expect(tasks.locator('.task-list-head')).toContainText('1 of 2')
      await tasks.getByRole('button', { name: 'Untick the list for another pass' }).click()
      await expect(tasks.getByLabel('Check the dates', { exact: true })).not.toBeChecked()
      await tasks.getByLabel('Read the whole book aloud', { exact: true }).click()
      await expect(tasks.getByLabel('Read the whole book aloud', { exact: true })).toBeChecked()

      // Both inputs and every action must fit inside the sheet, including
      // compact iPad Split View / iPhone, where a desktop input row is too wide.
      expect(await sheet.evaluate((element) => {
        const bounds = element.getBoundingClientRect()
        return Array.from(element.querySelectorAll('button, input')).every((control) => {
          const rect = control.getBoundingClientRect()
          return rect.left >= bounds.left && rect.right <= bounds.right
        })
      })).toBe(true)

      await sheet.getByRole('button', { name: 'SCENE NOTES', exact: true }).click()
      await expect(sheet.locator('#dock-notes')).toHaveValue('A separate scene note.')
      await expect(sheet.locator('.tasks-panel')).toHaveCount(0)
      await sheet.getByRole('button', { name: 'Close', exact: true }).click()

      // Reload the project from disk: a local-only list or a mobile-only store
      // would lose the new task or its completion state here.
      await h.page.evaluate(async () => {
        const project = window.novalistStores.project.getState()
        const path = project.projectPath!
        await project.closeProject()
        await window.novalistStores.project.getState().openProject(path)
      })
      await enterWrite(h.page, tablet)
      await h.page.locator('.binder-scene-title', { hasText: 'Scene' }).click()
      await h.page.getByRole('button', { name: 'Inspector', exact: true }).click()
      await sheet.getByRole('button', { name: 'To do', exact: true }).click()
      await expect(tasks.getByLabel('Read the whole book aloud', { exact: true })).toBeChecked()
      await expect(tasks.getByLabel('Check the dates', { exact: true })).not.toBeChecked()
      await tasks.locator('.task', { hasText: 'Read the whole book aloud' })
        .getByRole('button', { name: 'Remove this', exact: true }).click()
      await expect(tasks.locator('.task')).toHaveCount(1)
      await expect.poll(() => h.rpc<{ text: string; done: boolean }[]>('tasks/list'))
        .toEqual([expect.objectContaining({ text: 'Check the dates', done: false })])
    } finally { await h.close() }
  })

  test(`${layout}: to-dos are available before a scene exists`, async () => {
    const h = await launchApp('nl-mobile-tasks-empty-', env)
    try {
      await seedBook(h, {})
      await dismissTour(h.page)
      await resizeWindow(h, tablet ? 1180 : 393, tablet ? 820 : 852)
      await enterWrite(h.page, tablet)
      await h.page.getByRole('button', { name: 'Inspector', exact: true }).click()
      const sheet = h.page.locator('.mobile-sheet')
      await expect(sheet.locator('.tasks-panel')).toBeVisible()
      await sheet.getByPlaceholder('What needs doing').fill('Plan the first chapter')
      await sheet.locator('.tasks-add button').click()
      await expect(sheet.getByLabel('Plan the first chapter', { exact: true })).toBeVisible()
      await expect(sheet.locator('.task-list-head')).toContainText('Loose ends')
      await expect.poll(() => h.rpc('tasks/list')).toEqual([
        expect.objectContaining({ text: 'Plan the first chapter', list: '', sceneId: '' })
      ])
    } finally { await h.close() }
  })
}
