import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

test('scene planning prose merges between writers with individual undo across writing views', async () => {
  const participants: Harness[] = []
  try {
    const owner = await launchApp('nl-scene-notes-owner-')
    participants.push(owner)
    const state = await seedBook(owner, { Opening: ['Arrival'] })
    await dismissTour(owner.page)
    const chapter = state.chapters.find(chapter => chapter.title === 'Opening')!
    const scene = chapter.scenes[0]
    await owner.rpc('scenes/setSynopsis', [chapter.guid, scene.id, 'Original synopsis.'])
    await owner.rpc('scenes/setNotes', [chapter.guid, scene.id, 'Original notes.'])
    await owner.rpc('scenes/setGoalOutcome', [chapter.guid, scene.id, 'Original goal.', 'Original outcome.'])
    await owner.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Owner'])
    const link = await owner.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
    const guest = await launchApp('nl-scene-notes-guest-')
    participants.push(guest)
    await dismissTour(guest.page)
    await guest.rpc('collaboration/peer/join', [link, join(guest.workDir, 'Joined'), 'Guest'])
    for (const h of participants) {
      await h.page.evaluate(() => document.querySelector<HTMLButtonElement>('[data-collaboration-trigger][aria-expanded="false"]')?.click())
      await expect(h.page.getByText('Connected to the shared project.', { exact: true })).toBeVisible()
      await h.page.evaluate(async ({ chapter, scene }) => {
        await window.novalistStores.project.getState().openScene(chapter, scene)
        window.novalistStores.shell.getState().setInspectorTab('notes')
      }, { chapter: chapter.guid, scene: scene.id })
    }
    for (const [key, label] of [['synopsis', 'SYNOPSIS'], ['notes', 'SCENE NOTES'], ['goal', 'Goal'], ['outcome', 'Outcome']]) {
      const first = owner.page.locator('.inspector').getByRole('textbox', { name: label, exact: true })
      const second = guest.page.locator('.inspector').getByRole('textbox', { name: label, exact: true })
      await expect(first).toHaveText(`Original ${key}.`)
      await first.focus()
      await first.press('Control+End')
      await owner.page.keyboard.insertText(' Alice')
      await second.focus()
      await second.press('Control+Home')
      await guest.page.keyboard.insertText('Bob ')
      await expect(first).toHaveText(`Bob Original ${key}. Alice`)
      await expect(second).toHaveText(`Bob Original ${key}. Alice`)
      await first.focus()
      await first.press('Control+z')
      await expect(second).toHaveText(`Bob Original ${key}.`)
    }
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    await owner.page.getByRole('button', { name: 'Outliner', exact: true }).click()
    const outline = owner.page.locator('.outliner').getByRole('textbox', { name: 'SYNOPSIS', exact: true })
    await expect(outline).toHaveText('Bob Original synopsis.')
    await outline.focus()
    await outline.press('Control+End')
    await owner.page.keyboard.insertText(' Outline')
    await expect(guest.page.locator('.inspector').getByRole('textbox', { name: 'SYNOPSIS', exact: true })).toHaveText('Bob Original synopsis. Outline')
    await owner.page.getByRole('button', { name: 'Corkboard', exact: true }).click()
    const card = owner.page.locator('.corkboard').getByRole('textbox', { name: 'SYNOPSIS', exact: true })
    await expect(card).toHaveText('Bob Original synopsis. Outline')
    await card.focus()
    await card.press('Control+End')
    await owner.page.keyboard.insertText(' Card')
    await expect(guest.page.locator('.inspector').getByRole('textbox', { name: 'SYNOPSIS', exact: true })).toHaveText('Bob Original synopsis. Outline Card')
    const updated = await owner.rpc<{ chapters: { guid: string; scenes: { id: string; title: string }[] }[] }>('project/createScene', [chapter.guid, 'Next scene'])
    const next = updated.chapters.flatMap(chapter => chapter.scenes).find(scene => scene.title === 'Next scene')!
    await expect(guest.page.locator('.binder-scene-row').filter({ hasText: 'Next scene' })).toBeVisible()
    for (const h of participants) await h.page.evaluate(async ({ chapter, scene }) => {
      await window.novalistStores.project.getState().openScene(chapter, scene)
      window.novalistStores.shell.getState().setInspectorTab('notes')
    }, { chapter: chapter.guid, scene: next.id })
    const fresh = owner.page.locator('.inspector .cm-content[aria-label="SCENE NOTES"]')
    await expect(fresh).toHaveText('')
    await fresh.fill('Fresh planning text.')
    await expect(guest.page.locator('.inspector .cm-content[aria-label="SCENE NOTES"]')).toHaveText('Fresh planning text.')
    await expect(guest.page.locator('.binder-scene-row').filter({ hasText: 'Next scene' }).locator('.collaboration-scene-badge')).toHaveAttribute('title', /Owner/)
  } finally {
    for (const participant of participants.reverse()) await participant.close()
  }
})
