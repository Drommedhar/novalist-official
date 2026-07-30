import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * One Replace All is one batch of snapshots, and can be cleared as one.
 *
 * A project-wide replace snapshots every scene it touches, which on a long book
 * is hundreds at once. They all carried the same fixed label, so one run could
 * not be told from the last and the documented remedy was deleting folders on
 * disk with the project closed.
 */
test('a find/replace run groups its snapshots, and clearing it spares the rest', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-batches-'))
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
    let state = await rpc.request('project/create', [parent, 'Batches', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    for (const title of ['A', 'B']) {
      state = await rpc.request('project/createScene', [guid, title])
      const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
        .find((c) => c.guid === guid)!.scenes
      const scene = scenes[scenes.length - 1]
      await rpc.request('scenes/write', [guid, scene.id, '<p>the cat sat</p>', 'the cat sat'])
    }
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  // One snapshot the writer took, then a replace across both scenes.
  const kept = await page.evaluate(async () => {
    const rpc = window.novalistRpc
    const state = window.novalistStores.project.getState()
    const chapter = state.chapters[state.chapters.length - 1]
    await rpc.request('snapshots/take', [chapter.guid, chapter.scenes[0].id, 'Mine'])
    await rpc.request('search/replaceAll', [
      'cat', 'dog', false, false, false, 'ActiveBook', null, null, false
    ])
    return 'Mine'
  })
  expect(kept).toBe('Mine')

  const all = await page.evaluate(() =>
    window.novalistRpc.request<{ label: string }[]>('snapshots/all')
  )
  const batch = all.map((s) => s.label).filter((l) => l.startsWith('Before find/replace'))
  // Both scenes changed, and both belong to the same run.
  expect(batch).toHaveLength(2)
  expect(new Set(batch).size).toBe(1)

  const removed = await page.evaluate(
    (label) => window.novalistRpc.request<number>('snapshots/deleteByLabel', [label]),
    batch[0]
  )
  expect(removed).toBe(2)

  const left = await page.evaluate(() =>
    window.novalistRpc.request<{ label: string }[]>('snapshots/all')
  )
  expect(left.map((s) => s.label)).toEqual(['Mine'])

  await app.close()
})
