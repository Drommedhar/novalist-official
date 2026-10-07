import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook, type Book } from './harness'

interface Project extends Book { activeBookId: string; books: { id: string; name: string }[] }
declare global { interface Window { releaseSearchSave?: () => void } }

test('a project search result waits for dirty prose before opening its book and scene', async () => {
  const h = await launchApp('nl-search-book-')
  try {
    const first = await seedBook(h, { 'First chapter': ['Pending scene'] })
    await dismissTour(h.page)
    await h.rpc('scenes/write', [first.chapters[0].guid, first.chapters[0].scenes[0].id,
      '<p>Original first-book baseline</p>', 'Original first-book baseline'])
    const initial = await h.rpc<Project>('project/getState')
    await h.rpc('project/createBook', ['Second book'])
    const project = await h.rpc<Project>('project/getState')
    const secondId = project.books.find(book => book.name === 'Second book')!.id
    await h.page.evaluate(id => window.novalistStores.project.getState().switchBook(id), secondId)
    const created = await h.rpc<Book>('project/createChapter', ['Remote chapter'])
    const chapterGuid = created.chapters[created.chapters.length - 1].guid
    const withScene = await h.rpc<Book>('project/createScene', [chapterGuid, 'Remote scene'])
    const sceneId = withScene.chapters.find(chapter => chapter.guid === chapterGuid)!.scenes[0].id
    await h.rpc('scenes/write', [chapterGuid, sceneId, '<p>UniqueRemoteNeedle in the second book.</p>', 'UniqueRemoteNeedle in the second book.'])
    await h.page.evaluate(async ({ bookId, chapterGuid, sceneId }) => {
      const state = window.novalistStores.project.getState()
      await state.switchBook(bookId)
      await state.openScene(chapterGuid, sceneId)
    }, { bookId: initial.activeBookId, chapterGuid: first.chapters[0].guid, sceneId: first.chapters[0].scenes[0].id })
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('Original first-book baseline')
    await h.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      let firstSave = true
      window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
        if (method === 'scenes/write' && firstSave) {
          firstSave = false
          await new Promise<void>(resolve => { window.releaseSearchSave = resolve })
        }
        return original<T>(method, params)
      }
    })
    await editor.evaluate(element => {
      element.innerHTML = '<p><strong>Pending original-book prose</strong></p>'
      element.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText' }))
    })
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().isDirty)).toBe(true)
    await h.page.evaluate(() => window.novalistStores.shell.setState({ findReplaceOpen: true }))
    const dialog = h.page.locator('.findreplace-card')
    await dialog.locator('input').first().fill('UniqueRemoteNeedle')
    await dialog.locator('.findreplace-scope').selectOption('Project')
    await dialog.locator('.dialog-actions button').first().click()
    const result = dialog.locator('.findreplace-result', { hasText: 'Second book / Remote chapter - Remote scene' })
    await expect(result.locator('mark')).toHaveText('UniqueRemoteNeedle')
    await result.click()
    await h.page.waitForFunction(() => !!window.releaseSearchSave)
    expect(await h.page.evaluate(() => window.novalistStores.project.getState().activeBookId)).toBe(initial.activeBookId)
    await h.page.evaluate(() => window.releaseSearchSave!())
    await expect(dialog).toHaveCount(0)
    await expect.poll(() => h.page.evaluate(() => {
      const state = window.novalistStores.project.getState()
      return { bookId: state.activeBookId, chapterGuid: state.openChapterGuid, sceneId: state.openSceneId }
    })).toEqual({ bookId: secondId, chapterGuid, sceneId })
    await expect(editor).toContainText('UniqueRemoteNeedle in the second book.')
    await h.page.evaluate(async ({ bookId, chapterGuid, sceneId }) => {
      const state = window.novalistStores.project.getState()
      await state.switchBook(bookId)
      await state.openScene(chapterGuid, sceneId)
    }, { bookId: initial.activeBookId, chapterGuid: first.chapters[0].guid, sceneId: first.chapters[0].scenes[0].id })
    expect(await h.rpc<{ html: string }>('scenes/read', [first.chapters[0].guid, first.chapters[0].scenes[0].id]))
      .toMatchObject({ html: expect.stringContaining('<strong>Pending original-book prose</strong>') })
    await expect(editor.locator('strong')).toHaveText('Pending original-book prose')
  } finally { await h.close() }
})
