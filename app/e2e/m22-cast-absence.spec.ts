import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * Who drops out of the book, on the Dashboard.
 *
 * Appearances per chapter have been counted for a long time and only ever drawn
 * as a grid, with "last seen N chapters ago" for one entry at a time in the
 * Inspector. Nothing read the grid for the answer a revision wants.
 */
test('the dashboard names the character who disappears and the chapters they miss', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-absence-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [parent, 'Absence', 'Book One'])
    const mira = (await rpc.request('entities/create', ['character', 'Mira'])) as { id: string }

    // Present in the first and the last of five chapters: the gap is the
    // middle three, and nothing is owing at the end.
    for (const title of ['One', 'Two', 'Three', 'Four', 'Five']) {
      state = await rpc.request('project/createChapter', [title])
      const chapters = (state as { chapters: { guid: string; title: string }[] }).chapters
      const guid = chapters[chapters.length - 1].guid
      state = await rpc.request('project/createScene', [guid, 'S'])
      const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
        .find((c) => c.guid === guid)!.scenes
      const scene = scenes[scenes.length - 1]
      const html =
        title === 'One' || title === 'Five'
          ? `<p><span class="nv-entity-mention" data-entity-id="${mira.id}">Mira</span> is here.</p>`
          : '<p>Nobody in particular.</p>'
      await rpc.request('scenes/write', [guid, scene.id, html, 'text'])
    }
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))

  const row = page.locator('.cast-absence-row')
  await expect(row).toHaveCount(1, { timeout: 20_000 })
  await expect(row.locator('.cast-absence-name')).toHaveText('Mira')
  // The chapters are named, not numbered: a row saying "index 1 to 3" is a row
  // somebody has to go and count against.
  await expect(row.locator('.cast-absence-gap')).toContainText('Two')
  await expect(row.locator('.cast-absence-gap')).toContainText('Four')
  await expect(row.locator('.cast-absence-gap')).toContainText('3')

  // She comes back in the last chapter, so there is nothing owing at the end.
  await expect(row.locator('.cast-absence-since')).toHaveCount(0)

  await app.close()
})
