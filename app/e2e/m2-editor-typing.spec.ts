import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * Regression: typing must not reset the caret to the start of the scene, and
 * undo must work. The bug was a store→editor echo loop that re-pushed the whole
 * HTML on every keystroke, inserting each character at position 0 (so a typed
 * run appeared reversed at the top) and wiping the native undo stack.
 */

test('editor: typing keeps the caret and undo works', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-type-'))
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
  await expect.poll(async () => (await editor.innerText()).trim().length, { timeout: 15_000 }).toBeGreaterThan(20)

  // Type a distinctive run character-by-character. If the caret reset each
  // keystroke, the characters would land at position 0 in reverse order.
  await editor.click()
  const before = await editor.innerText()
  const marker = 'QWERTYUIOP'
  await page.keyboard.type(marker, { delay: 25 })

  const typed = await editor.innerText()
  // The run appears IN ORDER (caret held) and the doc grew by its length.
  expect(typed).toContain(marker)
  expect(typed).not.toContain('POIUYTREWQ')
  expect(typed.length).toBeGreaterThan(before.length)

  // Undo must actually change the document (it was completely dead before).
  await page.keyboard.press('ControlOrMeta+Z')
  await expect.poll(async () => (await editor.innerText()) !== typed, { timeout: 5_000 }).toBe(true)

  await app.close()
})
