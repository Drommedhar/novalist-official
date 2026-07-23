import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'

/**
 * Regression: the editor's selection-capture actions ("Add selection to entity",
 * "Create entity from selection") must act on the text that was selected when the
 * context menu opened. Clicking a menu row moves focus out of the contenteditable
 * and collapses the live selection, so reading it at click time silently did
 * nothing — the menu item looked dead.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('editor: "Add selection to entity" opens the picker with the selected text', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-capture-'))
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
  await page.evaluate(async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  await page.locator('.binder-scene-row').first().click()
  const frame = page.frameLocator('.editor-frame')
  const editor = frame.locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect.poll(async () => (await editor.innerText()).trim().length, { timeout: 15_000 })
    .toBeGreaterThan(20)

  // Select the first paragraph, then open the editor's context menu on it.
  const paragraph = editor.locator('p').first()
  await paragraph.selectText()
  const selected = (await frame.locator('#editor').evaluate(() =>
    (window.getSelection()?.toString() ?? '').trim()
  )).slice(0, 40)
  expect(selected.length).toBeGreaterThan(0)

  await paragraph.click({ button: 'right' })
  const menuItem = frame.locator('.cm-item[data-action="appendToEntitySection"]')
  await expect(menuItem).toBeVisible()
  await menuItem.click()

  // The host dialog opens, quoting the passage that was selected.
  const dialog = page.locator('.dialog-card', { hasText: /.*/ }).filter({
    has: page.locator('.capture-excerpt')
  })
  await expect(dialog).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.capture-excerpt')).toContainText(selected.slice(0, 20))

  // Cancel leaves the prose untouched.
  const beforeCancel = await editor.innerText()
  await page.locator('.dialog-button', { hasText: /^(Cancel|Abbrechen|取消)$/ }).click()
  await expect(page.locator('.capture-excerpt')).toHaveCount(0)
  expect(await editor.innerText()).toBe(beforeCancel)

  await app.close()
})
