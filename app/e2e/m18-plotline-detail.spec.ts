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
 *
 * It is a screen now rather than a dialog, so the test also holds the two
 * things that cost work when it was one: the step list has a scroller of its
 * own, and leaving with unsaved edits asks before throwing them away.
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
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })
  await page.evaluate(() => window.novalistRpc.request('plot/createPlotline', ['The debt']))

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('plotGrid'))
  const row = page.locator('.plotgrid-rowlabel', { hasText: 'The debt' })
  await expect(row).toBeVisible({ timeout: 15_000 })

  // The detail lives behind the row's own context menu.
  await row.click({ button: 'right' })
  await page.locator('.context-menu-item', { hasText: /Thread detail|Strang-Details/ }).click()

  // The grid steps aside for the thread rather than dimming behind a card.
  const screen = page.locator('.plotline-detail')
  await expect(screen).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.plotgrid-table')).toHaveCount(0)

  // Earlier versions are a tab, not something stacked under the fields.
  await expect(screen.locator('.plotline-detail-tab')).toHaveCount(2)

  await screen.locator('#plotline-importance').selectOption('Main')
  await screen.locator('#plotline-description').fill('Who owes whom, and what it costs.')
  await screen.locator('.plotline-add-step').click()
  await screen.locator('.plotline-step .inspector-input').fill('The debt is called in.')

  // Steps live in a scroller of their own, so a long list cannot push the rest
  // of the form off a short monitor.
  const stepsOverflow = await screen
    .locator('.plotline-steps')
    .evaluate((el) => getComputedStyle(el).overflowY)
  expect(stepsOverflow).toBe('auto')

  // Leaving with edits in hand asks first - a stray click used to lose them.
  await screen.locator('.plotline-detail-back').click()
  const prompt = page.locator('.dialog-card[role="dialog"]')
  await expect(prompt).toBeVisible({ timeout: 10_000 })
  await prompt.locator('.dialog-button', { hasText: /Stay here|Hier bleiben/ }).click()
  await expect(screen).toBeVisible()

  // And the question is the shell's, not this screen's: switching view is the
  // same loss through a different door, so it is held the same way. Keeping the
  // work is on offer rather than only losing it.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
  await expect(prompt).toBeVisible({ timeout: 10_000 })
  await prompt.locator('.dialog-button.primary').click()
  await expect(screen).toHaveCount(0, { timeout: 10_000 })
  await expect(page.locator('.timeline')).toBeVisible({ timeout: 10_000 })

  // Both marks read from the grid without opening anything.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('plotGrid'))
  await expect(row.locator('.plotgrid-importance')).toBeVisible({ timeout: 10_000 })
  await expect(row.locator('.plotgrid-unresolved')).toBeVisible()

  // Discarding really discards: the thread keeps what was saved.
  await row.click({ button: 'right' })
  await page.locator('.context-menu-item', { hasText: /Thread detail|Strang-Details/ }).click()
  await expect(screen).toBeVisible({ timeout: 10_000 })
  await screen.locator('#plotline-description').fill('Typed by mistake.')
  await screen.locator('.plotline-detail-back').click()
  await prompt.locator('.dialog-button.danger').click()
  await expect(screen).toHaveCount(0, { timeout: 10_000 })

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
