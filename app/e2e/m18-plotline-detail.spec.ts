import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * A plot thread is an object, not a row of ticks.
 *
 * Importance, cast and resolution steps were all in PlotlineData, all handled
 * by plot/setPlotlineDetail, and reachable from no screen at all - the exact
 * shape of failure the project rules describe, where 100% backend coverage says
 * nothing about whether production can get there. This test goes through the
 * grid, so it fails if the route disappears again.
 */
test('a plot thread carries importance and resolution steps, from the grid', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-plotline-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  // Split in two so the plotline is created against the shell rather than the
  // start screen.
  await evaluateWhenReady(page, async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Threads', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })
  await page.evaluate(() => window.novalistRpc.request('plot/createPlotline', ['The debt']))

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('plotGrid'))
  const row = page.locator('.plotgrid-rowlabel', { hasText: 'The debt' })
  await expect(row).toBeVisible({ timeout: 15_000 })

  // The detail lives behind the row's own context menu.
  await row.click({ button: 'right' })
  await page.locator('.context-menu-item', { hasText: /Thread detail|Strang-Details/ }).click()

  const dialog = page.locator('.plotline-detail')
  await expect(dialog).toBeVisible({ timeout: 10_000 })

  await dialog.locator('#plotline-importance').selectOption('Main')
  await dialog.locator('#plotline-description').fill('Who owes whom, and what it costs.')
  await dialog.locator('.plotline-add-step').click()
  await dialog.locator('.plotline-step .inspector-input').fill('The debt is called in.')
  await dialog.locator('.dialog-button.primary').click()
  await expect(dialog).toHaveCount(0, { timeout: 10_000 })

  // Both marks read from the grid without opening anything.
  await expect(row.locator('.plotgrid-importance')).toBeVisible({ timeout: 10_000 })
  await expect(row.locator('.plotgrid-unresolved')).toBeVisible()

  // And it survived the round trip to disk, rather than only the render.
  const saved = await page.evaluate(async () => {
    const grid = (await window.novalistRpc.request('plot/grid', ['plotline'])) as {
      plotlines: { name: string; importance: string; description: string; steps: unknown[] }[]
    }
    return grid.plotlines.find((p) => p.name === 'The debt')
  })
  expect(saved?.importance).toBe('Main')
  expect(saved?.description).toBe('Who owes whom, and what it costs.')
  expect(saved?.steps).toHaveLength(1)

  await app.close()
})
