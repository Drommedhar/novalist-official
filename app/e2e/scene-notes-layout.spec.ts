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


test('notes dock around nested panes, reflow with their size and float without losing edits', async ({}, testInfo) => {
  const h = await launchApp('nl-notes-docking-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await dismissTour(h.page)
    await h.rpc('scenes/setNotes', [chapter.guid, scene.id, '- First goal\n- Second goal\n- Third goal'])
    const mainWindow = await h.app.browserWindow(h.page)
    await mainWindow.evaluate(win => { win.unmaximize(); win.setBounds({ x: 0, y: 0, width: 1440, height: 1000 }) })
    await h.page.evaluate(async ({ chapterGuid, sceneId }) => {
      const shell = window.novalistStores.shell
      shell.setState({ binderVisible: false, modePanelVisible: false, inspectorVisible: false })
      await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
    }, { chapterGuid: chapter.guid, sceneId: scene.id })
    const opened = h.app.waitForEvent('window')
    await h.page.locator('.toolbar [data-command="app.toggleSceneNotes"]').click()
    const notes = await opened
    await expect(notes.getByRole('heading', { name: 'Arrival' })).toBeVisible()
    const alignment = await notes.locator('.scene-checklist-heading').evaluate(heading => {
      const title = heading.querySelector('h1')!.getBoundingClientRect()
      const button = heading.querySelector('button')!.getBoundingClientRect()
      return Math.abs(title.y + title.height / 2 - button.y - button.height / 2)
    })
    expect(alignment).toBeLessThan(2)
    await notes.getByRole('checkbox', { name: 'Follow active scene' }).uncheck()
    await notes.getByRole('button', { name: 'Dock scene notes', exact: true }).click()
    await h.page.getByRole('button', { name: 'Dock above', exact: true }).click()
    const panel = h.page.locator('.scene-notes-panel')
    await expect(panel.getByRole('heading', { name: 'Arrival' })).toBeVisible()
    await expect(panel.getByRole('checkbox', { name: 'Follow active scene' })).not.toBeChecked()
    await expect(notes.locator('.scene-checklist')).toHaveCount(0)
    await expect(h.page.getByRole('button', { name: 'Close pane', exact: true })).toHaveCount(0)
    const roots = panel.locator('.scene-checklist-roots > .scene-checklist-item')
    await expect.poll(async () => {
      const a = await roots.nth(0).boundingBox()
      const b = await roots.nth(1).boundingBox()
      return !!a && !!b && Math.abs(a.y - b.y) < 2 && b.x > a.x
    }).toBe(true)
    await panel.getByRole('button', { name: 'Edit notes', exact: true }).click()
    await expect(panel.getByRole('checkbox', { name: 'First goal', exact: true })).toBeVisible()
    const editor = panel.getByRole('textbox', { name: 'Edit notes', exact: true })
    await editor.fill('- First goal\n- Second goal\n- Third goal\n- Pending docked edit')
    const order = await panel.evaluate(element => {
      const list = element.querySelector('.scene-checklist-list')!.getBoundingClientRect()
      const editor = element.querySelector('.scene-checklist-editor')!.getBoundingClientRect()
      return editor.top >= list.bottom
    })
    expect(order).toBe(true)
    await h.page.screenshot({ path: testInfo.outputPath('notes-horizontal.png') })
    await panel.getByRole('button', { name: 'Show checklist', exact: true }).click()
    await panel.getByRole('button', { name: 'Move scene notes', exact: true }).click()
    await h.page.getByRole('button', { name: 'Dock right', exact: true }).click()
    await expect(panel.getByRole('checkbox', { name: 'Pending docked edit', exact: true })).toBeVisible()
    await expect(h.page.locator('.pane-leaf[data-view="sceneNotes"]')).toHaveCount(1)
    await h.page.screenshot({ path: testInfo.outputPath('notes-vertical.png') })
    await h.page.evaluate(() => window.novalistStores.shell.getState().saveLayout('Notes beside editor'))
    // Toggle before the iframe's debounced change report. Its DOM, caret and
    // undo history must survive hiding and restoring the auxiliary notes slot.
    const prose = h.page.frameLocator('.editor-frame').locator('#editor')
    await prose.evaluate(element => {
      const editor = element as HTMLElement & { notesToggleIdentity?: string }
      editor.notesToggleIdentity = 'original-editor'
      editor.innerHTML = '<p>A character typed just before hiding notes.</p>'
      editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: '.' }))
      ;(element.ownerDocument.defaultView!.parent as Window).novalistStores.shell.getState().toggleNotesDock()
    })
    await expect(panel).toHaveCount(0)
    await expect(prose).toHaveText('A character typed just before hiding notes.')
    expect(await prose.evaluate(element => (element as HTMLElement & { notesToggleIdentity?: string }).notesToggleIdentity)).toBe('original-editor')
    await h.page.locator('.toolbar [data-command="app.toggleSceneNotes"]').click()
    await expect(panel).toBeVisible()
    expect(await prose.evaluate(element => (element as HTMLElement & { notesToggleIdentity?: string }).notesToggleIdentity)).toBe('original-editor')
    await h.page.evaluate(() => window.novalistStores.shell.getState().splitActivePane('column'))
    await expect(h.page.locator('.pane-leaf')).toHaveCount(3)
    await panel.getByRole('button', { name: 'Move scene notes', exact: true }).click()
    await h.page.getByRole('button', { name: 'Dock below', exact: true }).last().click()
    await expect(panel.getByRole('checkbox', { name: 'Pending docked edit', exact: true })).toBeVisible()
    await expect(h.page.locator('.pane-leaf[data-view="sceneNotes"]')).toHaveCount(1)
    await panel.getByRole('button', { name: 'Hide scene notes', exact: true }).click()
    await expect(panel).toHaveCount(0)
    await h.page.locator('.toolbar [data-command="app.toggleSceneNotes"]').click()
    await expect(panel).toBeVisible()
    await panel.getByRole('button', { name: 'Float scene notes', exact: true }).click()
    await expect(panel).toHaveCount(0)
    await expect(notes.getByRole('checkbox', { name: 'Pending docked edit', exact: true })).toBeVisible()
    await notes.getByRole('button', { name: 'Edit notes', exact: true }).click()
    await notes.getByRole('textbox', { name: 'Edit notes', exact: true }).fill('- Fresh floating edit')
    await h.page.evaluate(() => window.novalistStores.shell.getState().applyLayout('Notes beside editor'))
    await expect(panel.getByRole('checkbox', { name: 'Fresh floating edit', exact: true })).toBeVisible()
    await expect(notes.locator('.scene-checklist')).toHaveCount(0)
    await expect.poll(async () => (await h.rpc<{ notes: string }>('scenes/getMeta', [chapter.guid, scene.id])).notes).toContain('Fresh floating edit')
    await h.page.evaluate(async ({ chapterGuid, sceneId }) => {
      await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
      window.novalistStores.shell.getState().toggleFocusMode()
    }, { chapterGuid: chapter.guid, sceneId: scene.id })
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
    await expect(notes.getByRole('checkbox', { name: 'Fresh floating edit', exact: true })).toBeVisible()
    expect(await h.page.evaluate(() => window.novalistStores.shell.getState().focusMode)).toBe(true)
    await expect.poll(() => h.page.evaluate(async () => (await window.novalist.sceneNotesWindowState?.())?.docked)).toBe(false)
  } finally { await h.close() }
})
