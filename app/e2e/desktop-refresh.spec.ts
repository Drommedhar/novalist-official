import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, resizeWindow, seedBook } from './harness'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'

test('populated dictionaries and gallery cards stay usable at desktop window sizes', async () => {
  const h = await launchApp('nl-refresh-assets-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    const languages = await h.rpc<{ id: string }[]>('conlang/create', ['Old Hillsford'])
    const id = languages[0].id
    for (const [word, meaning] of [
      ['valen', 'friend'],
      ['mora', 'river'],
      ['esh', 'home']
    ]) {
      await h.rpc('conlang/saveWord', [id, null, word, meaning, 'noun', word, ''])
    }
    await resizeWindow(h, 1280, 900)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('languages'))
    await expect(h.page.locator('.languages-table tbody tr')).toHaveCount(3)
    await h.page.locator('.desktop-view-actions button').click()
    await expect(h.page.locator('.languages-draft input').first()).toBeFocused()
    await h.page.locator('.languages-draft input').nth(0).fill('toran')
    await h.page.locator('.languages-draft input').nth(1).fill('promise')
    await h.page.locator('.languages-draft input').nth(2).fill('noun')
    await h.page.locator('.languages-draft button').click()
    await expect(h.page.locator('.languages-table tbody tr')).toHaveCount(4)
    await expect
      .poll(
        async () =>
          (await h.rpc<{ words: { word: string; meaning: string }[] }[]>('conlang/list'))[0].words
      )
      .toContainEqual(expect.objectContaining({ word: 'toran', meaning: 'promise' }))
    await h.page.screenshot({ path: '../artifacts/ui-refresh/v2/fixture-languages.png' })
    const source = join(h.workDir, 'reference.png')
    writeFileSync(
      source,
      Buffer.from(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZAAAAABJRU5ErkJggg==',
        'base64'
      )
    )
    await h.rpc('gallery/import', [source])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('gallery'))
    const card = h.page.locator('.gallery-item').first()
    await expect(card.locator('img')).toBeVisible()
    const fits = await card.evaluate((el) => {
      const cardBox = el.getBoundingClientRect()
      const captionBox = el.querySelector('.gallery-caption')!.getBoundingClientRect()
      return captionBox.bottom <= cardBox.bottom && captionBox.width <= cardBox.width
    })
    expect(fits).toBe(true)
    await card.focus()
    await h.page.keyboard.press('Enter')
    await expect(h.page.locator('.gallery-lightbox')).toBeVisible()
    await h.page.keyboard.press('Escape')
    await expect(h.page.locator('.gallery-lightbox')).toHaveCount(0)
    await expect(card).toBeFocused()
  } finally {
    await h.close()
  }
})

test('research retains notes across tabs and keeps its metadata editable', async () => {
  const h = await launchApp('nl-refresh-research-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await h.rpc('research/save', [
      null,
      'Archive source',
      'Note',
      'Original source',
      ['archive'],
      []
    ])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    await h.page.locator('.codex-row').first().click()
    await h.page.locator('.research-content .cm-content').fill('Revised source with **detail**.')
    await h.page
      .locator('.research-view > .codex-tabs')
      .getByRole('button', { name: 'Scratchpad', exact: true })
      .click()
    await h.page.locator('.scratchpad-panel textarea').fill('An unfinished thought')
    await h.page.getByRole('button', { name: 'Project research', exact: true }).click()
    await expect(h.page.locator('.research-content .cm-content')).toContainText('Revised source')
    await h.page.locator('.research-metadata > summary').click()
    await expect(h.page.locator('.research-tag')).toContainText('archive')
    await h.page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('Resolved')
    await expect
      .poll(() => h.rpc('research/list'))
      .toMatchObject([{ status: 'Resolved', content: 'Revised source with **detail**.' }])
    await h.page
      .locator('.research-view > .codex-tabs')
      .getByRole('button', { name: 'Scratchpad', exact: true })
      .click()
    await expect(h.page.locator('.scratchpad-panel textarea')).toHaveValue('An unfinished thought')
    // A header action must return to the new item, even from the scratchpad.
    await h.page.locator('.desktop-view-actions .research-action-btn').first().click()
    await expect(h.page.locator('.research-editor')).toBeVisible()
    await expect.poll(async () => (await h.rpc<unknown[]>('research/list')).length).toBe(2)
  } finally {
    await h.close()
  }
})

test('scene rows toggle real plot assignments and retain editable notes', async () => {
  const h = await launchApp('nl-refresh-plot-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Discovery', 'Departure'] })
    const chapter = book.chapters[0]
    await dismissTour(h.page)
    await h.rpc('plot/createPlotline', ['The missing letter'])
    await h.rpc('plot/createPlotline', ['The friendship'])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('plotGrid'))
    await expect(h.page.locator('.plotgrid-table tbody tr')).toHaveCount(3)
    await expect(h.page.locator('.plotgrid-table thead th')).toHaveCount(3)
    const cell = h.page.getByRole('button', { name: 'Arrival · The missing letter', exact: true })
    await cell.click()
    await expect(cell).toHaveAttribute('aria-pressed', 'true')
    await cell.click({ button: 'right' })
    const dialog = h.page.locator('.dialog-card[role="dialog"]')
    await dialog.locator('input').fill('The letter changes hands.')
    await dialog.locator('input').press('Enter')
    await expect(cell).toContainText('The letter changes hands.')
    await cell.click({ button: 'right' })
    await expect(dialog.locator('input')).toHaveValue('The letter changes hands.')
    await dialog.locator('input').fill('')
    await dialog.locator('input').press('Enter')
    await expect(cell).toContainText('Assigned')
    const grid = await h.rpc<{
      columns: { sceneId: string; plotlineIds: string[]; notes: object }[]
    }>('plot/grid')
    expect(grid.columns.find((c) => c.sceneId === chapter.scenes[0].id)).toMatchObject({
      plotlineIds: [expect.any(String)],
      notes: {}
    })
    await resizeWindow(h, 1280, 900)
    await expect(cell).toBeInViewport()
    await h.page.screenshot({ path: '../artifacts/ui-refresh/v2/fixture-plot-grid.png' })
  } finally {
    await h.close()
  }
})

test('combined navigation keeps writing space and scene notes save through both entry points', async () => {
  const h = await launchApp('nl-refresh-notes-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await resizeWindow(h, 1280, 900)
    await h.page.locator('.binder-scene-row').first().click()
    await expect(h.page.locator('.workspace-sidebar .binder')).toBeVisible()
    const sort = h.page.locator('.binder-sort-select').first()
    const alternate = await sort.locator('option').nth(1).getAttribute('value')
    await sort.selectOption(alternate!)
    await h.page.evaluate(() => window.novalistStores.shell.setState({ modePanelVisible: false }))
    await expect(sort).toHaveValue(alternate!)
    await h.page.evaluate(() => window.novalistStores.shell.setState({ modePanelVisible: true }))
    await expect(sort).toHaveValue(alternate!)
    await sort.selectOption('order')
    const main = await h.page.locator('.shell-main').boundingBox()
    expect(main!.width).toBeGreaterThan(600)
    await h.page
      .locator('.inspector-tabs')
      .getByRole('button', { name: 'Notes', exact: true })
      .click()
    await expect(h.page.locator('#inspector-notes')).toBeVisible()
    await h.page.locator('#inspector-notes').fill('A note kept with this scene.')
    await h.page.locator('#inspector-goal').fill('Find the missing letter.')
    await h.page.locator('#inspector-outcome').fill('The letter names a new suspect.')
    await h.page
      .locator('.inspector-tabs')
      .getByRole('button', { name: 'Context', exact: true })
      .click()
    await expect
      .poll(async () => h.rpc('scenes/getMeta', [chapter.guid, scene.id]))
      .toMatchObject({
        notes: 'A note kept with this scene.',
        goal: 'Find the missing letter.',
        outcome: 'The letter names a new suspect.'
      })
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
    await expect(h.page.locator('#dock-notes')).toHaveValue('A note kept with this scene.')
    await h.page
      .locator('.inspector-tabs')
      .getByRole('button', { name: 'Notes', exact: true })
      .click()
    await expect(h.page.locator('#dock-notes')).toHaveCount(0)
    await expect(h.page.locator('#inspector-goal')).toHaveValue('Find the missing letter.')
    await h.page.locator('.binder-scene-row').nth(1).click()
    await expect(h.page.locator('#inspector-notes')).toHaveValue('')
    await h.page.locator('.binder-scene-row').first().click()
    await expect(h.page.locator('#inspector-notes')).toHaveValue('A note kept with this scene.')
  } finally {
    await h.close()
  }
})

test('dashboard resumes the real scene and export stays reachable in a maximized window', async () => {
  const h = await launchApp('nl-refresh-layout-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    if (process.platform === 'linux' && process.env.CI) {
      // Xvfb has no window manager; exercise the large layout with real bounds.
      await resizeWindow(h, 1440, 900)
    } else {
      await h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].maximize())
      await expect.poll(() => h.app.evaluate(({ BrowserWindow }) =>
        BrowserWindow.getAllWindows()[0].isMaximized())).toBe(true)
    }
    await h.page.getByRole('button', { name: 'Dashboard', exact: true }).click()
    await expect(h.page.locator('.dashboard-resume')).toContainText('Arrival')
    await h.page.getByRole('button', { name: 'Continue writing', exact: true }).click()
    await expect(h.page.locator('.editor-frame')).toBeVisible()
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('export'))
    await expect(h.page.locator('.export-run')).toBeInViewport()
    const workspace = await h.page.locator('.export-workspace').boundingBox()
    const available = await h.page.locator('.shell-main').boundingBox()
    expect(workspace!.width).toBeGreaterThan(available!.width * 0.9)
    await expect(h.page.locator('.export-summary')).toBeInViewport()
    await h.page.getByRole('button', { name: 'Library', exact: true }).click()
    await expect(h.page.locator('.library-content')).toBeVisible()
    await expect(h.page.locator('.volume-title').first()).toBeVisible()
  } finally {
    await h.close()
  }
})

test('narration fills its pane and binder scene selection stays in the reading', async () => {
  const h = await launchApp('nl-refresh-narration-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Discovery', 'Departure'] })
    const chapter = book.chapters[0]
    for (const scene of chapter.scenes) {
      const prose = Array.from({ length: 35 }, (_, i) => `${scene.title}: passage ${i}.`)
      await h.rpc('scenes/write', [
        chapter.guid,
        scene.id,
        prose.map((p) => `<p>${p}</p>`).join(''),
        prose.join('\n')
      ])
    }
    await dismissTour(h.page)
    await enterWriting(h.page)
    await h.page.locator('.binder-scene-row').first().click()
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))
    const frame = h.page.frameLocator('.narration-frame')
    await expect(frame.locator('.nl-scene')).toHaveCount(3)
    for (const maximized of [true, false]) {
      if (maximized) {
        await h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].maximize())
      } else {
        await resizeWindow(h, 1280, 900)
      }
      const workspace = await h.page.locator('.narration-workspace').boundingBox()
      const available = await h.page.locator('.shell-main').boundingBox()
      expect(workspace!.width).toBeGreaterThan(available!.width * 0.95)
      const cast = await h.page.locator('.narration-cast').boundingBox()
      const prose = await h.page.locator('.narration-stage').boundingBox()
      expect(prose!.width).toBeGreaterThan(cast!.width)
      const wrap = await frame.locator('#wrap').boundingBox()
      expect(wrap!.width).toBeGreaterThan(prose!.width * 0.95)
      for (const index of [2, 0, 2]) {
        await h.page.locator('.binder-scene-row').nth(index).click()
        await expect(frame.locator('.nl-scene-title').nth(index)).toBeInViewport()
        await expect(h.page.locator('.binder-scene-row').nth(index)).toHaveClass(/active/)
        expect(
          await h.page.evaluate(() => ({
            view: window.novalistStores.shell.getState().mainView,
            editor: window.novalistStores.project.getState().openSceneId
          }))
        ).toEqual({ view: 'narration', editor: chapter.scenes[0].id })
      }
      // Clicking the same scene again must reveal it after manual scrolling.
      await frame.locator('#wrap').evaluate((el) => {
        el.scrollTop = 0
      })
      await h.page.locator('.binder-scene-row').nth(2).click()
      await expect(frame.locator('.nl-scene-title').nth(2)).toBeInViewport()
    }
  } finally {
    await h.close()
  }
})

test('failed update checks are truthful and the review dialog contains keyboard focus', async () => {
  const h = await launchApp('nl-refresh-update-')
  try {
    await h.app.evaluate(({ ipcMain }) => {
      ipcMain.removeHandler('novalist:check-app-update')
      ipcMain.handle('novalist:check-app-update', () => {
        throw new Error('Offline')
      })
    })
    await h.page.evaluate(() => {
      window.novalistStores.extensions.setState({ checkStoreUpdates: async () => 0 })
      window.postMessage({ novalist: 'menu-command', command: 'help:checkUpdates' }, '*')
    })
    const dialog = h.page.getByRole('dialog', { name: 'Could not complete the update check' })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('alert')).toContainText('Could not check Novalist updates.')
    await expect(dialog).not.toContainText('up to date')
    await dialog.getByRole('button', { name: 'Check again' }).focus()
    for (let i = 0; i < 8; i++) {
      await h.page.keyboard.press(i < 4 ? 'Tab' : 'Shift+Tab')
      expect(
        await h.page.evaluate(() => Boolean(document.activeElement?.closest('.update-dialog')))
      ).toBe(true)
    }
    await h.page.keyboard.press('Escape')
    await expect(dialog).not.toBeVisible()
  } finally {
    await h.close()
  }
})
