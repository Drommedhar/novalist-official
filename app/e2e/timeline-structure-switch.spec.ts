import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook, state } from './harness'

test('Add structure switches templates without accumulating entries, including a newly created structure (#20)', async () => {
  const h = await launchApp('nl-structure-switch-')
  try {
    const original = await seedBook(h, { Chapter: ['Written scene'] })
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
    const picker = h.page.getByRole('combobox', { name: 'Add structure...' })
    const titles = async () => {
      const timeline = await h.rpc<{ groups: { events: { isManual: boolean; title: string }[] }[] }>('timeline/get')
      return timeline.groups.flatMap(g => g.events).filter(e => e.isManual).map(e => e.title)
    }
    for (const [id, count] of [['seven-point', 7], ['three-act', 8], ['seven-point', 7], ['seven-point', 7]] as const) {
      await picker.selectOption(id)
      await expect.poll(async () => (await titles()).length).toBe(count)
    }

    await h.page.getByRole('button', { name: 'Structure', exact: true }).click()
    await h.page.getByRole('button', { name: 'New structure', exact: true }).click()
    const dialog = h.page.getByRole('dialog', { name: 'Story structure' })
    await dialog.locator('input').first().fill('My structure')
    await dialog.getByRole('button', { name: 'Add a beat' }).click()
    await dialog.getByRole('textbox', { name: 'Beat', exact: true }).fill('Arrival')
    await dialog.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    await expect(picker.locator('option', { hasText: 'My structure' })).toHaveCount(1)
    await picker.selectOption({ label: 'My structure' })
    await expect.poll(titles).toEqual(['Arrival'])
    await picker.selectOption('three-act')
    await expect.poll(async () => (await titles()).length).toBe(8)
    await picker.selectOption({ label: 'My structure' })
    await expect.poll(titles).toEqual(['Arrival'])
    expect((await state(h)).chapters).toEqual(original.chapters)
  } finally {
    await h.close()
  }
})
