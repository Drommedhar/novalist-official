import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * What a Codex entry said before it was overwritten.
 *
 * Snapshots covered scenes and nothing else, so the wrong eye colour typed over
 * the right one had no answer inside the app - the manual's remedy was a backup
 * of the whole project.
 */
test('an overwritten codex entry can be put back from the detail pane', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-hist-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  // Its own Electron profile. The renderer remembers the last workspace in
  // localStorage, which lives in the profile, so a spec that shares the default
  // one can have a previously opened project restored over the top of the one it
  // just created - and then every id it holds belongs to a project the backend
  // is no longer in. That surfaced here as "Unknown entity" on a fresh id.
  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'electron-profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const id = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    const state = await rpc.request('project/create', [parent, 'History', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
    const mira = (await rpc.request('entities/create', ['character', 'Mira'])) as { id: string }
    // The right answer, then the wrong one typed over it.
    await rpc.request('entities/update', ['character', mira.id, { eyeColor: 'green' }])
    await rpc.request('entities/update', ['character', mira.id, { eyeColor: 'brown' }])
    return mira.id
  }, workDir)
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.locator('.codex-row', { hasText: 'Mira' }).click()

  const panel = page.locator('.codex-match', { hasText: /Earlier versions|Fruehere|历史版本/ })
  await expect(panel).toBeVisible({ timeout: 15_000 })
  await panel.locator('summary').click()

  const rows = panel.locator('.entity-history-row')
  await expect.poll(() => rows.count(), { timeout: 15_000 }).toBeGreaterThan(0)

  // The oldest revision holds the state before "green" was ever set, so the one
  // that puts green back is the newest.
  await rows.first().locator('.dialog-button').click()

  await expect
    .poll(
      () =>
        page.evaluate(
          (entityId) =>
            window.novalistRpc
              .request<{ eyeColor: string }>('entities/get', ['character', entityId])
              .then((e) => e.eyeColor),
          id
        ),
      { timeout: 15_000 }
    )
    .toBe('green')

  // And the state just replaced became a revision of its own, so the restore is
  // itself undoable.
  await expect.poll(() => rows.count(), { timeout: 15_000 }).toBeGreaterThan(1)

  await app.close()
})
