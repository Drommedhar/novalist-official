import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

interface CalendarConfig {
  monthNames: string[]
  daysPerMonth: number[]
  weekdayNames: string[]
  eras: { name: string; startYear: number; countsDown: boolean }[]
  yearLength: number
}

test('calendar rows remain editable through saves and named rows survive reopening', async () => {
  const h = await launchApp('novalist-calendar-rows-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const setup = h.page.getByRole('button', { name: 'Calendar setup', exact: true })
    await setup.click()
    const panel = h.page.locator('.calendar-config')
    const custom = panel.getByRole('checkbox', { name: 'Use a custom in-world calendar', exact: true })
    await custom.check()
    await expect(custom).toBeEnabled()
    const months = panel.locator('.calendar-config-row').filter({ has: h.page.locator('.calendar-config-days') })
    const weekdays = panel.locator('.calendar-config-row').filter({ hasNot: h.page.locator('input[type="number"]') })
    await expect(months).toHaveCount(3)
    await expect(weekdays).toHaveCount(5)

    await panel.getByRole('button', { name: 'Add month', exact: true }).click()
    // The toggle is enabled only after the save reply has been handled. An
    // immediate count assertion alone could pass during the old brief flash.
    await expect(custom).toBeEnabled()
    await expect(months).toHaveCount(4)
    await panel.getByRole('button', { name: 'Add weekday', exact: true }).click()
    await expect(custom).toBeEnabled()
    await expect(weekdays).toHaveCount(6)
    await panel.getByRole('button', { name: 'Add an era', exact: true }).click()
    await expect(custom).toBeEnabled()
    await expect(panel.getByRole('textbox', { name: 'Era name', exact: true })).toHaveCount(1)

    // A slow earlier reply must not replace the newer name or remove other
    // unfinished rows. Delay an actual backend reply while the writer edits.
    await h.page.evaluate(() => {
      const rpc = window.novalistRpc
      const original = rpc.request.bind(rpc)
      let first = true
      rpc.request = (async <T,>(method: string, params?: unknown): Promise<T> => {
        const result = await original<T>(method, params)
        if (method === 'calendar/setConfig' && first) {
          first = false
          await new Promise((resolve) => setTimeout(resolve, 300))
        }
        return result
      }) as typeof rpc.request
    })
    await months.nth(3).getByRole('textbox').fill('Leaf ')
    await months.nth(3).getByRole('textbox').fill('Leaf fall')
    await months.nth(3).getByRole('spinbutton').fill('42')
    await weekdays.nth(5).getByRole('textbox').fill('Starday')
    await panel.getByRole('textbox', { name: 'Era name', exact: true }).fill('After the Fall')
    await expect(custom).toBeEnabled()
    await expect(months.nth(3).getByRole('textbox')).toHaveValue('Leaf fall')
    await expect(weekdays).toHaveCount(6)

    // The server sorts eras and rejects duplicate starting years. A new era
    // must stay available while its name and starting year are filled in.
    await panel.getByRole('button', { name: 'Add an era', exact: true }).click()
    await expect(custom).toBeEnabled()
    const eraNames = panel.getByRole('textbox', { name: 'Era name', exact: true })
    await expect(eraNames).toHaveCount(2)
    await eraNames.nth(1).fill('Before the Fall')
    await expect(custom).toBeEnabled()
    await expect(eraNames).toHaveCount(2)
    await panel.getByRole('spinbutton', { name: 'First year', exact: true }).nth(1).fill('-100')
    await panel.getByRole('checkbox', { name: 'Counts down', exact: true }).nth(1).check()
    await expect(custom).toBeEnabled()
    await expect(eraNames.nth(1)).toHaveValue('Before the Fall')

    const config = await h.rpc<CalendarConfig>('calendar/getConfig')
    expect(config.monthNames).toEqual(['First Month', 'Second Month', 'Third Month', 'Leaf fall'])
    expect(config.daysPerMonth).toEqual([30, 30, 30, 42])
    expect(config.weekdayNames).toContain('Starday')
    expect(config.yearLength).toBe(132)
    expect(config.eras).toEqual([
      { name: 'Before the Fall', startYear: -100, countsDown: true },
      { name: 'After the Fall', startYear: 0, countsDown: false }
    ])
    await setup.click()
    await setup.click()
    await expect(months.nth(3).getByRole('textbox')).toHaveValue('Leaf fall')
    await expect(weekdays.nth(5).getByRole('textbox')).toHaveValue('Starday')
    await expect(eraNames.nth(0)).toHaveValue('Before the Fall')
    await expect(eraNames.nth(1)).toHaveValue('After the Fall')

    // Clearing a saved name to replace it also leaves the input in place.
    await months.nth(3).getByRole('textbox').fill('')
    await expect(custom).toBeEnabled()
    await expect(months).toHaveCount(4)
    await months.nth(3).getByRole('textbox').fill('Long dusk')
    await expect(custom).toBeEnabled()
    await months.nth(0).getByRole('button').click()
    await weekdays.nth(0).getByRole('button').click()
    await expect(custom).toBeEnabled()
    const edited = await h.rpc<CalendarConfig>('calendar/getConfig')
    expect(edited.monthNames).toEqual(['Second Month', 'Third Month', 'Long dusk'])
    expect(edited.daysPerMonth).toEqual([30, 30, 42])
    expect(edited.weekdayNames).toHaveLength(5)
    expect(edited.yearLength).toBe(102)
  } finally {
    await h.close()
  }
})
