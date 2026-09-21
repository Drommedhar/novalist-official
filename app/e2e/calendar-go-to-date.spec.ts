import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('go to date jumps centuries in every view, validates input and remembers the destination', async () => {
  const h = await launchApp('novalist-calendar-go-to-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await h.rpc('calendar/setAnchor', ['2026-03-14'])
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const view = h.page.locator('.calendar')
    const open = view.getByRole('button', { name: 'Go to date', exact: true })
    const input = view.getByRole('textbox', { name: 'Go to date', exact: true })
    const go = view.getByRole('button', { name: 'Go', exact: true })
    await expect(view.getByRole('button', { name: 'Story start', exact: true })).toBeDisabled()
    await expect(view.getByRole('button', { name: 'Today', exact: true })).toHaveCount(0)

    await open.click()
    await expect(input).toBeFocused()
    await expect(input).toHaveValue('2026-03-14')
    await input.fill('2326-03-14')
    await input.press('Enter')
    await expect(view.locator('.calendar-week-grid')).toBeVisible()
    await expect(view.locator('.calendar-header-label')).toHaveText('2326-03-08 - 2326-03-14')
    await expect(open).toBeFocused()
    await expect.poll(() => h.rpc('calendar/getAnchor')).toBe('2326-03-14')

    await view.getByRole('button', { name: 'Month', exact: true }).click()
    await open.click()
    for (const invalid of ['', 'not a date', '2326-02-29', '2326-13-01']) {
      await input.fill(invalid)
      await go.click()
      await expect(view.getByRole('alert')).toContainText('Enter a valid date')
      await expect(input).toHaveAttribute('aria-invalid', 'true')
      expect(await h.rpc('calendar/getAnchor')).toBe('2326-03-14')
    }
    await input.fill('2328-02-29')
    await go.click()
    await expect(view.locator('.calendar-header-label')).toHaveText('February 2328')
    await expect(view.locator('.calendar-cell:not(.outside)')).toHaveCount(29)

    await view.getByRole('button', { name: 'Year', exact: true }).click()
    await open.click()
    await input.fill('2628-12-25')
    await go.click()
    await expect(view.locator('.calendar-header-label')).toHaveText('2628')
    await expect(view.locator('.calendar-year')).toBeVisible()
    await expect.poll(() => h.rpc('calendar/getAnchor')).toBe('2628-12-25')

    await open.click()
    await input.fill('2026-01-01')
    await input.press('Escape')
    await expect(input).toHaveCount(0)
    await expect(open).toBeFocused()
    expect(await h.rpc('calendar/getAnchor')).toBe('2628-12-25')

    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    await open.click()
    await expect(input).toHaveValue('2628-12-25')
  } finally { await h.close() }
})

test('go to date respects custom month lengths, extra months, year zero and negative years', async () => {
  const h = await launchApp('novalist-calendar-go-to-custom-')
  try {
    const book = await seedBook(h, { Chapter: ['Distant scene'] })
    await h.rpc('calendar/setConfig', ['Custom', 'Cycle',
      Array.from({ length: 13 }, (_, i) => `Moon ${i + 1}`), Array(13).fill(40),
      ['Lightday', 'Darkday'], []])
    await h.rpc('calendar/setAnchor', ['0.1.1'])
    await h.rpc('project/setSceneDateRange', [book.chapters[0].guid, book.chapters[0].scenes[0].id, '300.13.40', '', ''])
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const view = h.page.locator('.calendar')
    const open = view.getByRole('button', { name: 'Go to date', exact: true })
    const input = view.getByRole('textbox', { name: 'Go to date', exact: true })
    await view.getByRole('button', { name: 'Month', exact: true }).click()
    await expect(view.locator('.calendar-header-label')).toHaveText('Moon 13 300 Cycle')
    await open.click()
    await input.fill('300.13.41')
    await input.press('Enter')
    await expect(view.getByRole('alert')).toBeVisible()
    expect(await h.rpc('calendar/getAnchor')).toBe('0.1.1')
    await input.fill('300.13.40')
    await input.press('Enter')
    await expect(view.locator('.calendar-header-label')).toHaveText('Moon 13 300 Cycle')
    await expect(view.getByRole('button', { name: 'Distant scene', exact: true })).toBeVisible()

    for (const year of [-300, 0]) {
      await open.click()
      await input.fill(`${year}.13.40`)
      await input.press('Enter')
      await expect(view.locator('.calendar-header-label')).toHaveText(`Moon 13 ${year} Cycle`)
      await expect.poll(() => h.rpc('calendar/getAnchor')).toBe(`${year}.13.40`)
    }
    await view.getByRole('button', { name: 'Story start', exact: true }).click()
    await expect(view.locator('.calendar-header-label')).toHaveText('Moon 13 300 Cycle')
  } finally { await h.close() }
})

test('calendar opens at the earliest story date and Story start returns there after navigation', async () => {
  const h = await launchApp('novalist-calendar-story-start-')
  try {
    const book = await seedBook(h, { Chapter: ['Later scene', 'First date'] })
    const chapter = book.chapters[0]
    await h.rpc('project/setSceneDateRange', [chapter.guid, chapter.scenes[0].id, '2626-03-14', '', ''])
    await h.rpc('project/setSceneDateRange', [chapter.guid, chapter.scenes[1].id, '2326-03-14', '', ''])
    await h.rpc('calendar/setAnchor', ['2026-03-14'])
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const view = h.page.locator('.calendar')
    await expect(view.locator('.calendar-header-label')).toHaveText('2326-03-08 - 2326-03-14')
    await expect(view.getByRole('button', { name: 'First date', exact: true })).toBeVisible()
    await expect(view.getByRole('button', { name: 'Today', exact: true })).toHaveCount(0)
    await view.getByRole('button', { name: 'Year', exact: true }).click()
    await view.locator('.calendar-nav button').last().click()
    await expect(view.locator('.calendar-header-label')).toHaveText('2327')
    await view.getByRole('button', { name: 'Story start', exact: true }).click()
    await expect(view.locator('.calendar-header-label')).toHaveText('2326')
    await view.locator('.calendar-nav button').last().click()
    await expect.poll(() => h.rpc('calendar/getAnchor')).toBe('2327-03-14')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    await expect(view.locator('.calendar-header-label')).toHaveText('2326-03-08 - 2326-03-14')
  } finally { await h.close() }
})
