import { expect, test, type Locator, type Page } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

async function openNotes(h: Harness): Promise<Page> {
  const opened = h.app.waitForEvent('window')
  await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
  const notes = await opened
  await expect(notes.getByRole('heading', { name: 'Arrival', exact: true })).toBeVisible()
  await notes.getByRole('button', { name: 'Edit notes', exact: true }).click()
  return notes
}

async function mergeAndUndo(first: Locator, second: Locator, key: string): Promise<void> {
  await expect(first).toHaveText(`Original ${key}.`)
  await expect(second).toHaveText(`Original ${key}.`)
  await first.focus()
  await first.press(process.platform === 'darwin' ? 'Meta+ArrowDown' : 'Control+End')
  await first.page().keyboard.insertText(' Alice')
  await second.focus()
  await second.press(process.platform === 'darwin' ? 'Meta+ArrowUp' : 'Control+Home')
  await second.page().keyboard.insertText('Bob ')
  await expect(first).toHaveText(`Bob Original ${key}. Alice`)
  await expect(second).toHaveText(`Bob Original ${key}. Alice`)
  await first.focus()
  await first.press(process.platform === 'darwin' ? 'Meta+z' : 'Control+z')
  await expect(first).toHaveText(`Bob Original ${key}.`)
  await expect(second).toHaveText(`Bob Original ${key}.`)
}

test('scene planning prose merges between writers with individual undo across writing views', async () => {
  const participants: Harness[] = []
  const pageErrors: string[] = []
  try {
    const owner = await launchApp('nl-scene-notes-owner-')
    participants.push(owner)
    owner.page.on('pageerror', error => pageErrors.push(error.message))
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
    guest.page.on('pageerror', error => pageErrors.push(error.message))
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
    for (const [key, label] of [['synopsis', 'SYNOPSIS'], ['goal', 'Goal'], ['outcome', 'Outcome']]) {
      await mergeAndUndo(
        owner.page.locator('.inspector').getByRole('textbox', { name: label, exact: true }),
        guest.page.locator('.inspector').getByRole('textbox', { name: label, exact: true }), key)
    }
    // Desktop notes live in their own renderer, using the shared Markdown editor.
    const ownerNotes = await openNotes(owner)
    const guestNotes = await openNotes(guest)
    for (const notes of [ownerNotes, guestNotes]) notes.on('pageerror', error => pageErrors.push(error.message))
    await mergeAndUndo(
      ownerNotes.getByRole('textbox', { name: 'Edit notes', exact: true }),
      guestNotes.getByRole('textbox', { name: 'Edit notes', exact: true }), 'notes')
    await owner.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    await owner.page.getByRole('button', { name: 'Outliner', exact: true }).click()
    const outline = owner.page.locator('.outliner').getByRole('textbox', { name: 'SYNOPSIS', exact: true })
    await expect(outline).toHaveText('Bob Original synopsis.')
    await outline.focus()
    await outline.press(process.platform === 'darwin' ? 'Meta+ArrowDown' : 'Control+End')
    await owner.page.keyboard.insertText(' Outline')
    await expect(guest.page.locator('.inspector').getByRole('textbox', { name: 'SYNOPSIS', exact: true })).toHaveText('Bob Original synopsis. Outline')
    await owner.page.getByRole('button', { name: 'Corkboard', exact: true }).click()
    const card = owner.page.locator('.corkboard').getByRole('textbox', { name: 'SYNOPSIS', exact: true })
    await expect(card).toHaveText('Bob Original synopsis. Outline')
    await card.focus()
    await card.press(process.platform === 'darwin' ? 'Meta+ArrowDown' : 'Control+End')
    await owner.page.keyboard.insertText(' Card')
    await expect(guest.page.locator('.inspector').getByRole('textbox', { name: 'SYNOPSIS', exact: true })).toHaveText('Bob Original synopsis. Outline Card')
    const updated = await owner.rpc<{ chapters: { guid: string; scenes: { id: string; title: string }[] }[] }>('project/createScene', [chapter.guid, 'Next scene'])
    const next = updated.chapters.flatMap(chapter => chapter.scenes).find(scene => scene.title === 'Next scene')!
    await expect(guest.page.locator('.binder-scene-row').filter({ hasText: 'Next scene' })).toBeVisible()
    for (const h of participants) await h.page.evaluate(async ({ chapter, scene }) => {
      await window.novalistStores.project.getState().openScene(chapter, scene)
      window.novalistStores.shell.getState().setInspectorTab('notes')
    }, { chapter: chapter.guid, scene: next.id })
    for (const notes of [ownerNotes, guestNotes]) {
      await expect(notes.getByRole('heading', { name: 'Next scene', exact: true })).toBeVisible()
      await notes.getByRole('button', { name: 'Edit notes', exact: true }).click()
    }
    const fresh = ownerNotes.getByRole('textbox', { name: 'Edit notes', exact: true })
    await expect(fresh).toHaveText('')
    await fresh.fill('Fresh planning text.')
    await expect(guestNotes.getByRole('textbox', { name: 'Edit notes', exact: true })).toHaveText('Fresh planning text.')
    await expect(guest.page.locator('.binder-scene-row').filter({ hasText: 'Next scene' }).locator('.collaboration-scene-badge')).toHaveAttribute('title', /Owner/)
    expect(pageErrors).toEqual([])
  } finally {
    for (const participant of participants.reverse()) await participant.close()
  }
})
