import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdirSync, mkdtempSync, writeFileSync, copyFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'

/**
 * Writing one section of a Codex entry, and asking for another go at it.
 *
 * The only AI over Codex data was one whole-entity Wiki summary, regenerated
 * all or nothing - the wrong unit for how an entry is actually filled in, where
 * the history is fine and the appearance needs another attempt.
 *
 * What no unit test reaches is the half that decides whether this exists for
 * the writer: that the button is on the section head at all, that it is absent
 * when no extension can generate, and that what comes back lands in the section
 * they pressed it on rather than the first one.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('a Codex section can be written and re-rolled from its own head', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-section-'))
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

  // Before the extension loads there is no generator, so no button - otherwise
  // the writer clicks something that can never answer.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.waitForTimeout(1500)
  const firstEntry = page.locator('.codex-list .codex-row').first()
  await expect(firstEntry).toBeVisible({ timeout: 30_000 })
  await firstEntry.click()
  await page.waitForTimeout(1200)
  const addSection = page.locator('button', { hasText: /Abschnitt hinzuf|Add Section/ }).last()
  await expect(addSection).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.entity-section-head button[title]')).toHaveCount(0)

  // Now with one loaded.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('extensions'))
  await page.waitForTimeout(3000)
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.waitForTimeout(1200)
  await firstEntry.click()
  await page.waitForTimeout(1200)

  // Two sections, so "it landed in the one I pressed" is a real assertion
  // rather than a coincidence of there being only one.
  await addSection.click()
  await page.waitForTimeout(400)
  await addSection.click()
  await page.waitForTimeout(600)
  const titles = page.locator('.entity-section-title')
  const count = await titles.count()
  expect(count).toBeGreaterThanOrEqual(2)
  await titles.nth(count - 2).fill('Backstory')
  await titles.nth(count - 1).fill('Appearance')
  // Blur, so the titles are what the entry holds rather than what the inputs
  // show - the generate call reads the saved section.
  await page.keyboard.press('Tab')
  await page.waitForTimeout(800)

  const generate = page.locator('.entity-section-head button[title]')
  await expect(generate.nth(count - 1)).toBeVisible({ timeout: 15_000 })
  await generate.nth(count - 1).click()

  // The section it was pressed on carries its own heading back.
  const sections = page.locator('.entity-section')
  await expect(sections.nth(count - 1)).toContainText('On Appearance', { timeout: 30_000 })
  await expect(sections.nth(count - 2)).not.toContainText('On Appearance')

  // And a second press is a re-roll: the generator was told what was there.
  await generate.nth(count - 1).click()
  await expect(sections.nth(count - 1)).toContainText('On Appearance (again)', { timeout: 30_000 })

  await app.close()
})
