import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * A field the writer defines has to reach the surfaces that use it.
 *
 * The backend can be perfect and the feature still absent: a definition that
 * no view reads is a column nobody has, which is exactly how a settings switch
 * once shipped wired to nothing.
 */
test('a scene field defined in Settings becomes an editable outliner column', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-fields-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const sceneId = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [parent, 'Fields', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    state = await rpc.request('project/createScene', [guid, 'Opening'])
    window.novalistStores.project.getState().applyState(state as never)
    const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
      .find((c) => c.guid === guid)!.scenes
    return scenes[scenes.length - 1].id
  }, workDir)

  // ── Define a field in Settings, the way a writer would ──
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
  const card = page.locator('.dashboard-card', { hasText: 'Scene and chapter fields' })
  await expect(card).toBeVisible({ timeout: 20_000 })

  await card.getByRole('button', { name: 'Add a field' }).click()
  const row = card.locator('.props-row').first()
  await row.locator('input').first().fill('Tension')
  await row.locator('select').nth(1).selectOption('Int')
  await row.getByRole('checkbox').check()
  await card.getByRole('button', { name: 'Save' }).click()

  // ── It becomes a column in the outliner, and typing in it sticks ──
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
  await page.evaluate(() => window.novalistStores.manuscript?.getState?.().setMode?.('outliner'))
  const modeButton = page.getByRole('button', { name: 'Outliner' })
  if (await modeButton.count()) await modeButton.first().click()

  await expect(page.locator('.outliner-head')).toContainText('Tension', { timeout: 20_000 })
  const cell = page.locator('.outliner-row:not(.outliner-head) input[aria-label="Tension"]')
  await expect(cell).toHaveCount(1)
  await cell.fill('8')
  await cell.blur()

  // Round-trips through the backend rather than only living in the input. The
  // key is generated and never shown, so it is read back rather than guessed.
  await expect
    .poll(
      async () =>
        (await page.evaluate(async (id) => {
          const defs = (await window.novalistRpc.request(
            'manuscriptProps/definitions'
          )) as { key: string }[]
          const all = (await window.novalistRpc.request('manuscriptProps/allSceneValues')) as Record<
            string,
            Record<string, string>
          >
          return all[id]?.[defs[0]?.key] ?? null
        }, sceneId)) as string | null,
      { timeout: 10_000 }
    )
    .toBe('8')

  await app.close()
})
