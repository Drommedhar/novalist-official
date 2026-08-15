import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { enterWriting } from './harness'

/**
 * Regression: Command-key hotkeys pressed while the caret is in the editor.
 *
 * editor.html forwards keystrokes to the shell only for combinations it does not
 * handle itself, and that test ignored metaKey entirely - so every Cmd shortcut
 * died inside the editor. On macOS the native menu's accelerators masked it; an
 * iPad with a hardware keyboard has no menu, so nothing fired at all.
 *
 * The paired assertion matters just as much: Cmd+B must NOT reach the shell,
 * because Ctrl+B is bound to "toggle binder". Forwarding it would take bold away
 * from every macOS and iPad writer.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('editor: Cmd hotkeys reach the shell, Cmd+B stays native bold', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-hotkey-'))
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

  // An opened project lands on the Dashboard; the binder and editor are Write's.
  await enterWriting(page)

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () => (await editor.innerText()).trim().length, { timeout: 15_000 })
    .toBeGreaterThan(20)

  // Caret genuinely inside the editor iframe - the case that used to swallow
  // every Cmd shortcut.
  await editor.click()

  // Quick Open is Ctrl+P, and the shell treats Cmd as Ctrl. Assert on the store
  // rather than a selector: the dialog shares its classes with the command
  // palette and its accessible name is localized.
  const quickOpenOpen = (): Promise<boolean> =>
    page.evaluate(() => window.novalistStores.shell.getState().quickOpenOpen)

  await page.keyboard.press('Meta+p')
  await expect.poll(quickOpenOpen, { timeout: 10_000 }).toBe(true)
  await page.keyboard.press('Escape')
  await expect.poll(quickOpenOpen, { timeout: 10_000 }).toBe(false)

  // Cmd+B must be left to the writing surface. If it were forwarded it would hit
  // the Ctrl+B binding and toggle the binder, so assert the binder is unmoved.
  const binderVisibleBefore = await page.evaluate(
    () => window.novalistStores.shell.getState().binderVisible
  )
  await editor.click()
  await page.keyboard.press('Meta+b')
  await page.waitForTimeout(500)
  const binderVisibleAfter = await page.evaluate(
    () => window.novalistStores.shell.getState().binderVisible
  )
  expect(binderVisibleAfter).toBe(binderVisibleBefore)

  await app.close()
})
