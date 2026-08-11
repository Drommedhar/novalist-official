import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * The activity bar holds nineteen views before a single extension adds one, and
 * a short window used to simply run out of rail: the icons at the end fell off
 * the bottom, taking Extensions and Settings with them. Extensions is how you
 * reach the extension whose icon just vanished, and Settings is how you change
 * anything at all, so neither may ever be the thing that overflows.
 */

test('the activity bar keeps Extensions and Settings reachable at any window size', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-rail-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await page.evaluate(async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Rail', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 20_000 })

  const more = page.locator('.activity-bar-more')
  const settings = page.locator('.activity-bar-bottom .activity-bar-item').last()
  const extensions = page.locator('.activity-bar-bottom .activity-bar-item').first()

  // Tall enough for every view: nothing is hidden, so nothing offers to unhide.
  await page.setViewportSize({ width: 1100, height: 1000 })
  await expect(more).toHaveCount(0)
  await expect(settings).toBeVisible()
  await expect(page.locator('.activity-bar-item[data-view="git"]')).toBeVisible()

  // Short window: the tail of the rail moves into the menu, the pinned pair stays.
  await page.setViewportSize({ width: 1100, height: 520 })
  await expect(more).toBeVisible({ timeout: 10_000 })
  await expect(settings).toBeVisible()
  await expect(extensions).toBeVisible()

  // The menu lands on screen and reaches a view the rail no longer shows.
  await more.click()
  const menu = page.locator('.context-menu')
  await expect(menu).toBeVisible({ timeout: 10_000 })
  const box = await menu.boundingBox()
  expect(box).not.toBeNull()
  expect(box!.y).toBeGreaterThanOrEqual(0)
  expect(box!.y + box!.height).toBeLessThanOrEqual(520)
  await menu.getByRole('menuitem').last().click()
  await expect(menu).toHaveCount(0)

  // Views an extension contributed sit at the end of the rail, so they are the
  // first to overflow - and the first the writer loses track of.
  await page.evaluate(() => {
    window.novalistStores.extensions.setState({
      views: Array.from({ length: 8 }, (_, i) => ({
        extensionId: `test.ext${i}`,
        key: `view${i}`,
        title: `Contributed ${i}`,
        iconPath: 'M4 4h16v16H4z',
        placement: 'main' as const,
        entry: 'index.html',
        folderPath: ''
      }))
    })
  })
  await page.setViewportSize({ width: 1100, height: 1000 })
  await expect(more).toBeVisible({ timeout: 10_000 })
  await expect(settings).toBeVisible()

  await more.click()
  await expect(page.locator('.context-menu')).toBeVisible({ timeout: 10_000 })
  await expect(page.getByRole('menuitem', { name: 'Contributed 7' })).toBeVisible()
  await page.getByRole('menuitem', { name: 'Contributed 7' }).click()
  // Opening one from the menu selects it, and the button says which set it came
  // from rather than leaving the rail looking as though nothing is chosen.
  await expect(more).toHaveClass(/active/, { timeout: 10_000 })

  // Squeezed to almost nothing, the pinned pair is still there to be clicked.
  await page.setViewportSize({ width: 1100, height: 320 })
  await expect(settings).toBeVisible()
  await expect(extensions).toBeVisible()
  await settings.click()
  await expect(page.locator('#set-theme')).toBeVisible({ timeout: 15_000 })

  await app.close()
})
