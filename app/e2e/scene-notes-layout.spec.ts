import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('floating notes convert bullets, expand details and persist independent checkboxes', async () => {
  const h = await launchApp('nl-scene-checklist-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await dismissTour(h.page)
    await h.rpc('scenes/setNotes', [chapter.guid, scene.id, 'Remember the viewpoint.\n\n• Repair the bag\n  Speak calmly.\n  - Rocket joke\n  - Introduce Astronaut'])
    await h.page.evaluate(({ chapterGuid, sceneId }) => window.novalistStores.project.getState().openScene(chapterGuid, sceneId), { chapterGuid: chapter.guid, sceneId: scene.id })
    const opened = h.app.waitForEvent('window')
    await h.page.locator('.toolbar [data-command="app.toggleSceneNotes"]').click()
    const notes = await opened
    await expect(notes.getByRole('heading', { name: 'Arrival' })).toBeVisible()
    await expect(notes.getByText('Remember the viewpoint.')).toBeVisible()
    await expect(notes.getByText('Speak calmly.')).toBeHidden()
    await notes.locator('summary').filter({ hasText: 'Repair the bag' }).click()
    await expect(notes.getByText('Speak calmly.')).toBeVisible()
    await notes.getByRole('checkbox', { name: 'Rocket joke', exact: true }).check()
    await expect(notes.getByRole('checkbox', { name: 'Repair the bag', exact: true })).not.toBeChecked()
    await expect.poll(async () => (await h.rpc<{ notes: string }>('scenes/getMeta', [chapter.guid, scene.id])).notes)
      .toBe('Remember the viewpoint.\n\n• [ ] Repair the bag\n  Speak calmly.\n  - [x] Rocket joke\n  - [ ] Introduce Astronaut')
    // Completion moves the root into a collapsed group, replacing this input.
    await notes.getByRole('checkbox', { name: 'Repair the bag', exact: true }).click()
    await expect(notes.locator('.scene-checklist-completed > summary')).toHaveText('Completed (1)')
    await notes.locator('.scene-checklist-completed > summary').click()
    await expect(notes.getByRole('checkbox', { name: 'Repair the bag', exact: true })).toBeChecked()
    await expect(h.page.locator('.notes-dock')).toHaveCount(0)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setInspectorTab('notes'))
    await expect(h.page.locator('#inspector-synopsis')).toBeVisible()
    await expect(h.page.locator('#inspector-notes')).toHaveCount(0)
  } finally { await h.close() }
})

test('notes follow or hold a scene across views and can be hidden and restored', async () => {
  const h = await launchApp('nl-notes-follow-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'] })
    const chapter = book.chapters[0]
    await dismissTour(h.page)
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
    const opened = h.app.waitForEvent('window')
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
    const notes = await opened
    await expect(notes.getByRole('heading', { name: 'Arrival' })).toBeVisible()
    await notes.getByRole('textbox', { name: 'Add a scene goal', exact: true }).fill('Keep the scene grounded')
    await notes.getByRole('button', { name: 'Add a scene goal', exact: true }).click()
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[1].id), chapter)
    await expect(notes.getByRole('heading', { name: 'Departure' })).toBeVisible()
    await notes.getByRole('checkbox', { name: 'Follow active scene' }).uncheck()
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
    await expect(notes.getByRole('heading', { name: 'Departure' })).toBeVisible()
    await notes.getByRole('checkbox', { name: 'Follow active scene' }).check()
    await expect(notes.getByRole('checkbox', { name: 'Keep the scene grounded' })).toBeVisible()
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    await expect(notes.getByRole('heading', { name: 'Arrival' })).toBeVisible()
    await notes.getByRole('button', { name: 'Hide scene notes' }).click()
    await expect.poll(() => h.page.evaluate(async () => (await window.novalist.sceneNotesWindowState?.())?.visible)).toBe(false)
    await h.page.locator('.toolbar [data-command="app.toggleSceneNotes"]').click()
    await expect.poll(() => h.page.evaluate(async () => (await window.novalist.sceneNotesWindowState?.())?.visible)).toBe(true)
    await expect(notes.getByRole('checkbox', { name: 'Keep the scene grounded' })).toBeVisible()
    await notes.getByRole('button', { name: 'Pinned', exact: true }).click()
    await expect(notes.getByRole('button', { name: 'Auto-hide', exact: true })).toHaveAttribute('aria-pressed', 'false')
    // CDP's bringToFront activates a tab; native window focus triggers blur.
    const mainWindow = await h.app.browserWindow(h.page)
    await mainWindow.evaluate((win) => win.focus())
    await expect.poll(() => mainWindow.evaluate((win) => win.isFocused())).toBe(true)
    await expect.poll(() => h.page.evaluate(async () => (await window.novalist.sceneNotesWindowState?.())?.visible)).toBe(false)
  } finally { await h.close() }
})
