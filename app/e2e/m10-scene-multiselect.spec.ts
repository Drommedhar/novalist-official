import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * Multi-select in the binder and the bulk operations it drives.
 *
 * The point of asserting this end to end: the selection lives in one store but
 * is read by four views, and the bulk RPCs return the new project state rather
 * than the caller refetching it. Both are exactly the kind of wiring that unit
 * tests pass while the app shows nothing.
 */

test('ctrl-click builds a selection and the bulk bar acts on it', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-bulk-'))

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(async (dir) => {
    const state = await window.novalistRpc.request('project/create', [dir, 'Bulk Novel', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  await page.evaluate(async () => {
    const store = window.novalistStores.project.getState()
    await store.createChapter('Chapter One')
    const guid = window.novalistStores.project.getState().chapters[0].guid
    for (const title of ['Scene A', 'Scene B', 'Scene C'])
      await window.novalistStores.project.getState().createScene(guid, title)
  })

  const rows = page.locator('.binder-scene-row')
  await expect.poll(() => rows.count()).toBe(3)

  // A plain click opens a scene and selects nothing — the bar stays away.
  await rows.nth(0).click()
  await expect(page.locator('.scene-bulk-bar')).toHaveCount(0)

  // Ctrl-click adds to the selection; the bar appears at two.
  await rows.nth(0).click({ modifiers: ['ControlOrMeta'] })
  await rows.nth(1).click({ modifiers: ['ControlOrMeta'] })
  await expect(page.locator('.scene-bulk-bar')).toBeVisible()
  await expect(page.locator('.binder-scene-row.selected')).toHaveCount(2)

  // Ctrl-click again takes one back out.
  await rows.nth(1).click({ modifiers: ['ControlOrMeta'] })
  await expect(page.locator('.binder-scene-row.selected')).toHaveCount(1)

  // The bar's clear button drops the selection entirely.
  await rows.nth(1).click({ modifiers: ['ControlOrMeta'] })
  await page.locator('.scene-bulk-bar button').last().click()
  await expect(page.locator('.binder-scene-row.selected')).toHaveCount(0)

  // Shift-click covers everything from the anchor to the row clicked, replacing
  // whatever was selected — the behaviour a file explorer has.
  await rows.nth(0).click({ modifiers: ['ControlOrMeta'] })
  await rows.nth(2).click({ modifiers: ['Shift'] })
  await expect(page.locator('.binder-scene-row.selected')).toHaveCount(3)

  // Archiving the selection empties the chapter and the bar goes away with it.
  await page.locator('.scene-bulk-bar button', { hasText: 'Archive' }).first().click()
  await page.locator('.dialog-card .dialog-button.danger').click()
  await expect.poll(() => rows.count()).toBe(0)
  await expect(page.locator('.scene-bulk-bar')).toHaveCount(0)

  // The scenes really moved: the backend lists all three as archived.
  const archived = await page.evaluate(
    async () => (await window.novalistRpc.request('scenes/archived')) as unknown[]
  )
  expect(archived).toHaveLength(3)

  await app.close()
})
