import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { enterWriting } from './harness'

/**
 * Offline spell check reaching the prose surface.
 *
 * The setting has to travel backend -> settings store -> editor iframe, and the
 * surface it lands on used to be hardcoded to spellcheck="false". Asserting the
 * attribute in the real iframe is the only thing that proves the chain, which
 * unit tests on either end cannot.
 */

test('the spell-check setting reaches the prose surface', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-spell-'))

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (dir) => {
    const state = await window.novalistRpc.request('project/create', [dir, 'Spell Novel', 'Book'])
    window.novalistStores.project.getState().applyState(state as never)
    const store = window.novalistStores.project.getState()
    await store.createChapter('Chapter One')
    const guid = window.novalistStores.project.getState().chapters[0].guid
    await window.novalistStores.project.getState().createScene(guid, 'Scene One')
  }, workDir)

  // A created project opens on the Dashboard; the binder and editor are Write's.
  await enterWriting(page)

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })

  // On by default: an offline-first app spell-checks out of the box.
  await expect
    .poll(() => editor.evaluate((el) => (el as HTMLElement).spellcheck), { timeout: 15_000 })
    .toBe(true)

  // The prose surface is tagged with the writing language, not the UI language,
  // so the platform picks the right dictionary.
  await page.evaluate(async () => {
    await window.novalistRpc.request('settings/updateGlobal', [{ autoReplacementLanguage: 'de' }])
    await window.novalistStores.settings.getState().load()
  })
  await expect
    .poll(() => editor.evaluate((el) => (el as HTMLElement).lang), { timeout: 15_000 })
    .toBe('de')

  // Turning it off actually stops the underlines rather than only the setting.
  await page.evaluate(async () => {
    await window.novalistRpc.request('settings/updateGlobal', [{ spellCheckEnabled: false }])
    await window.novalistStores.settings.getState().load()
  })
  await expect
    .poll(() => editor.evaluate((el) => (el as HTMLElement).spellcheck), { timeout: 15_000 })
    .toBe(false)

  // A word taught from the spelling menu is stored with the writer's settings,
  // so it survives a reinstall instead of living only in the OS dictionary.
  const words = await page.evaluate(async () => {
    await window.novalistRpc.request('spell/addWord', ['Aelthorn'])
    return (await window.novalistRpc.request('spell/words')) as string[]
  })
  expect(words).toEqual(['Aelthorn'])

  await app.close()
})
