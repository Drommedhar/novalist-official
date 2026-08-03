import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * Deciding against a note, and having it still say so.
 *
 * Resolving a comment said only that it was finished with, so a remark acted on
 * and a remark disagreed with came out identical. That is the wrong shape for
 * feedback: most of a beta reader's notes are opinions to weigh, and the ones
 * turned down are exactly the ones worth recognising six weeks later when a
 * second reader says the same thing.
 *
 * What no unit test reaches: that the three verdicts are on the note in the
 * Inbox, that choosing one shows on the note, and that weighing a note leaves
 * it in the list while declining takes it out.
 */

test('a note can be weighed or declined, and says which', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-verdict-'))
  const projectCopy = join(workDir, 'project')
  copyProject(REAL_PROJECT, projectCopy)
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await page.setViewportSize({ width: 1440, height: 900 })
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  const where = await page.evaluate(async () => {
    const project = window.novalistStores.project.getState()
    const chapter = project.chapters.find((c) => c.scenes.length > 0)!
    const scene = chapter.scenes[0]
    await project.openScene(chapter.guid, scene.id)
    await window.novalistRpc.request('review/applyComments', [
      chapter.guid,
      scene.id,
      [
        { anchorText: '', text: 'The middle of this chapter drags badly.', author: 'Beta reader', kind: 'comment', date: '' },
        { anchorText: '', text: 'Is the sister necessary at all?', author: 'Beta reader', kind: 'comment', date: '' }
      ]
    ])
    return { chapterGuid: chapter.guid, sceneId: scene.id }
  })
  expect(where.sceneId).toBeTruthy()

  await page.evaluate(() => window.novalistStores.shell.getState().setInspectorTab('inbox'))
  const items = page.locator('.inbox-item')
  await expect(items).toHaveCount(2, { timeout: 30_000 })

  // Weighing: a state to sit in, so the note stays where it can be seen.
  await items.first().locator('.inbox-verdict-btn').nth(1).click()
  await expect(items).toHaveCount(2, { timeout: 15_000 })
  await expect(items.first().locator('.inbox-verdict')).toBeVisible()

  // Declining: finished with, so it leaves the open list exactly as resolving
  // does - but the reason is kept, which is the whole point.
  await items.first().locator('.inbox-verdict-btn').nth(2).click()
  await expect(items).toHaveCount(1, { timeout: 15_000 })

  const stored = (await page.evaluate(async () =>
    window.novalistRpc.request('inbox/list', [true])
  )) as { text: string; verdict: string; resolved: boolean }[]
  const declined = stored.find((i) => i.text.includes('middle of this chapter'))!
  expect(declined.verdict).toBe('declined')
  expect(declined.resolved).toBe(true)

  await app.close()
})
