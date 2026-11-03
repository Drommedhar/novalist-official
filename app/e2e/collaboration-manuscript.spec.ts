import { expect, test } from '@playwright/test'
import { cp } from 'node:fs/promises'
import { join, relative } from 'node:path'
import { dismissTour, launchApp, seedBook, type Book, type Harness } from './harness'

async function populatedBook(owner: Harness): Promise<Book> {
  const source = process.env.NOVALIST_COLLABORATION_CORPUS
  if (source) {
    const destination = join(owner.workDir, 'Project')
    await cp(source, destination, { recursive: true, filter: path => !relative(source, path).split(/[\\/]/).includes('.git') })
    await owner.rpc('project/open', [destination])
    return owner.rpc<Book>('project/getState')
  }
  const chapters = Object.fromEntries(Array.from({ length: 12 }, (_, chapter) =>
    [`Chapter ${chapter + 1}`, Array.from({ length: 10 }, (_, scene) => `Scene ${chapter + 1}.${scene + 1}`)]))
  return seedBook(owner, chapters)
}

test('a populated shared manuscript loads and remains editable', async () => {
  const owner = await launchApp('nl-manuscript-populated-')
  try {
    const project = await populatedBook(owner)
    const chapter = project.chapters.find(chapter => chapter.scenes.length > 0)!
    const scene = chapter.scenes[0]
    await owner.rpc('scenes/write', [chapter.guid, scene.id, '<p>A populated book.</p>', 'A populated book.'])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    await dismissTour(owner.page)
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    const manuscript = owner.page.frameLocator('iframe[title="manuscript"]')
    await expect(manuscript.locator('.scene-editor')).toHaveCount(project.chapters.reduce((count, chapter) => count + chapter.scenes.length, 0), { timeout: 15_000 })
    const editor = manuscript.locator(`.scene-editor[data-scene-id="${scene.id}"]`)
    await expect(editor).toHaveText('A populated book.')
    await editor.click()
    await owner.page.keyboard.press('End')
    await owner.page.keyboard.type(' Still responsive.')
    await expect(editor).toHaveText('A populated book. Still responsive.')
    await owner.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].close())
    await expect.poll(() => owner.app.windows().length, { timeout: 20_000 }).toBe(0)
  } finally { await owner.close().catch(() => {}) }
})

test('continuous manuscript shares existing rich scenes, preserves roots and publishes only the focused scene', async () => {
  const writers: Harness[] = []
  try {
    const owner = await launchApp('nl-manuscript-owner-')
    writers.push(owner)
    const project = await seedBook(owner, { Opening: ['Arrival', 'Departure'] })
    const chapter = project.chapters.find(chapter => chapter.title === 'Opening')!
    const [first, second] = chapter.scenes
    await owner.rpc('scenes/write', [chapter.guid, first.id, '<p><strong>Our</strong> story.</p>', 'Our story.'])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-manuscript-guest-')
    writers.push(guest)
    await guest.rpc('collaboration/peer/join', [invitation, join(guest.workDir, 'Joined'), 'Guest'])
    for (const writer of writers) {
      await dismissTour(writer.page)
      await writer.page.evaluate(() => document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="false"]')?.click())
      await expect(writer.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
    }
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    await guest.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: first.id })
    const manuscript = owner.page.frameLocator('iframe[title="manuscript"]')
    const firstEditor = manuscript.locator(`.scene-editor[data-scene-id="${first.id}"]`)
    const guestEditor = guest.page.frameLocator('iframe[title="editor"]').locator('#editor')
    await expect(firstEditor.locator('strong')).toHaveText('Our')
    await firstEditor.click()
    await owner.page.keyboard.press('Control+End')
    await owner.page.keyboard.type(' From manuscript.')
    await expect(guestEditor).toContainText('From manuscript.')
    await guestEditor.click()
    await guest.page.keyboard.press('Control+End')
    await guest.page.keyboard.type(' From scene.')
    await expect(firstEditor).toContainText('From scene.')
    await guestEditor.evaluate(element => {
      const range = element.ownerDocument.createRange()
      range.selectNodeContents(element)
      const selection = element.ownerDocument.getSelection()!
      selection.removeAllRanges()
      selection.addRange(range)
    })
    await expect(manuscript.locator('.nv-collaboration-label').getByText('Guest', { exact: true })).toBeVisible()
    await expect.poll(() => firstEditor.evaluate(element => {
      const view = element.ownerDocument.defaultView as Window & typeof globalThis
      return Array.from(view.CSS.highlights.values()).flatMap(highlight => Array.from(highlight, range => range.toString())).join('')
    })).toContain('From scene.')
    await firstEditor.click()
    await owner.page.keyboard.press('Control+z')
    await expect(firstEditor).toHaveText('Our story. From scene.')
    await expect(guestEditor).toHaveText('Our story. From scene.')
    const root = await firstEditor.elementHandle()
    await guest.rpc('project/createScene', [chapter.guid, 'New shared scene'])
    await expect(manuscript.locator('.scene-editor')).toHaveCount(3)
    expect(await root!.evaluate(element => element.isConnected)).toBe(true)
    const secondEditor = manuscript.locator(`.scene-editor[data-scene-id="${second.id}"]`)
    await secondEditor.click()
    await owner.page.keyboard.type('A second scene.')
    const firstShared = await guest.rpc<{ shared: { sessionId: string; itemId: string } }>('scenes/read', [chapter.guid, first.id])
    const secondShared = await owner.rpc<{ shared: { itemId: string } }>('scenes/read', [chapter.guid, second.id])
    await expect.poll(async () => (await guest.rpc<{ name: string; itemId: string }[]>('collaboration/participants', [firstShared.shared.sessionId]))
      .filter(participant => participant.name === 'Owner').map(participant => participant.itemId)).toEqual([secondShared.shared.itemId])
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    await expect(firstEditor).toHaveText('Our story. From scene.')
    await expect(secondEditor).toHaveText('A second scene.')
  } finally {
    for (const writer of writers.reverse()) await writer.close()
  }
})

test('scrolling to an offscreen shared scene refreshes remote edits before writing there', async () => {
  const writers: Harness[] = []
  try {
    const owner = await launchApp('nl-manuscript-scroll-owner-')
    writers.push(owner)
    const project = await seedBook(owner, { Opening: ['Long opening', 'Later scene'] })
    const chapter = project.chapters.find(chapter => chapter.title === 'Opening')!
    const [first, last] = chapter.scenes
    await owner.rpc('scenes/write', [chapter.guid, first.id, '<p>A long opening paragraph.</p>'.repeat(120), 'A long opening paragraph. '.repeat(120)])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    const invitation = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-manuscript-scroll-guest-')
    writers.push(guest)
    await guest.rpc('collaboration/peer/join', [invitation, join(guest.workDir, 'Joined'), 'Alex'])
    for (const writer of writers) await dismissTour(writer.page)
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    const manuscript = owner.page.frameLocator('iframe[title="manuscript"]')
    await expect(manuscript.locator('.scene-editor')).toHaveCount(2)
    await guest.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene), { chapter: chapter.guid, scene: last.id })
    const guestEditor = guest.page.frameLocator('iframe[title="editor"]').locator('#editor')
    await guestEditor.click()
    await guest.page.keyboard.type('Remote offscreen prose.')
    await guest.page.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
    const editor = manuscript.locator(`.scene-editor[data-scene-id="${last.id}"]`)
    await editor.scrollIntoViewIfNeeded()
    await expect(editor).toHaveText('Remote offscreen prose.')
    await editor.click()
    await owner.page.keyboard.press('End')
    await owner.page.keyboard.type(' Continued here.')
    await expect(guestEditor).toHaveText('Remote offscreen prose. Continued here.')
    await manuscript.locator(`.scene-editor[data-scene-id="${first.id}"]`).scrollIntoViewIfNeeded()
    await editor.scrollIntoViewIfNeeded()
    await editor.click()
    await owner.page.keyboard.press('Control+z')
    await expect(guestEditor).toHaveText('Remote offscreen prose.')
  } finally {
    for (const writer of writers.reverse()) await writer.close()
  }
})
