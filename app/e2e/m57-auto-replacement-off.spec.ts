import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * Switching auto-replacement off has to reach the prose surface, not just the
 * settings file. A writer who turns it off and still watches their hyphens
 * turn into dashes has been told no and overruled.
 *
 * The cleanup dialog is checked in the same run because it is the one pass
 * that could put every substitution back over a whole book at once.
 */

test('auto-replacement off leaves the typed characters alone', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-autorep-'))
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

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () => (await editor.innerText()).trim().length, { timeout: 15_000 })
    .toBeGreaterThan(20)
  await page.evaluate(() => window.novalistStores.settings.getState().load())

  // A fresh install replaces as you type, which is what the switch is turning
  // off - assert it is actually on first, or the test below proves nothing.
  await editor.click()
  await page.keyboard.type('ZZ--ZZ...ZZ', { delay: 30 })
  await expect.poll(async () => editor.innerText(), { timeout: 15_000 }).toContain('ZZ—ZZ…ZZ')

  await page.evaluate(async () => {
    await window.novalistStores.settings
      .getState()
      .update('global', { autoReplacementEnabled: false })
  })

  await editor.click()
  await page.keyboard.type('YY--YY...YY', { delay: 30 })
  await expect.poll(async () => editor.innerText(), { timeout: 15_000 }).toContain('YY--YY...YY')

  // And nothing offers to put them back: the two substitution rules are shown
  // greyed out rather than quietly dropped, so the reason is visible.
  await page.evaluate(() => window.novalistStores.shell.getState().setCleanupOpen(true))
  const dialog = page.locator('.cleanup-card')
  await expect(dialog).toBeVisible({ timeout: 20_000 })
  // Quotes, typography and the writer's own rules: the three that substitute.
  const boxes = dialog.locator('.cleanup-rules input[type="checkbox"]')
  await expect(boxes.nth(0)).toBeDisabled()
  await expect(boxes.nth(1)).toBeDisabled()
  await expect(boxes.nth(2)).toBeDisabled()
  await expect(boxes.nth(0)).not.toBeChecked()
  // The rules that only tidy whitespace and paragraphs are untouched.
  await expect(boxes.nth(3)).toBeEnabled()

  await app.close()
})
