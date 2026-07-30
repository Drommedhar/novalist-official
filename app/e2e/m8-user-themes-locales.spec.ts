import { test, expect, _electron as electron } from '@playwright/test'
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * Themes and interface languages dropped into folders under the settings root
 * must reach the app: the theme joins the Settings dropdown and repaints the
 * tokens when picked, and the language joins the language dropdown and
 * retranslates the interface. Both are scanned once at launch, which is exactly
 * what an Electron launch here exercises.
 */
test('folder themes and locales reach the Settings dropdowns', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-assets-'))
  const settingsDir = join(workDir, 'settings')
  mkdirSync(join(settingsDir, 'Themes'), { recursive: true })
  mkdirSync(join(settingsDir, 'Locales'), { recursive: true })

  // A token-map theme. Only the --nl-* tier is honoured, so --nv-ground must be
  // ignored while --nl-surface-window takes effect.
  writeFileSync(
    join(settingsDir, 'Themes', 'nord.json'),
    JSON.stringify({
      name: 'Nord',
      tokens: {
        '--nl-surface-window': '#2e3440',
        '--nl-accent': '#88c0d0',
        '--nv-ground': '#ff0000'
      }
    })
  )

  // A stylesheet theme, injected only while selected.
  writeFileSync(
    join(settingsDir, 'Themes', 'crimson.css'),
    ":root { --nl-accent: #b3001b; }\nbody { --nl-e2e-marker: injected; }"
  )

  // A partial translation: one label plus the native language name. Everything
  // it leaves out must fall back to English rather than showing raw keys.
  writeFileSync(
    join(settingsDir, 'Locales', 'eo.json'),
    JSON.stringify({
      language: { name: 'Esperanto', code: 'eo' },
      settings: { theme: 'Etoso' }
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

  // Settings live inside the shell, which only renders with a project open.
  await page.evaluate(async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Assets', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
  await expect(page.locator('#set-theme')).toBeVisible({ timeout: 15_000 })

  // Both folder themes joined the dropdown, after the built-in ones. The
  // built-ins are asserted by position rather than by count: pinning the count
  // meant a new shipped theme broke a test that is about folder themes.
  const themeOptions = await page.locator('#set-theme option').allTextContents()
  expect(themeOptions.slice(0, 2)).toEqual(['Default', 'Discord'])
  expect(themeOptions).toContain('Nord')
  expect(themeOptions).toContain('crimson')
  expect(themeOptions.indexOf('Nord')).toBeGreaterThan(themeOptions.indexOf('Discord'))

  // Picking the token theme repaints the tokens and pins its slug.
  await page.selectOption('#set-theme', 'Nord')
  await expect
    .poll(() => page.evaluate(() => document.documentElement.dataset.theme), { timeout: 10_000 })
    .toBe('user-nord')
  const nord = await page.evaluate(() => {
    const style = getComputedStyle(document.documentElement)
    return {
      window: style.getPropertyValue('--nl-surface-window').trim(),
      accent: style.getPropertyValue('--nl-accent').trim(),
      brand: style.getPropertyValue('--nv-ground').trim()
    }
  })
  expect(nord.window).toBe('#2e3440')
  expect(nord.accent).toBe('#88c0d0')
  // The brand layer is the corporate identity and is not overridable.
  expect(nord.brand).not.toBe('#ff0000')

  // The stylesheet theme's rules apply while it is selected...
  await page.selectOption('#set-theme', 'crimson')
  await expect
    .poll(
      () => page.evaluate(() => getComputedStyle(document.body).getPropertyValue('--nl-e2e-marker').trim()),
      { timeout: 10_000 }
    )
    .toBe('injected')

  // ...and are gone again once another theme is picked, so they cannot leak.
  await page.selectOption('#set-theme', 'Default')
  await expect
    .poll(
      () => page.evaluate(() => getComputedStyle(document.body).getPropertyValue('--nl-e2e-marker').trim()),
      { timeout: 10_000 }
    )
    .toBe('')

  // The dropped language lists under its native name and retranslates the UI.
  const languageOptions = await page.locator('#set-language option').allTextContents()
  expect(languageOptions).toContain('Esperanto')

  await page.selectOption('#set-language', 'eo')
  await expect(page.locator('label[for="set-theme"]')).toHaveText('Etoso', { timeout: 10_000 })
  // A key the partial translation omits falls back to English, not to a raw key.
  await expect(page.locator('label[for="set-language"]')).toHaveText('Interface Language')

  await app.close()
})
