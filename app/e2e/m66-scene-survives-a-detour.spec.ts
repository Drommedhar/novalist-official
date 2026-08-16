import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

/**
 * Looking at something else does not close the scene.
 *
 * A pane that has been closed and a pane that is showing the Timeline were the
 * same thing to the store: both meant "no longer an editor", and an editor that
 * was no longer an editor had its scene forgotten. So glancing at the outline
 * and coming back left the writer at "Choose a scene in the binder" with their
 * place gone - a detour of two clicks, and the book was shut.
 *
 * The words themselves were never at risk; they were flushed on the way out.
 * What was lost was where the writer was, which for a detour is the whole
 * point of taking one.
 */
test('a detour to another view leaves the scene where it was', async () => {
  test.setTimeout(180_000)
  const app = await launchApp('nl-detour-')
  await seedBook(app, { One: ['A', 'B'] })
  const page = app.page

  await dismissTour(page)
  await enterWriting(page)
  await page.locator('.binder-scene-row').first().click()

  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.type('Hello from the scene.')
  // Wait for the shell to have the prose, not merely the iframe: the editor
  // reports a change on a short timer, and leaving before it fires is a race
  // about saving rather than the thing under test here.
  await expect
    .poll(() => page.evaluate(() => window.novalistStores.project.getState().openScenePlainText), {
      timeout: 15_000
    })
    .toContain('Hello from the scene.')

  // Off to the Timeline and straight back, the way a writer checks a date.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
  await expect(page.locator('.editor-frame')).toHaveCount(0, { timeout: 15_000 })

  // While away, the shell still knows which scene is being written - that is
  // what the inspector and the status bar go on describing.
  expect(await page.evaluate(() => window.novalistStores.project.getState().openSceneId)).not.toBeNull()

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('write'))
  await expect(editor).toContainText('Hello from the scene.', { timeout: 20_000 })
  // And the binder still points at it, so the writer can see where they are.
  await expect(page.locator('.binder-scene-row.active')).toHaveCount(1, { timeout: 10_000 })

  await app.close()
})
