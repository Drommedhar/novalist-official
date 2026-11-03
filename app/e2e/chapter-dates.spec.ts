import { expect, test } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'
import type { ProjectStateDto } from '../src/renderer/src/stores/projectTypes'

for (const [calendar, start, end] of [
  ['Gregorian', '2042-03-04', '2042-03-06'],
  ['Custom', '812.2.38', '812.2.40']
]) test(`chapter dates can be created, edited, reopened and cleared (${calendar})`, async () => {
  const h = await launchApp('nl-chapter-dates-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await enterWriting(h.page)
    if (calendar === 'Custom')
      await h.rpc('calendar/setConfig', ['Custom', 'Cycle', ['First', 'Last'], [30, 40], ['Day']])
    await h.page.locator('[data-command="project.newChapter"]').click()
    const dialog = h.page.getByRole('dialog')
    await dialog.getByPlaceholder('Enter chapter title...').fill('Arrival')
    await expect(dialog.getByLabel('Start', { exact: true })).toBeVisible()
    await dialog.getByLabel('Start', { exact: true }).fill(start)
    await dialog.getByLabel('End', { exact: true }).fill(end)
    await dialog.getByRole('button', { name: 'OK', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    await h.page.locator('.binder-chapter-row', { hasText: 'Arrival' }).click({ button: 'right' })
    await h.page.getByText('Rename Chapter', { exact: true }).click()
    await expect(dialog.getByLabel('Start', { exact: true })).toHaveValue(start)
    await expect(dialog.getByLabel('End', { exact: true })).toHaveValue(end)
    await dialog.getByLabel('Start', { exact: true }).fill(end)
    await dialog.getByLabel('End', { exact: true }).fill('')
    await dialog.getByRole('button', { name: 'OK', exact: true }).click()
    await expect(dialog).toHaveCount(0)

    const saved = await h.rpc<ProjectStateDto>('project/getState')
    const reopened = await h.rpc<ProjectStateDto>('project/open', [saved.projectPath])
    await h.page.evaluate(state => window.novalistStores.project.getState().applyState(state), reopened)
    await enterWriting(h.page)
    await h.page.locator('.binder-chapter-row', { hasText: 'Arrival' }).click({ button: 'right' })
    await h.page.getByText('Rename Chapter', { exact: true }).click()
    await expect(dialog.getByLabel('Start', { exact: true })).toHaveValue(end)
    await expect(dialog.getByLabel('End', { exact: true })).toHaveValue('')
    await dialog.getByLabel('Start', { exact: true }).fill('')
    await dialog.getByRole('button', { name: 'OK', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    expect((await h.rpc<ProjectStateDto>('project/getState')).chapters[0].dateStart).toBe('')
  } finally {
    await h.close()
  }
})
