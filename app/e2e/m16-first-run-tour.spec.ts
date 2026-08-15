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

  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'electron-profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.start-screen')).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () =>
      page.evaluate(async () => {
        try {
          await window.novalistRpc.request('system/ping')
          return true
        } catch {
          return false
        }
      })
    )
    .toBe(true)

  await page.evaluate(async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Tour', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  // Offered on a first run, unasked.
  await expect(page.locator('.tour-card')).toBeVisible({ timeout: 20_000 })

  // It opens by saying where everything is. The walk used to start on the
  // Dashboard and leave the writer to work out how it had got there, which
  // stopped being survivable once the rail held five workspaces rather than
  // every view by name.
  await expect(page.locator('.tour-card')).toContainText('Five workspaces')
  await page.getByRole('button', { name: 'Next' }).click()

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

  // Writing-only tasks explain their prerequisite instead of navigating to an
  // empty editor or pretending Focus Peek can be demonstrated without a scene.
  await page.getByRole('button', { name: 'Next' }).click()
  await expect(page.locator('.tour-task')).toContainText('Open a scene from the binder first')
  await expect
    .poll(() => page.evaluate(() => window.novalistStores.shell.getState().mainView))
    .toBe('manuscript')

  // Skip is as reachable as Next, for somebody who already knows the app.
  await page.getByRole('button', { name: 'Skip' }).click()
  await expect(page.locator('.tour-card')).toBeHidden()
  await expect
    .poll(() => page.evaluate(() => window.novalistStores.shell.getState().mainView))
    .toBe('dashboard')

  // Skipping is durable, versioned installation state, separate from later
  // feature tips. The legacy one-bit key is retired after the richer write.
  await expect
    .poll(() =>
      page.evaluate(() => {
        const stored = JSON.parse(localStorage.getItem('nl.onboarding') ?? '{}') as {
          version?: number
          tour?: string
          tipsEnabled?: boolean
          tips?: Record<string, string>
        }
        return {
          version: stored.version,
          tour: stored.tour,
          tipsEnabled: stored.tipsEnabled,
          tips: stored.tips,
          legacy: localStorage.getItem('nl.tour.seen')
        }
      })
    )
    .toEqual({
      version: 1,
      tour: 'skipped',
      tipsEnabled: true,
      tips: {},
      legacy: null
    })

  // And it does not ask again: ordinary navigation leaves it closed.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await expect(page.locator('.tour-card')).toBeHidden()

  // A pre-schema installation with the old key migrates as completed and does
  // not get a surprise repeat after upgrading.
  await page.evaluate(() => {
    localStorage.removeItem('nl.onboarding')
    localStorage.setItem('nl.tour.seen', '1')
  })
  await page.reload()
  // A request made before the reloaded renderer has its port back stays
  // pending rather than rejecting, so an unbounded ping would hang this
  // predicate and time the poll out however long it is given. Each attempt is
  // bounded so the poll can actually retry.
  await expect
    .poll(
      async () =>
        page.evaluate(async () => {
          try {
            return await Promise.race([
              window.novalistRpc.request('system/ping').then(() => true),
              new Promise<boolean>((resolve) => setTimeout(() => resolve(false), 2_000))
            ])
          } catch {
            return false
          }
        }),
      { timeout: 30_000 }
    )
    .toBe(true)
  await expect(page.locator('.tour-card')).toBeHidden()

  await app.close()
})
