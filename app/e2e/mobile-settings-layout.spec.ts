import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

test('tablet Settings keeps the iPad category column beside the form when scrolling and resizing', async () => {
  const h = await launchApp('nl-tablet-settings-', {
    NOVALIST_FORCE_MOBILE: '1', NOVALIST_FORCE_TABLET: '1'
  })
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await resizeWindow(h, 1120, 800)
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('appearance'))
    await expect(h.page.locator('.tablet-shell .settings-view')).toBeVisible()

    for (const width of [1120, 1500, 820, 720]) {
      await resizeWindow(h, width, 800)
      const main = h.page.locator('.tablet-main .main-area')
      for (const scroll of [0, 900]) {
        await main.evaluate((element, top) => { element.scrollTop = top }, scroll)
        await expect.poll(() => h.page.evaluate(() => {
          const nav = document.querySelector('.settings-nav')!.getBoundingClientRect()
          const form = document.querySelector('.settings-section-surface')!.getBoundingClientRect()
          return nav.right <= form.left && nav.width > 0 && form.width > 0
        }), { message: `Categories must stay beside the form at width ${width}, scroll ${scroll}` }).toBe(true)
      }
      await h.page.locator('.settings-nav-item', { hasText: 'Accessibility' }).click()
      await expect(h.page.locator('#set-contextual-tips')).toBeVisible()
      await h.page.getByRole('button', { name: 'Scene templates', exact: true }).click()
      await expect(h.page.locator('.settings-section-surface')).toHaveAttribute('data-settings-section', 'sceneTemplates')
      await h.page.locator('.settings-nav-item', { hasText: 'Appearance' }).click()
      await expect(h.page.locator('#set-theme')).toBeVisible()
      expect(await main.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1)
    }
  } finally { await h.close() }
})
