import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('custom names, lengths, eras, scenes and navigation reach all calendar views', async () => {
  const h = await launchApp('novalist-custom-calendar-')
  try {
    const book = await seedBook(h, { Chapter: ['Crossing', 'Companion'] })
    const chapter = book.chapters[0]
    await h.rpc('calendar/setConfig', ['Custom', 'Cycle', ['Rimefall', 'Thaw', 'Highsun'], [30, 30, 40],
      ['Firstday', 'Starday', 'Moonday', 'Fireday', 'Lastday'],
      [{ name: 'After the Fall', startYear: 0, countsDown: false }]])
    await h.rpc('project/setSceneDateRange', [chapter.guid, chapter.scenes[0].id, '812.3.39', '813.1.2', ''])
    await h.rpc('project/setSceneDateRange', [chapter.guid, chapter.scenes[1].id, '812.3.40', '', ''])
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const view = h.page.locator('.calendar')
    await expect(view.locator('.calendar-week-daylabel')).toHaveText(['Firstday 36', 'Starday 37', 'Moonday 38', 'Fireday 39', 'Lastday 40'])
    await expect(view.locator('.calendar-header-label')).toContainText('813 After the Fall')
    await expect(view.getByRole('button', { name: 'Today', exact: true })).toHaveCount(0)
    await expect(view.locator('.calendar-event-title').filter({ hasText: 'Crossing' })).toHaveCount(2)
    await view.getByRole('button', { name: 'Month', exact: true }).click()
    await expect(view.locator('.calendar-cell:not(.outside)')).toHaveCount(40)
    await expect(view.locator('.calendar-weekday')).toHaveText(['Firstday', 'Starday', 'Moonday', 'Fireday', 'Lastday'])
    await expect(view.locator('.calendar-header-label')).toHaveText('Highsun 813 After the Fall')
    // A real drag must update the explicit story date range, including its end.
    const source = view.locator('.calendar-cell').filter({ has: h.page.locator('.calendar-cell-date', { hasText: /^39$/ }) })
    const target = view.locator('.calendar-cell').filter({ has: h.page.locator('.calendar-cell-date', { hasText: /^38$/ }) })
    await source.getByRole('button', { name: 'Crossing', exact: true }).dragTo(target)
    await expect.poll(async () => (await h.rpc<{ dateStart: string }>('project/getSceneEdit', [chapter.guid, chapter.scenes[0].id])).dateStart).toBe('812.3.38')
    const shifted = await h.rpc<{ dateEnd: string }>('project/getSceneEdit', [chapter.guid, chapter.scenes[0].id])
    expect(shifted.dateEnd).toBe('813.1.1')
    await view.getByRole('button', { name: 'Year', exact: true }).click()
    await expect(view.locator('.calendar-year-title')).toHaveText(['Rimefall', 'Thaw', 'Highsun'])
    await expect(view.locator('.calendar-year-month').nth(2)).toContainText('Crossing')
    // The display updates immediately after autosave, without closing setup.
    await view.getByRole('button', { name: 'Calendar setup', exact: true }).click()
    const panel = view.locator('.calendar-config')
    const months = panel.locator('.calendar-config-row').filter({ has: h.page.locator('.calendar-config-days') })
    await months.nth(2).getByRole('textbox').fill('Long dusk')
    await expect(view.locator('.calendar-year-title').nth(2)).toHaveText('Long dusk')
    await view.getByRole('button', { name: 'Calendar setup', exact: true }).click()
    await view.locator('.calendar-year-head').nth(2).click()
    await view.locator('.calendar-nav button').last().click()
    await expect(view.locator('.calendar-header-label')).toHaveText('Rimefall 814 After the Fall')
    await expect(view.locator('.calendar-cell:not(.outside)')).toHaveCount(30)
    await expect(view.locator('.calendar-event-title').filter({ hasText: 'Crossing' })).toHaveCount(1)
    // Reopening the view uses the persisted custom anchor.
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    await expect(view.locator('.calendar-header-label')).toContainText('814 After the Fall')
    await view.getByRole('button', { name: 'Calendar setup', exact: true }).click()
    await panel.getByRole('checkbox', { name: 'Use a custom in-world calendar', exact: true }).uncheck()
    await expect(view.getByRole('button', { name: 'Today', exact: true })).toBeVisible()
    await view.getByRole('button', { name: 'Year', exact: true }).click()
    await expect(view.locator('.calendar-year-title')).toHaveCount(12)
    await expect(view.locator('.calendar-year-title').first()).toHaveText('January')
  } finally { await h.close() }
})

test('a thirteen-month calendar supports negative eras and shifting a selected group across year zero', async () => {
  const h = await launchApp('novalist-calendar-thirteen-')
  try {
    const book = await seedBook(h, { Chapter: ['First', 'Second'] })
    const chapter = book.chapters[0]
    await h.rpc('calendar/setConfig', ['Custom', '', Array.from({ length: 13 }, (_, i) => `Moon ${i + 1}`),
      Array(13).fill(3), ['Lightday', 'Darkday'], [
        { name: 'Before the Fall', startYear: -100, countsDown: true },
        { name: 'After the Fall', startYear: 0, countsDown: false }
      ]])
    await h.rpc('project/setSceneDateRange', [chapter.guid, chapter.scenes[0].id, '-1.13.2', '', ''])
    await h.rpc('project/setSceneDateRange', [chapter.guid, chapter.scenes[1].id, '-1.13.3', '', ''])
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const view = h.page.locator('.calendar')
    await expect(view.locator('.calendar-header-label')).toContainText('1 Before the Fall')
    await expect(view.locator('.calendar-week-daylabel')).toHaveText(['Lightday 2', 'Darkday 3'])
    await view.getByRole('button', { name: 'Year', exact: true }).click()
    await expect(view.locator('.calendar-year-title')).toHaveCount(13)
    await view.locator('.calendar-year-head').last().click()
    const first = view.getByRole('button', { name: 'First', exact: true })
    const second = view.getByRole('button', { name: 'Second', exact: true })
    await first.click({ modifiers: ['ControlOrMeta'] })
    await second.click({ modifiers: ['ControlOrMeta'] })
    await expect(view.locator('.calendar-event.selected')).toHaveCount(2)
    const target = view.locator('.calendar-cell:not(.outside)').filter({ has: h.page.locator('.calendar-cell-date', { hasText: /^3$/ }) })
    await first.dragTo(target)
    await expect.poll(async () => (await h.rpc<{ dateStart: string }>('project/getSceneEdit', [chapter.guid, chapter.scenes[0].id])).dateStart).toBe('-1.13.3')
    expect((await h.rpc<{ dateStart: string }>('project/getSceneEdit', [chapter.guid, chapter.scenes[1].id])).dateStart).toBe('0.1.1')
    await view.locator('.calendar-nav button').last().click()
    await expect(view.locator('.calendar-header-label')).toHaveText('Moon 1 1 After the Fall')
    await expect(view.getByRole('button', { name: 'Second', exact: true })).toBeVisible()
    expect(await view.locator('.calendar-month').evaluate((el) => getComputedStyle(el).gridTemplateColumns.split(' ').length)).toBe(2)
  } finally { await h.close() }
})
