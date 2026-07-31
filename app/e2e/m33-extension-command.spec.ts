import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdirSync, mkdtempSync, writeFileSync, copyFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'

/**
 * A command an extension registered, found and run from the command palette.
 *
 * The registry had been filling up since the SDK shipped and nothing read it:
 * no RPC listed the commands and no surface offered them, so an extension
 * author could call RegisterCommand, see it succeed, and ship something the
 * writer had no way to reach. The unit tests were green throughout, because
 * every one of them called the registry directly.
 *
 * So this asserts the whole route: the palette lists what the backend has, and
 * choosing one actually runs the extension's handler - which the status bar
 * then shows, because a command that runs and changes nothing is the same
 * failure wearing a different hat.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('an extension command is listed in the palette and runs', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-extcmd-'))
  const projectCopy = join(workDir, 'project')
  copyProject(REAL_PROJECT, projectCopy)

  const settingsDir = join(workDir, 'settings')
  const extensionDir = join(settingsDir, 'Extensions', 'Toolkit')
  mkdirSync(extensionDir, { recursive: true })

  const candidates = [
    join(process.cwd(), '..', 'Novalist.Sdk.Example', 'bin', 'Debug', 'net8.0', 'Novalist.Sdk.Example.dll'),
    join(process.cwd(), 'out', 'backend', 'Novalist.Sdk.Example.dll')
  ]
  const assembly = candidates.find((c) => existsSync(c)) ?? candidates[0]
  test.skip(!existsSync(assembly), 'sample extension assembly not built')
  copyFileSync(assembly, join(extensionDir, 'Novalist.Sdk.Example.dll'))

  // Its locales too. Without them the command's title falls back to the raw key
  // and the search below would match that instead - passing while the writer
  // reads "command.pomodoro.title" in the palette.
  const locales = join(assembly, '..', 'Locales')
  mkdirSync(join(extensionDir, 'Locales'), { recursive: true })
  for (const file of ['en.json', 'de.json']) {
    if (existsSync(join(locales, file))) {
      copyFileSync(join(locales, file), join(extensionDir, 'Locales', file))
    }
  }

  writeFileSync(
    join(extensionDir, 'extension.json'),
    JSON.stringify({
      id: 'com.novalist.writingtoolkit',
      name: 'Writing Toolkit',
      version: '1.0.0',
      entryAssembly: 'Novalist.Sdk.Example.dll'
    })
  )

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = settingsDir
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  // Extensions load when the writer opens the Extensions view.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('extensions'))
  await page.waitForTimeout(3000)

  // The backend half first, so a failure below says which of the two broke.
  const listed = await page.evaluate(async () =>
    JSON.stringify(await window.novalistRpc.request('extensions/commands'))
  )
  expect(listed).toContain('ext.writingtoolkit.pomodoro.toggle')
  expect(listed).toContain('ext.writingtoolkit.countword')

  // Idle, before anything runs it.
  const pomodoro = page.locator('.status-ext-item', { hasText: '--:--' })
  await expect(pomodoro).toBeVisible({ timeout: 30_000 })

  // Now through the palette, the way a writer reaches it.
  await page.evaluate(() => window.novalistStores.shell.getState().setCommandPaletteOpen(true))
  const search = page.locator('.palette-card .dialog-input')
  await expect(search).toBeVisible({ timeout: 15_000 })
  await search.fill('pomodoro')

  const entry = page.locator('.palette-item', { hasText: 'omodoro' }).first()
  await expect(entry).toBeVisible({ timeout: 15_000 })
  // Listed despite carrying a schema: its argument is optional, and treating
  // any schema as a reason to hide the command emptied the palette of whole
  // extensions - nearly every one declares a flag a script may pass.
  await expect(page.locator('.palette-item')).toHaveCount(1)
  // The extension's own words, in whichever language the app is running -
  // not the key they are looked up by.
  await expect(entry).not.toContainText('command.pomodoro')
  await entry.click()

  // The extension's handler ran: the timer is counting rather than idle.
  await expect(page.locator('.status-ext-item', { hasText: '--:--' })).toHaveCount(0, {
    timeout: 30_000
  })

  // And the one that cannot run without an argument is not offered, because a
  // palette entry that fails when clicked is worse than one that is absent.
  await page.evaluate(() => window.novalistStores.shell.getState().setCommandPaletteOpen(true))
  await page.locator('.palette-card .dialog-input').fill('ount')
  await expect(page.locator('.palette-item', { hasText: 'ount a word' })).toHaveCount(0)

  await app.close()
})
