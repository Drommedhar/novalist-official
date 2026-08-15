import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * A starting point for the entity types a worldbuilder ends up needing.
 *
 * The custom type builder is an empty form, so everybody who wants species, a
 * magic system, factions or a language rebuilds the same field list by hand -
 * and rebuilds it differently in every project.
 */
test('a type pack fills the builder, and creates nothing until it is saved', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-packs-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Packs', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.locator('.codex-tab-manage').click()

  const packs = page.locator('.type-pack')
  await expect.poll(() => packs.count(), { timeout: 15_000 }).toBeGreaterThan(3)

  // Listing them creates nothing.
  expect(
    await page.evaluate(() =>
      window.novalistRpc.request<unknown[]>('entities/customTypes').then((t) => t.length)
    )
  ).toBe(0)

  await packs.filter({ hasText: /Magic system|Magiesystem|魔法/ }).first().click()

  // The form is filled in and still the writer's to change: the field that
  // keeps an ending earned is in there, with the question it is for.
  const card = page.locator('.type-manager-card')
  await expect(card.locator('input.dialog-input').first()).toHaveValue(/Magic system|Magiesystem|魔法/)
  await expect(card.locator('.type-manager-prompt').first()).not.toHaveValue('')
  await expect(card.locator('input[value="Limits"]')).toHaveCount(1)

  // Still nothing created until it is saved.
  expect(
    await page.evaluate(() =>
      window.novalistRpc.request<unknown[]>('entities/customTypes').then((t) => t.length)
    )
  ).toBe(0)

  await card.locator('.dialog-actions .dialog-button.primary').click()
  await expect
    .poll(
      () =>
        page.evaluate(() =>
          window.novalistRpc
            .request<{ defaultFields: { displayName: string; prompt: string }[] }[]>(
              'entities/customTypes'
            )
            .then((types) => types.flatMap((ty) => ty.defaultFields).filter((f) => f.prompt).length)
        ),
      { timeout: 15_000 }
    )
    .toBeGreaterThan(3)

  await app.close()
})
