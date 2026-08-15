import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

test('Help opens at the current context and preserves heading navigation', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-help-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([key, value]) => value !== undefined && key !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'electron-profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.start-screen')).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () =>
      page.evaluate(async () => {
        try {
          await window.novalistRpc.request('system/ping')
          return true
        } catch {
          return false
        }
      })
    )
    .toBe(true)

  // Keep this test about Help: a completed local onboarding record prevents a
  // first-run card from changing views underneath it.
  await evaluateWhenReady(page, () => {
    localStorage.setItem(
      'nl.onboarding',
      JSON.stringify({ version: 1, tour: 'completed', tipsEnabled: true, tips: {} })
    )
  })
  await page.reload()
  await expect(page.locator('.start-screen')).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () =>
      page.evaluate(async () => {
        try {
          await window.novalistRpc.request('system/ping')
          return true
        } catch {
          return false
        }
      })
    )
    .toBe(true)

  await page.evaluate(async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Help', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  const dashboardButton = page.getByRole('button', { name: 'Dashboard', exact: true }).first()
  await dashboardButton.focus()
  await page.evaluate(() => {
    const shell = window.novalistStores.shell.getState()
    shell.setInspectorTab('footnotes')
    shell.setMainView('write')
    shell.setHelpOpen(true)
  })

  const dialog = page.getByRole('dialog', { name: 'Novalist Manual' })
  await expect(dialog).toBeVisible()
  await expect(dialog.locator('.help-search')).toBeFocused()

  // The active writing/Inspector context deep-links to the relevant section,
  // while initial keyboard focus remains in Search.
  const footnotes = dialog.locator('#footnotes-and-comments-footnotes-tab')
  await expect(footnotes).toBeInViewport()
  await expect(dialog.locator('.help-search')).toBeFocused()

  // Search returns heading-level destinations with snippets rather than only a
  // page filter. Choosing one moves both scroll position and keyboard focus.
  await dialog.locator('.help-search').fill('Focus mode')
  const focusModeResult = dialog
    .locator('.help-page-item', {
      has: page.locator('.help-result-heading', { hasText: /^Focus mode$/ }),
      hasText: 'Editor'
    })
    .first()
  await expect(focusModeResult).toBeVisible()
  await focusModeResult.click()
  await expect(dialog.locator('#focus-mode')).toBeInViewport()
  await expect(dialog.locator('#focus-mode')).toBeFocused()

  // Anchor-only links stay on the current page and retain their fragment.
  await dialog.getByRole('link', { name: 'Readability marking' }).first().click()
  await expect(dialog.locator('#readability-marking')).toBeInViewport()
  await expect(dialog.locator('#readability-marking')).toBeFocused()

  await page.keyboard.press('Escape')
  await expect(dialog).toBeHidden()
  await expect(dashboardButton).toBeFocused()

  await app.close()
})
