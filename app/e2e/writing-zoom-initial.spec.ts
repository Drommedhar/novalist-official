import { expect, test } from '@playwright/test'
import { launchApp, resizeWindow, seedBook } from './harness'

for (const pageView of [false, true]) {
  test(`first scene fits the available width once and preserves manual zoom, page view=${pageView}`, async () => {
    const h = await launchApp('nl-initial-zoom-')
    try {
      await seedBook(h, { One: ['Opening', 'Next scene'] })
      await resizeWindow(h, 1500, 900)
      await h.page.evaluate(async (pages) => {
        await window.novalistStores.settings.getState().update('global', {
          enableBookWidth: !pages, bookPageFormat: 'Custom', bookTextBlockWidth: 4.2,
          pageViewEnabled: pages, editorFontSize: 17, bookFontSize: 17,
          spellCheckEnabled: false, grammarCheckEnabled: false
        })
      }, pageView)
      await h.page.locator('.binder-scene-row').first().click()
      const frame = h.page.frameLocator('iframe[title="editor"]')
      const editor = frame.locator('#editor')
      const scale = () => editor.evaluate(element => Number(getComputedStyle(element).getPropertyValue('--writing-zoom-scale')))
      await expect.poll(scale).toBeGreaterThan(1)
      await expect.poll(() => frame.locator(pageView ? '.nv-page' : '#editor-wrapper').first().evaluate(element =>
        element.getBoundingClientRect().width / window.innerWidth)).toBeGreaterThan(0.9)
      expect(await frame.locator('#editor-wrapper').evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1)
      const fitted = await scale()
      await editor.hover()
      await h.page.keyboard.down('Control')
      await h.page.mouse.wheel(0, 100)
      await h.page.keyboard.up('Control')
      await expect.poll(scale).toBeLessThan(fitted)
      const chosen = await scale()
      await h.page.locator('.binder-scene-row').nth(1).click()
      await expect.poll(scale).toBe(chosen)
      await resizeWindow(h, 1300, 900)
      await expect.poll(() => h.page.locator('iframe[title="editor"]').evaluate(element => element.clientWidth)).toBeLessThan(1200)
      await expect.poll(scale).toBe(chosen)
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
      await expect(editor).toHaveCount(0)
      await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('write'))
      await expect.poll(scale).toBe(chosen)
    } finally {
      await h.close()
    }
  })
}
