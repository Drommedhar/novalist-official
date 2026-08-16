import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

/**
 * Ctrl+B bolds the selection.
 *
 * It used to toggle the binder, which left Windows and Linux writers with no
 * keyboard gesture for bold at all - the editor forwards Ctrl+B to the shell and
 * suppresses the native behaviour on the way, so the one gesture every writing
 * program has had for thirty years opened a panel instead. The binder moved to
 * Ctrl+Alt+B rather than bold going without.
 */
test('Ctrl+B bolds the selection, and does not open the binder', async () => {
  test.setTimeout(180_000)
  const app = await launchApp('nl-bold-')
  await seedBook(app, { One: ['A'] })
  const page = app.page

  await dismissTour(page)
  await enterWriting(page)
  await page.locator('.binder-scene-row').first().click()

  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.type('bold this')
  await expect(editor).toContainText('bold this', { timeout: 15_000 })

  const binderBefore = await page.evaluate(
    () => window.novalistStores.shell.getState().binderVisible
  )

  // Select-all is handled natively inside the editor rather than forwarded, so
  // it lands on its own schedule; the gesture under test acts on what it
  // selected and has to follow it rather than race it.
  await page.keyboard.press('Control+A')
  await page.waitForTimeout(300)
  await page.keyboard.press('Control+B')

  await expect(editor.locator('b, strong')).toHaveCount(1, { timeout: 15_000 })
  await expect(editor.locator('b, strong')).toContainText('bold this')

  // And the panel it used to open stayed where it was.
  expect(await page.evaluate(() => window.novalistStores.shell.getState().binderVisible)).toBe(
    binderBefore
  )

  await app.close()
})
