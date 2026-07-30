import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * Novalist has eighteen views behind four activity-bar groups, and a writer at
 * a blank Dashboard has no way to know the Plot Grid or the Codex are there.
 * The tour offers itself once, walks through the views for real, and never
 * asks again.
 */
test('the tour offers itself once and walks through real views', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-tour-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  // The "seen" flag lives in local storage, which Electron keeps in its user
  // data directory and shares across launches - that is the point of it, and it
  // means this test has to start from a clean slate to see the first run at all.
  await page.evaluate(() => localStorage.removeItem('nl.tour.seen'))

  await page.evaluate(async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Tour', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  // Offered on a first run, unasked.
  await expect(page.locator('.tour-card')).toBeVisible({ timeout: 20_000 })

  // Each stop actually goes there rather than describing it.
  await expect
    .poll(() => page.evaluate(() => window.novalistStores.shell.getState().mainView), {
      timeout: 10_000
    })
    .toBe('dashboard')
  await page.getByRole('button', { name: 'Next' }).click()
  await expect
    .poll(() => page.evaluate(() => window.novalistStores.shell.getState().mainView), {
      timeout: 10_000
    })
    .toBe('manuscript')

  // Skip is as reachable as Next, for somebody who already knows the app.
  await page.getByRole('button', { name: 'Skip' }).click()
  await expect(page.locator('.tour-card')).toBeHidden()

  // And it does not ask again: reopening the shell leaves it closed.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await expect(page.locator('.tour-card')).toBeHidden()

  // Put back what this test cleared. Local storage lives in Electron's user
  // data directory and is shared across every launch in the suite, so leaving
  // the flag cleared makes the tour open in every test that runs after this
  // one - and a tour walking the views underneath another test's first
  // page.evaluate destroys the execution context it was running in.
  await page.evaluate(() => localStorage.setItem('nl.tour.seen', '1'))

  await app.close()
})
