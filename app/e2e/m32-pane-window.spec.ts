import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'

/**
 * A pane torn out into its own window.
 *
 * The Codex on a second monitor while the manuscript stays where it is. What
 * matters, and what no unit test can reach, is that the second window is a
 * real one: its own connection to the same backend, showing the real view
 * rather than a picture of it. A window that opens blank, or opens showing the
 * main shell again, would look fine in every other test.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('a pane opens in its own window and shows the real view', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-popout-'))
  const projectCopy = join(workDir, 'project')
  copyProject(REAL_PROJECT, projectCopy)
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  // Tear the Codex out.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.waitForTimeout(600)
  const opened = app.waitForEvent('window')
  await page.evaluate(() => window.novalist.openPaneWindow('codex'))
  const second = await opened

  // The real Codex, in the second window: entries the backend gave it, not an
  // empty shell.
  await expect(second.locator('.codex-hub, .codex-list, .entity-list').first()).toBeVisible({
    timeout: 30_000
  })
  // And it is a torn-off pane rather than another copy of the whole app.
  await expect(second.locator('.app-shell.detached')).toBeVisible({ timeout: 15_000 })
  await expect(second.locator('.activity-bar')).toHaveCount(0)

  // The window it came from is untouched.
  await expect(page.locator('.activity-bar')).toBeVisible()

  await app.close()
})
