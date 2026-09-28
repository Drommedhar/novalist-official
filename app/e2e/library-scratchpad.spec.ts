import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

for (const mobile of [false, true]) {
  test(`library scratchpad stays reachable and keeps notes (${mobile ? 'phone' : 'desktop'})`, async () => {
    const h = await launchApp('nl-library-scratchpad-', mobile ? { NOVALIST_FORCE_MOBILE: '1' } : {})
    try {
      await seedBook(h, { Chapter: ['Scene'] })
      await dismissTour(h.page)
      for (let i = 2; i <= 10; i++) await h.rpc('project/createBook', [`Book ${i}`])
      await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
      await resizeWindow(h, mobile ? 393 : 1200, 800)
      const library = h.page.locator('.project-library')
      const trigger = h.page.getByRole('button', { name: 'Scratchpad', exact: true })
      await expect(trigger).toBeInViewport()
      await library.evaluate((element) => { element.scrollTop = element.scrollHeight })
      const scrollTop = await library.evaluate((element) => element.scrollTop)
      expect(scrollTop).toBeGreaterThan(0)
      await expect(trigger).toBeInViewport()
      await trigger.click()

      const dialog = h.page.getByRole('dialog', { name: 'Scratchpad', exact: true })
      const input = dialog.getByRole('textbox', { name: 'Something you do not want to lose' })
      await expect(input).toBeFocused()
      await input.fill('An unfinished thought.\nKeep the second line.')
      await h.page.keyboard.press('Escape')
      await expect(dialog).toBeHidden()
      await expect(trigger).toBeFocused()
      expect(await library.evaluate((element) => element.scrollTop)).toBe(scrollTop)
      await trigger.click()
      await expect(input).toHaveValue('An unfinished thought.\nKeep the second line.')
      await expect(input).toBeFocused()
      await h.page.keyboard.press('Control+Enter')
      await expect(input).toHaveValue('')
      await expect(dialog.locator('.scratchpad-note')).toHaveCount(1)
      await dialog.getByRole('button', { name: 'Close', exact: true }).click()

      // Quick Capture can add notes while the retained dialog is closed.
      await h.rpc('scratchpad/add', ['Another idea from Quick Capture.'])
      await trigger.click()
      await expect(dialog.locator('.scratchpad-note')).toHaveCount(2)
      await dialog.locator('button').last().focus()
      await h.page.keyboard.press('Tab')
      await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeFocused()
      await h.page.screenshot({ path: `test-results/library-scratchpad-${mobile ? 'phone' : 'desktop'}.png` })
      await h.page.keyboard.press('Escape')
      await expect(trigger).toBeFocused()
      await h.page.reload()
      await trigger.click()
      await expect(dialog.locator('.scratchpad-note')).toHaveCount(2)
    } finally { await h.close() }
  })
}
