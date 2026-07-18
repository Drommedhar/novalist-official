import { test, expect, _electron as electron } from '@playwright/test'
import { execFileSync } from 'node:child_process'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * Opens a copy of a real Novalist project (119 scenes, German) and verifies
 * the binder and editor render real content. The original is never touched;
 * the app works on a temp copy.
 */

const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('real project renders binder and scene content', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(180_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-real-'))
  const projectCopy = join(workDir, 'project')
  execFileSync('rsync', [
    '-a',
    '--exclude', '.git',
    '--exclude', '.obsidian',
    '--exclude', '.claude',
    `${REAL_PROJECT}/`,
    projectCopy
  ])

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend')).toContainText('(', { timeout: 30_000 })

  await page.evaluate(async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  // Real chapters and scenes appear in the binder.
  const chapterRows = page.locator('.binder-chapter-row')
  await expect.poll(() => chapterRows.count()).toBeGreaterThan(3)
  const sceneRows = page.locator('.binder-scene-row')
  await expect.poll(() => sceneRows.count()).toBeGreaterThan(10)

  // Open the first scene; the real editor shows real prose.
  await sceneRows.first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () => ((await editor.innerText()) ?? '').trim().length, { timeout: 15_000 })
    .toBeGreaterThan(50)

  // Inspector: write a synopsis, blur, and confirm it persisted over RPC.
  const synopsis = page.locator('#inspector-synopsis')
  await synopsis.fill('Verification synopsis from e2e')
  await synopsis.blur()
  await expect
    .poll(async () => {
      return page.evaluate(async () => {
        const store = window.novalistStores.project.getState()
        const meta = (await window.novalistRpc.request('scenes/getMeta', [
          store.openChapterGuid,
          store.openSceneId
        ])) as { synopsis: string | null }
        return meta.synopsis
      })
    })
    .toBe('Verification synopsis from e2e')

  // Codex: real characters render in the list.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await expect.poll(() => page.locator('.codex-row').count(), { timeout: 15_000 }).toBeGreaterThan(0)

  // Dashboard: real totals appear.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await expect(page.locator('.dashboard-title')).toBeVisible({ timeout: 15_000 })
  const wordsMetric = await page.locator('.dashboard-metric-value').first().innerText()
  expect(Number(wordsMetric.replace(/[^0-9]/g, ''))).toBeGreaterThan(1000)

  // Manuscript: corkboard cards and outliner rows render from real scenes.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
  await expect(page.locator('.editor-frame')).toBeVisible({ timeout: 15_000 })
  await page.locator('.manuscript-modes button').nth(1).click()
  await expect.poll(() => page.locator('.corkboard-card').count(), { timeout: 15_000 }).toBeGreaterThan(5)
  await page.locator('.manuscript-modes button').nth(2).click()
  await expect.poll(() => page.locator('.outliner-row').count(), { timeout: 15_000 }).toBeGreaterThan(5)

  await app.close()
})
