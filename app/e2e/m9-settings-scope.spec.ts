import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * The per-section "Override for this project" switch must reflect what is
 * stored with the project, not what was last clicked in this session. It used
 * to be local component state seeded to off, so it read as off on every fresh
 * visit while the project's overrides were still in force — and an edit then
 * silently went to the global defaults.
 *
 * This is renderer wiring over a backend that was already correct and fully
 * unit-tested, which is precisely the gap a C# coverage gate cannot see, so the
 * assertion belongs here.
 */
test('the project-override switch survives leaving and re-entering Settings', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-scope-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Scoped', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  const openSettings = async (): Promise<void> => {
    await page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
    await expect(page.locator('#set-theme')).toBeVisible({ timeout: 15_000 })
  }
  // The Appearance card is the first section, so its switch is the first one.
  const appearanceSwitch = page.locator('.settings-scope input[type="checkbox"]').first()

  await openSettings()
  await expect(appearanceSwitch).not.toBeChecked()
  await expect(page.locator('.settings-scope-hint').first()).toHaveText(
    'Editing your global defaults. This project follows them.'
  )

  // A single click each way: the switch is controlled by what the backend
  // reports, so it stays unchecked until the round-trip lands. check()/uncheck()
  // would re-click while that is in flight and toggle it straight back.
  // Ticking pins the current values to the project straight away — no field edit.
  await appearanceSwitch.click()
  await expect(appearanceSwitch).toBeChecked({ timeout: 10_000 })
  await expect(page.locator('.settings-scope-hint').first()).toHaveText(
    'Editing this project only. Your global defaults are unchanged.'
  )

  // Leave Settings and come back: the switch must still read as overridden.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await openSettings()
  await expect(appearanceSwitch).toBeChecked()

  // Pinning copied the value in effect into the project, without an edit.
  const globalTheme = await page.evaluate(
    () => window.novalistStores.settings.getState().view?.global?.theme ?? null
  )
  await expect
    .poll(
      () =>
        page.evaluate(
          () => window.novalistStores.settings.getState().view?.overrides?.theme ?? null
        ),
      { timeout: 10_000 }
    )
    .toBe(globalTheme)

  // With the override on, an edit is written to the project, not to the globals.
  await page.selectOption('#set-theme', 'Discord')
  await expect
    .poll(
      () =>
        page.evaluate(
          () => window.novalistStores.settings.getState().view?.overrides?.theme ?? null
        ),
      { timeout: 10_000 }
    )
    .toBe('Discord')
  expect(
    await page.evaluate(
      () => window.novalistStores.settings.getState().view?.global?.theme ?? null
    )
  ).toBe(globalTheme)

  // Unticking drops the override and the section falls back to the globals.
  await appearanceSwitch.click()
  await expect(appearanceSwitch).not.toBeChecked({ timeout: 10_000 })
  await expect
    .poll(
      () =>
        page.evaluate(() => {
          const v = window.novalistStores.settings.getState().view
          return { override: v?.overrides?.theme ?? null, effective: v?.effective.theme ?? null }
        }),
      { timeout: 10_000 }
    )
    .toEqual({ override: null, effective: globalTheme })

  // ...and that survives a re-entry too, rather than reappearing.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await openSettings()
  await expect(appearanceSwitch).not.toBeChecked()

  await app.close()
})
