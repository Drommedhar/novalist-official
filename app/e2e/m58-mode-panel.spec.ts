import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * Nothing a mode holds is ever hidden.
 *
 * The rail this replaced held nineteen views before a single extension added
 * one, and a short window simply ran out of rail: the icons at the end fell off
 * the bottom into a "..." menu, and the ones a writer had gone out of their way
 * to install were the first to go. Being unable to find things is the complaint
 * the whole restructure started from, so an overflow menu is not an answer the
 * mode panel is allowed to give.
 *
 * What it gives instead is a scrolling list, and - past ten views - a filter.
 * The difference matters: the filter is an accelerator, and every view is in
 * the list whether or not it is showing.
 */

test('a mode lists every view it holds, at any window size', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-mode-panel-'))
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
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 20_000 })

  const rows = page.locator('.mode-panel-row')
  const filter = page.locator('.mode-panel-filter input')

  // World holds six views, so no filter and no hiding.
  await page.setViewportSize({ width: 1100, height: 1000 })
  await page.locator('.mode-rail-item[data-mode="world"]').click()
  await expect(rows).toHaveCount(6)
  await expect(filter).toHaveCount(0)
  await expect(page.locator('.mode-panel-row[data-view="languages"]')).toBeVisible()

  // A short window shortens the list's scroller, never the list.
  await page.setViewportSize({ width: 1100, height: 420 })
  await expect(rows).toHaveCount(6)
  await expect(page.locator('.mode-panel-row[data-view="languages"]')).toBeAttached()

  // Twelve contributed views join World, in their own group, last - so a newly
  // installed extension never reorders a core view or pushes one out of sight.
  await page.setViewportSize({ width: 1100, height: 1000 })
  await page.evaluate(() => {
    window.novalistStores.extensions.setState({
      views: Array.from({ length: 12 }, (_, i) => ({
        extensionId: `test.ext${i}`,
        key: `view${i}`,
        title: `Contributed ${i}`,
        iconPath: 'M4 4h16v16H4z',
        placement: 'main' as const,
        mode: 'world',
        entry: 'index.html',
        folderPath: ''
      }))
    })
  })
  await expect(rows).toHaveCount(18)
  const names = await rows.allInnerTexts()
  expect(names[0]).toContain('Codex')
  expect(names[names.length - 1]).toContain('Contributed 11')

  // Past ten, a filter appears. It narrows the list; it is not how a view
  // becomes reachable, which is why the count above is already eighteen.
  await expect(filter).toBeVisible()
  await filter.fill('Contributed 1')
  // "Contributed 1", and "Contributed 10" and "Contributed 11".
  await expect(rows).toHaveCount(3)
  await filter.fill('')
  await expect(rows).toHaveCount(18)

  // Opening a contributed view from the list selects it in place, rather than
  // through a menu that has to say which set it came from.
  await page.locator('.mode-panel-row', { hasText: 'Contributed 11' }).click()
  await expect(page.locator('.mode-panel-row[aria-current="true"]')).toContainText('Contributed 11')

  // Squeezed to almost nothing, the panel becomes an overlay holding the same
  // rows in the same order - and Settings is still one menu away, because it
  // is application scope and lives in the menu bar rather than on a rail that
  // can run out of room.
  await page.setViewportSize({ width: 780, height: 500 })
  await expect(page.locator('.shell')).toHaveAttribute('data-shell-capacity', 'compact')
  await page.evaluate(() => window.novalistStores.shell.getState().setModePanelOpen(true))
  await expect(rows).toHaveCount(18)
  await page.evaluate(() => window.novalistStores.shell.getState().openSettings())
  await expect(page.locator('#set-theme')).toBeVisible({ timeout: 15_000 })

  await app.close()
})
