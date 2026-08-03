import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * A cleanup pass over prose that is already written.
 *
 * Auto-replacements fire while typing and skip pasted text on purpose, so a
 * chapter written elsewhere and pasted in keeps its straight quotes and its
 * double spaces for good. The preview is the part that matters here: a pass
 * that rewrites every scene in a book must be reportable before it runs.
 */

test('the cleanup pass reports what it would change before changing it', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-cleanup-'))
  const projectCopy = join(workDir, 'project')
  copyProject(REAL_PROJECT, projectCopy)
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
  }, projectCopy)

  // Put a scene in the state a paste leaves it in: straight quotes, a hyphen
  // pair, three dots, doubled spaces and a paragraph holding nothing.
  const target = await page.evaluate(async () => {
    const project = window.novalistStores.project.getState()
    const chapter = project.chapters[0]
    const scene = chapter.scenes[0]
    await window.novalistRpc.request('scenes/write', [
      chapter.guid,
      scene.id,
      '<p>  "He left--again..."  </p><p></p><p>Then  she did.</p>',
      'He left again. Then she did.'
    ])
    return { chapterGuid: chapter.guid, sceneId: scene.id, sceneTitle: scene.title }
  })

  await page.evaluate(() => window.novalistStores.shell.getState().setCleanupOpen(true))
  const dialog = page.locator('.cleanup-card')
  await expect(dialog).toBeVisible({ timeout: 20_000 })

  // Preview first. It names the scene it would rewrite and leaves it alone.
  await dialog.getByRole('button').first().click()
  await expect(dialog.locator('.cleanup-titles li').first()).toBeVisible({ timeout: 20_000 })

  const untouched = await page.evaluate(
    async (t) =>
      ((await window.novalistRpc.request('scenes/read', [t.chapterGuid, t.sceneId])) as {
        html: string
      }).html,
    target
  )
  expect(untouched).toContain('"He left--again..."')

  // Then run it, and the prose comes out with real glyphs and no stray spaces.
  await dialog.locator('.dialog-button.primary').click()
  await expect(dialog.locator('.cleanup-report')).toBeVisible({ timeout: 20_000 })

  // Which quote pair lands depends on the book's writing language - a German
  // manuscript gets low-9 quotes - so this asserts the rules, not one locale.
  await expect
    .poll(
      async () =>
        page.evaluate(
          async (t) =>
            ((await window.novalistRpc.request('scenes/read', [t.chapterGuid, t.sceneId])) as {
              html: string
            }).html,
          target
        ),
      { timeout: 20_000 }
    )
    .not.toContain('--')

  const finalHtml = await page.evaluate(
    async (t) =>
      ((await window.novalistRpc.request('scenes/read', [t.chapterGuid, t.sceneId])) as {
        html: string
      }).html,
    target
  )
  expect(finalHtml).toContain('…')
  expect(finalHtml).not.toContain('"')
  expect(finalHtml).not.toContain('Then  she')
  expect(finalHtml).not.toContain('<p></p>')

  await app.close()
})
