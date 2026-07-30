import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * The read-only Wiki view must list Codex entities in its index and render a
 * browsable article (infobox / sections / appearances) for the selected entity,
 * and its "Edit in Codex" button must switch to the Codex view.
 */
const REAL = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('wiki view lists entities and opens an article', async () => {
  test.skip(!existsSync(join(REAL, '.novalist')), 'real project not available')
  test.setTimeout(120_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-wiki-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, REAL)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('wiki'))

  // The index lists at least one entity, and the article pane renders a title.
  await expect(page.locator('.wiki-entry').first()).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.wiki-article-heading h1')).toBeVisible({ timeout: 15_000 })

  // Selecting an index entry opens its article with the encyclopedic lead line.
  await page.locator('.wiki-entry').first().click()
  await expect(page.locator('.wiki-article')).toBeVisible()
  await expect(page.locator('.wiki-lead-line')).toBeVisible()

  // Clicking an infobox image opens it full-size in a lightbox; Escape closes it.
  const imageBtn = page.locator('.wiki-infobox .wiki-image-btn').first()
  if (await imageBtn.count()) {
    await imageBtn.click()
    await expect(page.locator('.wiki-lightbox img')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.locator('.wiki-lightbox')).toHaveCount(0)
  }

  // "Edit in Codex" jumps to the Codex view.
  await page.locator('.wiki-edit-btn').click()
  await expect
    .poll(async () => page.evaluate(() => window.novalistStores.shell.getState().mainView), {
      timeout: 10_000
    })
    .toBe('codex')

  await app.close()
})
