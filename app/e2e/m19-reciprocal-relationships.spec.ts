import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * A tie authored on a place is authored on the other end too.
 *
 * The backend stopped being character-only some time ago - entities/setRelationships
 * takes the entry's type and writes the inverse onto whatever the row names.
 * The renderer never passed the type, so it defaulted to "character", and saving
 * a relationship on a location looked for a character with that id and threw.
 * Every non-character tie was lost at the moment it was typed.
 */
test('a relationship saved on a location writes the inverse on the character', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-reciprocal-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (parent) => {
    const state = await window.novalistRpc.request('project/create', [parent, 'Ties', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  const locationId = await page.evaluate(async () => {
    const rpc = window.novalistRpc
    await rpc.request('entities/create', ['character', 'Mira'])
    const place = (await rpc.request('entities/create', ['location', 'The Foundry'])) as {
      id: string
    }
    return place.id
  })

  // Through the Codex, not the RPC: the RPC was already right, and going round
  // the renderer is exactly how this shipped broken.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.evaluate(() => window.novalistStores.codex.getState().setType('location'))
  // The list still holds the previous type for a tick, and clicking then leaves
  // the editor pointed at a character id while the type says location - which
  // the backend rightly refuses.
  await expect(page.locator('.codex-row')).toHaveCount(1, { timeout: 15_000 })
  await page.locator('.codex-row', { hasText: 'The Foundry' }).click()
  await expect
    .poll(
      () =>
        page.evaluate(() => {
          const s = window.novalistStores.codex.getState()
          return `${s.entityType}:${s.selectedId ?? ''}`
        }),
      { timeout: 15_000 }
    )
    .toBe(`location:${locationId}`)

  // A new entry has no rows yet; the editor adds one. Matched by class, because
  // the real project runs in German and the label is a translated string.
  const add = page.locator('.codex-add-relationship')
  await expect(add).toBeVisible({ timeout: 15_000 })
  await add.click()

  const row = page.locator('.entity-rel-row').first()
  await expect(row).toBeVisible({ timeout: 15_000 })
  await row.locator('input').first().fill('owned by')
  await row.locator('input').nth(1).fill('Mira')
  await row.locator('.codex-rel-inverse').fill('owns')
  // A real focus move rather than element.blur(), which raced the input event.
  await page.keyboard.press('Tab')

  // The place keeps its own row...
  await expect
    .poll(
      async () =>
        page.evaluate(async (id) => {
          const place = (await window.novalistRpc.request('entities/get', ['location', id])) as {
            relationships: { role: string; target: string }[]
          }
          return place.relationships.map((r) => `${r.role}->${r.target}`)
        }, locationId),
      { timeout: 15_000 }
    )
    .toContain('owned by->Mira')

  // ...and Mira gains the other half, which is the part that was being lost.
  await expect
    .poll(
      async () =>
        page.evaluate(async () => {
          const list = (await window.novalistRpc.request('entities/list', ['character'])) as {
            id: string
            name: string
          }[]
          const mira = list.find((c) => c.name.includes('Mira'))!
          const full = (await window.novalistRpc.request('entities/get', [
            'character',
            mira.id
          ])) as { relationships: { role: string; target: string }[] }
          return full.relationships.map((r) => `${r.role}->${r.target}`)
        }),
      { timeout: 15_000 }
    )
    .toContain('owns->The Foundry')

  await app.close()
})
