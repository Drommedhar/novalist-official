import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

for (const [width, height] of [[1440, 1000], [1152, 720]]) {
  test(`timeline event descriptions remain editable at ${width}x${height} (#58)`, async () => {
    const h = await launchApp('nl-timeline-description-')
    try {
      await seedBook(h, { Chapter: ['Scene'] })
      await dismissTour(h.page)
      await resizeWindow(h, width, height)
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
      await h.page.getByRole('button', { name: 'Add Event', exact: true }).click()
      const dialog = h.page.getByRole('dialog', { name: 'Title', exact: true })
      await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('Harvest festival')

      const description = dialog.getByRole('textbox', { name: 'Description', exact: true })
      await dialog.evaluate(element => { element.scrollTop = element.scrollHeight })
      // A contenteditable can have a normal bounding box while its flex-item
      // ancestor has collapsed to a border and clips the entire writing area.
      await expect(description).toBeInViewport()
      await expect.poll(() => dialog.locator('.md-editor').evaluate(element => {
        const editor = element.getBoundingClientRect()
        const surface = element.querySelector('.md-surface')!.getBoundingClientRect()
        return surface.top >= editor.top && surface.bottom <= editor.bottom
      })).toBe(true)
      await description.fill('The village gathers for the harvest festival.')
      const save = dialog.getByRole('button', { name: 'Save', exact: true })
      await expect(save).toBeInViewport()
      await save.click()
      await expect(dialog).toHaveCount(0)
      const event = h.page.locator('.timeline-event').filter({ hasText: 'Harvest festival' })
      await expect(event).toContainText('The village gathers for the harvest festival.')

      await event.click()
      await description.scrollIntoViewIfNeeded()
      await expect(description).toBeInViewport()
      await expect(description).toHaveText('The village gathers for the harvest festival.')
      await description.fill('The festival continues into the evening.')
      await save.click()
      await expect(event).toContainText('The festival continues into the evening.')
      await event.click()
      await expect(description).toHaveText('The festival continues into the evening.')
    } finally {
      await h.close()
    }
  })
}
