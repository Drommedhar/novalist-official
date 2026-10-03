import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

test('Calendar scene buttons open scenes with Enter and Space without opening the week', async () => {
  const h = await launchApp('nl-calendar-keys-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'] })
    const chapter = book.chapters[0]
    for (const scene of chapter.scenes)
      await h.rpc('project/setSceneDateRange', [chapter.guid, scene.id, '2026-03-14', '', ''])
    await h.rpc('calendar/setAnchor', ['2026-03-14'])
    await dismissTour(h.page)
    for (const [i, key] of ['Enter', 'Space'].entries()) {
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
      const button = h.page.locator('.calendar-month .calendar-event').nth(i)
      await button.focus()
      await button.press(key)
      await expect
        .poll(() => h.page.evaluate(() => window.novalistStores.project.getState().openSceneId))
        .toBe(chapter.scenes[i].id)
      await expect(h.page.locator('.editor-pane')).toBeVisible()
    }
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const day = h.page.locator('.calendar-cell:not(.outside)').first()
    await day.focus()
    await day.press('Enter')
    await expect(h.page.locator('.calendar-week-grid')).toBeVisible()
  } finally {
    await h.close()
  }
})

test('keyboard focus activates its split pane and narrow Settings keeps content visible', async () => {
  const h = await launchApp('nl-split-keys-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await resizeWindow(h, 1280, 900)
    const first = await h.page.evaluate(() => {
      const s = window.novalistStores.shell.getState()
      s.setMainView('settings')
      const id = s.activePaneId
      s.splitActivePane('row')
      s.setMainView('timeline')
      return id
    })
    const settings = h.page.locator('.pane-leaf[data-view="settings"]')
    await settings.locator('.settings-search').focus()
    await h.page.keyboard.press('Tab')
    expect(await h.page.evaluate(() => window.novalistStores.shell.getState().activePaneId)).toBe(
      first
    )
    const nav = settings.locator('.settings-nav')
    await nav.getByRole('button', { name: 'Editor', exact: true }).click()
    await expect(settings.locator('.settings-section-title')).toBeInViewport()
    const layout = await settings.locator('.settings-layout').boundingBox()
    const content = await settings.locator('.settings-sections').boundingBox()
    expect(content!.height).toBeGreaterThan(layout!.height / 2)
    await nav.getByRole('button', { name: 'Extensions', exact: true }).click()
    await expect(settings.locator('.settings-section-title')).toBeInViewport()
  } finally {
    await h.close()
  }
})

test('Outliner keeps editable columns separate while saving field changes', async () => {
  const h = await launchApp('nl-outliner-width-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await resizeWindow(h, 1280, 900)
    await h.page.evaluate(() => {
      window.novalistStores.shell.getState().setMainView('manuscript')
      window.novalistStores.manuscript.getState().setMode('outliner')
    })
    const row = h.page.locator('.outliner-row:not(.outliner-head)').first()
    await expect(row).toBeVisible()
    expect(
      await row.evaluate((el) => {
        const cells = [...el.children].map((c) => c.getBoundingClientRect())
        return cells.every((r, i) => r.width >= 60 && (i === 0 || r.left >= cells[i - 1].right))
      })
    ).toBe(true)
    const goal = row.locator('input').nth(1)
    await goal.fill('Find the missing letter')
    await goal.blur()
    await expect
      .poll(() => h.rpc('scenes/getMeta', [book.chapters[0].guid, book.chapters[0].scenes[0].id]))
      .toMatchObject({ goal: 'Find the missing letter' })
  } finally {
    await h.close()
  }
})

test('Codex table fills its pane and export entries have separate readable rows', async () => {
  const h = await launchApp('nl-codex-table-layout-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    for (let i = 0; i < 12; i++) await h.rpc('entities/create', ['character', `Character ${i}`])
    await dismissTour(h.page)
    await resizeWindow(h, 1280, 900)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await h.page.locator('.codex-tab-table').click()
    await expect(h.page.locator('.codex-table tbody tr')).toHaveCount(12)
    const table = await h.page.locator('.codex-table').boundingBox()
    const wrap = await h.page.locator('.codex-table-wrap').boundingBox()
    expect(table!.width).toBeGreaterThan(wrap!.width * 0.9)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('export'))
    await h.page.locator('.export-view select').first().selectOption('codex')
    const labels = h.page.locator('.export-entity-group label')
    await expect(labels).toHaveCount(12)
    const first = await labels.first().boundingBox()
    expect(first!.height).toBeGreaterThanOrEqual(30)
    await labels.first().getByRole('checkbox').uncheck()
    await expect(labels.first().getByRole('checkbox')).not.toBeChecked()
    await expect(h.page.locator('.export-summary')).not.toContainText('Character 0')
  } finally {
    await h.close()
  }
})

test('shared naming and confirmation dialogs contain keyboard focus and restore it on cancel', async () => {
  const h = await launchApp('nl-dialog-keys-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    await h.rpc('project/createDraft', ['Second draft', null])
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('drafts'))
    const create = h.page.locator('.desktop-view-actions button')
    await create.click()
    const dialog = h.page.getByRole('dialog')
    await expect(dialog.locator('input')).toBeFocused()
    for (let i = 0; i < 8; i++) {
      await h.page.keyboard.press(i < 4 ? 'Tab' : 'Shift+Tab')
      expect(await dialog.evaluate((el) => el.contains(document.activeElement))).toBe(true)
    }
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).focus()
    await h.page.keyboard.press('Escape')
    await expect(dialog).toHaveCount(0)
    await expect(create).toBeFocused()
    const remove = h.page.locator('.drafts-row').first().locator('button').last()
    await remove.click()
    await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused()
    await h.page.keyboard.press('Shift+Tab')
    expect(await dialog.evaluate((el) => el.contains(document.activeElement))).toBe(true)
    await h.page.keyboard.press('Escape')
    await expect(dialog).toHaveCount(0)
    await expect(remove).toBeFocused()
    await expect(h.page.locator('.drafts-row')).toHaveCount(2)
  } finally {
    await h.close()
  }
})
