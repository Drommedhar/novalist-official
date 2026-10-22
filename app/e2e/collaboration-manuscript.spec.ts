import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

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
      await writer.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
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
