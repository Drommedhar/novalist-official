import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * Names to pick from, where the name is typed.
 *
 * Naming is the highest-frequency thing that stops a draft. Every worldbuilding
 * tool in the field gives a generator away and Novalist had none - no lists, no
 * patterns, no assistance past aliases.
 */
test('the codex create dialog suggests names, and picking one fills the field', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-names-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Names', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.locator('.codex-list .binder-rail-item').click()

  const panel = page.locator('.name-suggestions')
  await expect(panel).toBeVisible({ timeout: 15_000 })
  await panel.locator('summary').click()

  // The sets come from the backend rather than a copy in the renderer, so the
  // picker cannot offer one that does not ship.
  await expect
    .poll(() => panel.locator('select option').count(), { timeout: 15_000 })
    .toBeGreaterThan(1)

  await panel.locator('.dialog-button').click()
  const suggestions = panel.locator('.name-suggestion')
  await expect.poll(() => suggestions.count(), { timeout: 15_000 }).toBeGreaterThan(1)

  const picked = (await suggestions.first().innerText()).trim()
  expect(picked.length).toBeGreaterThan(1)
  await suggestions.first().click()

  // Picking one fills the field it was offered beside, rather than making the
  // entry itself - the writer still gets to change it.
  await expect(page.locator('#codex-create-name')).toHaveValue(picked)

  // Deterministic: the same seed gives the same list back, so a name somebody
  // liked and did not write down is not gone.
  const twice = await page.evaluate(async () => {
    const rpc = window.novalistRpc
    const sets = (await rpc.request<string[]>('names/sets'))
    const a = await rpc.request<string[]>('names/generate', [sets[0], 6, 50, 99])
    const b = await rpc.request<string[]>('names/generate', [sets[0], 6, 50, 99])
    return { a, b }
  })
  expect(twice.a).toEqual(twice.b)
  expect(twice.a).toHaveLength(6)

  await app.close()
})
