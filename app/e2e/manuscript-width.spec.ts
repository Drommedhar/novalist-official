import { test, expect } from '@playwright/test'
import { launchApp, seedBook, resizeWindow } from './harness'

for (const view of ['manuscript', 'write'] as const) {
  test(`${view} follows book width settings and expands when disabled`, async () => {
    const h = await launchApp('nl-manuscript-width-')
    try {
      await seedBook(h, { One: ['Opening'] })
      await resizeWindow(h, 1500, 900)
      await h.page.locator('.binder-scene-row').first().click()
      await h.page.evaluate(async () => {
        await window.novalistStores.settings.getState().load()
        await window.novalistStores.settings.getState().update('global', {
          enableBookWidth: true, bookPageFormat: 'Custom', bookTextBlockWidth: 4,
          bookFontFamily: 'Georgia', bookFontSize: 17, editorFontFamily: 'Arial', editorFontSize: 20,
          pageViewEnabled: false
        })
      })
      await h.page.evaluate(v => window.novalistStores.shell.getState().setMainView(v), view)
      const frame = h.page.frameLocator(`iframe[title="${view === 'write' ? 'editor' : 'manuscript'}"]`)
      const wrapper = frame.locator(view === 'write' ? '#editor-wrapper' : '#manuscript-wrapper')
      const editor = frame.locator(view === 'write' ? '#editor' : '.scene-editor').first()
      await expect(wrapper).toBeVisible()
      await expect(editor).toHaveCSS('font-size', '17px')
      await expect(editor).toHaveCSS('font-family', /Georgia/)
      const width = (): Promise<number> => wrapper.evaluate(el => el.getBoundingClientRect().width)
      const padding = view === 'write' ? 48 : 64
      await expect.poll(width).toBe(4 * 96 + padding)
      const narrow = await width()
      await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { bookTextBlockWidth: 6 }))
      await expect.poll(width).toBeGreaterThan(narrow + 100)
      const constrained = await width()
      await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { bookPageFormat: 'A5' }))
      await expect.poll(width).toBeCloseTo(4.63 * 96 + padding, 1)
      await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { enableBookWidth: false }))
      await expect.poll(width).toBeGreaterThan(constrained + 100)
      await expect(editor).toHaveCSS('font-size', '20px')
      await expect(editor).toHaveCSS('font-family', /Arial/)
      await resizeWindow(h, 800, 700)
      await expect.poll(() => wrapper.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true)
      await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { enableBookWidth: true }))
      await expect.poll(() => wrapper.evaluate(el => el.getBoundingClientRect().width <= innerWidth)).toBe(true)
    } finally {
      await h.close()
    }
  })
}
