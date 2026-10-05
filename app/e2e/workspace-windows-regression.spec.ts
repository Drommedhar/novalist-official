import { test, expect, type Page } from '@playwright/test'
import { join } from 'node:path'
import { writeFileSync } from 'node:fs'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

type Scene = { chapterGuid: string; sceneId: string }
async function detach(h: Harness, scene: Scene): Promise<Page> {
  const opened = h.app.waitForEvent('window')
  await h.page.evaluate((scene) => window.novalist.openPaneWindow({
    view: 'write', projectPath: window.novalistStores.project.getState().projectPath, ...scene
  }), scene)
  const page = await opened
  await expect(page.locator('.app-shell.detached')).toBeVisible({ timeout: 30_000 })
  await expect(page.frameLocator('.editor-frame').locator('#editor')).toBeVisible({ timeout: 30_000 })
  return page
}
async function writeRich(page: Page, marker: string): Promise<void> {
  await page.frameLocator('.editor-frame').locator('#editor').evaluate((element, marker) => {
    element.innerHTML = `<p><strong>${marker}</strong> café</p>`
    element.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText' }))
  }, marker)
}
async function waitEditable(pages: Page[]): Promise<void> {
  for (const page of pages) await expect.poll(() => page.evaluate(() => window.novalistStores.project.getState().workspaceBusy)).toBe(false)
}

test('direct close prepares global settings in both windows and the main library before presentation', async () => {
  const h = await launchApp('novalist-shared-library-')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    const second = await detach(h, { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    const cover = join(h.workDir, 'cover.png')
    writeFileSync(cover, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=', 'base64'))
    await h.rpc('dashboard/setCover', [cover])
    await h.page.evaluate(async () => {
      const settings = window.novalistStores.settings.getState()
      await settings.update('global', { language: 'en' })
      await settings.pinSection('appearance')
      await settings.update('project', { language: 'de' })
      const client = window.novalistRpc
      const original = client.request.bind(client)
      let release!: () => void
      const gate = new Promise<void>(resolve => { release = resolve })
      const probe = { held: false, done: false, error: '', release }
      ;(window as unknown as { libraryClose: typeof probe }).libraryClose = probe
      client.request = async <T>(method: string, params?: unknown): Promise<T> => {
        const result = await original<T>(method, params)
        if (method === 'project/recent' && (params as boolean[])?.[0]) {
          probe.held = true
          await gate
        }
        return result
      }
      void client.request('project/close').catch(error => { probe.error = String(error) })
        .finally(() => { probe.done = true; client.request = original })
    })
    await expect.poll(() => h.page.evaluate(() => (window as unknown as { libraryClose: { held: boolean } }).libraryClose.held)).toBe(true)
    await expect(h.page.locator('.project-library')).toHaveCount(0)
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().workspaceBusy)).toBe(true)
    await h.page.evaluate(() => (window as unknown as { libraryClose: { release(): void } }).libraryClose.release())
    await expect.poll(() => h.page.evaluate(() => (window as unknown as { libraryClose: { done: boolean } }).libraryClose.done)).toBe(true)
    await waitEditable([h.page, second])
    await expect(h.page.locator('.project-library h1')).toHaveText('Your bookshelf')
    await expect(h.page.locator('.project-library img.start-recent-cover-img')).toBeVisible()
    for (const page of [h.page, second]) {
      expect(await page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(false)
      expect(await page.evaluate(() => window.novalistStores.settings.getState().view?.effective.language)).toBe('en')
    }
    expect(await h.page.evaluate(() => (window as unknown as { libraryClose: { error: string } }).libraryClose.error)).toBe('')
  } finally { await h.close() }
})

test('typed dialog and collection drafts retain their workspace until submitted or cancelled', async () => {
  const h = await launchApp('novalist-shared-dialog-')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    const second = await detach(h, { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    const added = await h.rpc<{ books: { id: string; name: string }[] }>('project/createBook', ['Second book'])
    const next = added.books.find(book => book.name === 'Second book')!
    const original = await h.page.evaluate(() => window.novalistStores.project.getState().activeBookId)
    await h.page.evaluate(() => window.novalistStores.shell.getState().openDialog('renameBook'))
    const dialog = h.page.getByRole('dialog', { name: 'Rename Book', exact: true })
    await dialog.locator('input').fill('Retain this draft name')
    await expect(second.evaluate(id => window.novalistStores.project.getState().switchBook(id), next.id)).rejects.toThrow()
    await waitEditable([h.page, second])
    await expect(dialog.locator('input')).toHaveValue('Retain this draft name')
    for (const page of [h.page, second]) expect(await page.evaluate(() => window.novalistStores.project.getState().activeBookId)).toBe(original)
    await h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().find(window => !new URL(window.webContents.getURL()).searchParams.has('pane'))!.close())
    await expect(h.page.locator('.toast-message').filter({ hasText: 'Save failed:' })).toBeVisible()
    await expect.poll(() => h.app.windows().length).toBe(2)
    await waitEditable([h.page, second])
    await expect(dialog.locator('input')).toHaveValue('Retain this draft name')
    await dialog.getByRole('button', { name: 'OK', exact: true }).click()
    await expect.poll(async () => (await h.rpc<{ books: { id: string; name: string }[] }>('project/getState')).books.find(book => book.id === original)?.name).toBe('Retain this draft name')
    await second.evaluate(id => window.novalistStores.project.getState().switchBook(id), next.id)
    await waitEditable([h.page, second])
    await h.page.evaluate(() => {
      window.novalistStores.shell.getState().setMainView('write')
      window.novalistStores.shell.setState({ binderVisible: true, binderTab: 'collections' })
    })
    const collectionName = h.page.locator('.collections-new input')
    await collectionName.fill('Pending collection name')
    await expect(second.evaluate(id => window.novalistStores.project.getState().switchBook(id!), original)).rejects.toThrow()
    await waitEditable([h.page, second])
    await expect(collectionName).toHaveValue('Pending collection name')
    await collectionName.fill('')
    await second.evaluate(id => window.novalistStores.project.getState().switchBook(id!), original)
    await waitEditable([h.page, second])
  } finally { await h.close() }
})

test('three windows retain independent RPC streams and flush rich prose before shared draft, book and project changes', async () => {
  const h = await launchApp('novalist-shared-workspace-')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Second', 'Third'] })
    const chapter = book.chapters[0]
    const scenes = chapter.scenes.map((scene) => ({ chapterGuid: chapter.guid, sceneId: scene.id }))
    await h.page.evaluate((scene) => window.novalistStores.project.getState().openScene(scene.chapterGuid, scene.sceneId), scenes[0])
    const second = await detach(h, scenes[1])
    const third = await detach(h, scenes[2])
    const pages = [h.page, second, third]
    const paths = await Promise.all(pages.map((page) => page.evaluate(async () => {
      const replies = await Promise.all(Array.from({ length: 20 }, (_, index) => index % 2
        ? window.novalistRpc.request<{ projectPath: string }>('project/getState').then(state => state.projectPath)
        : window.novalistRpc.request<{ version: string }>('system/ping').then(ping => ping.version)))
      return { path: window.novalistStores.project.getState().projectPath, replies }
    })))
    expect(new Set(paths.map(item => item.path)).size).toBe(1)
    for (const item of paths) expect(item.replies.filter((_, index) => index % 2)).toEqual(Array(10).fill(item.path))
    for (let index = 0; index < pages.length; index++) await writeRich(pages[index], `Window ${index} pending`)
    const originalDraft = await h.page.evaluate(() => window.novalistStores.project.getState().activeDraftId)
    const drafts = await h.rpc<{ id: string; name: string }[]>('project/createDraft', ['Shared copy', originalDraft])
    const copy = drafts.find(draft => draft.name === 'Shared copy')!
    await h.rpc('project/switchDraft', [copy.id])
    await waitEditable(pages)
    for (let index = 0; index < pages.length; index++) {
      const content = await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scenes[index].sceneId])
      expect(content.html).toContain(`<strong>Window ${index} pending</strong>`)
      expect(await pages[index].evaluate(() => window.novalistStores.project.getState().activeDraftId)).toBe(copy.id)
    }
    const oldBook = await h.page.evaluate(() => window.novalistStores.project.getState().activeBookId)
    const added = await h.rpc<{ books: { id: string; name: string }[] }>('project/createBook', ['Another book'])
    const nextBook = added.books.find(book => book.name === 'Another book')!
    await h.rpc('project/switchBook', [nextBook.id])
    await waitEditable(pages)
    for (const page of pages) expect(await page.evaluate(() => window.novalistStores.project.getState().activeBookId)).toBe(nextBook.id)
    await h.rpc('project/switchBook', [oldBook])
    await h.rpc('project/create', [join(h.workDir, 'other'), 'Other project', 'Other book'])
    await waitEditable(pages)
    for (const page of pages) expect(await page.evaluate(() => window.novalistStores.project.getState().projectName)).toBe('Other project')
    await second.evaluate(() => window.close())
    await expect.poll(() => h.app.windows().length).toBe(2)
    expect((await h.rpc<{ projectName: string }>('project/getState')).projectName).toBe('Other project')
  } finally { await h.close() }
})

test('a detached save failure vetoes scope changes and main close, retaining both windows and prose', async () => {
  const h = await launchApp('novalist-shared-veto-')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    const chapter = book.chapters[0]
    const second = await detach(h, { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    await writeRich(second, 'Must remain available')
    await second.evaluate(() => {
      const store = window.novalistStores.project
      const original = store.getState().flushPendingSave
      ;(window as unknown as { restoreFlush: () => void }).restoreFlush = () => store.setState({ flushPendingSave: original })
      store.setState({ flushPendingSave: async () => { throw new Error('Simulated disk refusal') } })
    })
    await expect(h.rpc('project/create', [join(h.workDir, 'blocked'), 'Blocked', 'Book'])).rejects.toThrow(/could not save|disk refusal/i)
    await waitEditable([h.page, second])
    expect((await h.rpc<{ projectName: string }>('project/getState')).projectName).toBe('Spec')
    await h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().find(win => !new URL(win.webContents.getURL()).searchParams.has('pane'))!.close())
    await expect.poll(() => second.evaluate(() => window.novalistStores.project.getState().workspaceBusy)).toBe(false)
    expect(h.app.windows()).toHaveLength(2)
    await expect(second.frameLocator('.editor-frame').locator('#editor')).toContainText('Must remain available')
    await second.evaluate(() => (window as unknown as { restoreFlush: () => void }).restoreFlush())
    await h.rpc('project/create', [join(h.workDir, 'allowed'), 'Allowed', 'Book'])
    await waitEditable([h.page, second])
    expect(await second.evaluate(() => window.novalistStores.project.getState().projectName)).toBe('Allowed')
  } finally { await h.close() }
})

test('backend restart restores the shared scope while keeping pending rich text in both windows', async () => {
  const h = await launchApp('novalist-shared-restart-')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    const chapter = book.chapters[0]
    await h.page.evaluate((scene) => window.novalistStores.project.getState().openScene(scene.chapterGuid, scene.sceneId), { chapterGuid: chapter.guid, sceneId: chapter.scenes[0].id })
    const second = await detach(h, { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    await writeRich(h.page, 'Main survives restart')
    await writeRich(second, 'Detached survives restart')
    await h.page.evaluate(() => {
      window.novalistRpc.notify('system/shutdown')
    })
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().workspaceEpoch)).toBe(0)
    await waitEditable([h.page, second])
    for (const [page, marker] of [[h.page, 'Main survives restart'], [second, 'Detached survives restart']] as const) {
      await expect(page.frameLocator('.editor-frame').locator('#editor')).toContainText(marker)
      await page.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
    }
    await expect.poll(async () => (await h.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[0].id])).html).toContain('Main survives restart')
    await expect.poll(async () => (await h.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[1].id])).html).toContain('Detached survives restart')
  } finally { await h.close() }
})

test('unavailable project after restart keeps both windows frozen and retry restores their pending prose', async () => {
  const h = await launchApp('novalist-shared-recovery-failure-')
  const { renameSync } = await import('node:fs')
  let projectPath = '', movedPath = ''
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    const chapter = book.chapters[0]
    const second = await detach(h, { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    await writeRich(second, 'Preserved during unavailable drive')
    projectPath = (await h.page.evaluate(() => window.novalistStores.project.getState().projectPath))!
    movedPath = `${projectPath}-temporarily-unavailable`
    renameSync(projectPath, movedPath)
    await h.page.evaluate(() => window.novalistRpc.notify('system/shutdown'))
    for (const page of [h.page, second]) {
      await expect.poll(() => page.evaluate(() => window.novalistStores.project.getState().workspaceRecoveryError)).not.toBeNull()
      expect(await page.evaluate(() => window.novalistStores.project.getState().workspaceBusy)).toBe(true)
    }
    renameSync(movedPath, projectPath)
    movedPath = ''
    await second.getByRole('button', { name: 'Try again', exact: true }).click()
    await waitEditable([h.page, second])
    await expect(second.frameLocator('.editor-frame').locator('#editor')).toContainText('Preserved during unavailable drive')
    await second.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
    await expect.poll(async () => (await h.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[1].id])).html).toContain('Preserved during unavailable drive')
  } finally {
    if (movedPath) renameSync(movedPath, projectPath)
    await h.close()
  }
})

test('closing the main window drains rich pending prose from every detached window', async () => {
  const h = await launchApp('novalist-shared-close-')
  const { readdirSync, readFileSync } = await import('node:fs')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    const chapter = book.chapters[0]
    await h.page.evaluate((scene) => window.novalistStores.project.getState().openScene(scene.chapterGuid, scene.sceneId), { chapterGuid: chapter.guid, sceneId: chapter.scenes[0].id })
    const second = await detach(h, { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    const path = (await h.page.evaluate(() => window.novalistStores.project.getState().projectPath))!
    await writeRich(h.page, 'Main global close marker')
    await writeRich(second, 'Detached global close marker')
    await h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows().find(win => !new URL(win.webContents.getURL()).searchParams.has('pane'))!.close())
    await expect.poll(() => h.app.windows().length).toBe(0)
    const prose = readdirSync(path, { recursive: true }).filter(file => String(file).includes('scene-') && String(file).endsWith('.novalist')).map(file => readFileSync(join(path, String(file)), 'utf8')).join('\n')
    expect(prose).toContain('<strong>Main global close marker</strong>')
    expect(prose).toContain('<strong>Detached global close marker</strong>')
  } finally { await h.close().catch(() => {}) }
})

test('detached window routes application dialogs to the main owner and retains local pane shortcuts', async () => {
  const h = await launchApp('novalist-shared-commands-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    const second = await detach(h, { chapterGuid: book.chapters[0].guid, sceneId: book.chapters[0].scenes[0].id })
    await second.evaluate(() => window.postMessage({ novalist: 'menu-command', command: 'app.splitRight' }, '*'))
    await expect(second.locator('.pane-header')).toHaveCount(2)
    await second.evaluate(() => window.postMessage({ novalist: 'menu-command', command: 'project.newChapter' }, '*'))
    await expect(h.page.getByRole('dialog', { name: 'New Chapter', exact: true })).toBeVisible()
    await expect(h.page.getByRole('dialog', { name: 'New Chapter', exact: true }).locator('.dialog-title')).toContainText('New Chapter')
    expect(await second.locator('.dialog-overlay').count()).toBe(0)
  } finally { await h.close() }
})

test('failed resume rejects the initiating call and read-only retry preserves new dirty prose', async () => {
  const h = await launchApp('novalist-shared-resume-retry-')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    const chapter = book.chapters[0]
    await h.page.evaluate((scene) => window.novalistStores.project.getState().openScene(scene.chapterGuid, scene.sceneId), { chapterGuid: chapter.guid, sceneId: chapter.scenes[0].id })
    const second = await detach(h, { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    await second.evaluate(() => {
      const rpc = window.novalistRpc
      const original = rpc.request.bind(rpc)
      const probe = window as unknown as { resumeReadHeld: boolean; refuseResumeRead: () => void }
      rpc.request = async (method, params) => {
        if (method === 'scenes/read') {
          rpc.request = original
          probe.resumeReadHeld = true
          await new Promise((_resolve, reject) => { probe.refuseResumeRead = () => reject(new Error('Simulated resume read failure')) })
        }
        return original(method, params)
      }
    })
    const changing = h.rpc('project/createDraft', ['Resume probe', null]).then(() => 'unexpected success', error => String(error))
    await expect.poll(() => second.evaluate(() => (window as unknown as { resumeReadHeld: boolean }).resumeReadHeld)).toBe(true)
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().workspaceBusy)).toBe(false)
    await writeRich(h.page, 'Typed before another window failed')
    await second.evaluate(() => (window as unknown as { refuseResumeRead: () => void }).refuseResumeRead())
    expect(await changing).toContain('could not finish loading')
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().workspaceRecoveryError)).not.toBeNull()
    await h.page.getByRole('button', { name: 'Try again', exact: true }).click()
    await waitEditable([h.page, second])
    await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toContainText('Typed before another window failed')
    await h.page.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
    expect((await h.rpc<{ html: string }>('scenes/read', [chapter.guid, chapter.scenes[0].id])).html).toContain('Typed before another window failed')
  } finally { await h.close() }
})
