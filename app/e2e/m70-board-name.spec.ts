import { test, expect } from '@playwright/test'
import { launchApp, seedBook, dismissTour } from './harness'

/**
 * Naming a planning board.
 *
 * Every board was created as "Board" with no way to say otherwise and no way to
 * rename one afterwards, so a writer with two boards had two entries in the
 * picker with the same name and nothing to tell them apart.
 */

test('a planning board can be named and renamed', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-board-name-')
  await dismissTour(h.page)
  await seedBook(h, { 'Chapter One': ['Scene One'] })

  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('canvas'))
  const toolbar = h.page.locator('.canvas-toolbar')
  await expect(toolbar).toBeVisible({ timeout: 15_000 })

  // Creating a board asks what it is called.
  await toolbar.getByRole('button', { name: 'New board' }).click()
  const dialog = h.page.locator('.dialog-card')
  await expect(dialog).toBeVisible({ timeout: 10_000 })
  await dialog.locator('.dialog-input').fill('Act Two knots')
  await dialog.getByRole('button', { name: 'OK' }).click()

  const picker = toolbar.locator('select')
  await expect(picker.locator('option')).toHaveText(['Act Two knots'], { timeout: 15_000 })

  // And a board that is already there can be renamed.
  await toolbar.getByRole('button', { name: 'Rename board' }).click()
  await expect(dialog).toBeVisible({ timeout: 10_000 })
  await dialog.locator('.dialog-input').fill('The knots of Act Two')
  await dialog.getByRole('button', { name: 'OK' }).click()

  await expect(picker.locator('option')).toHaveText(['The knots of Act Two'], { timeout: 15_000 })

  // The name is the book's, not the screen's: it is what the next session lists.
  await expect
    .poll(
      async () =>
        (await h.rpc<{ id: string; name: string }[]>('canvas/list')).map((b) => b.name),
      { timeout: 15_000 }
    )
    .toEqual(['The knots of Act Two'])

  await h.close()
})
