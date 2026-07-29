import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * Scene titles line up in a column, staged or not.
 *
 * The row is a button laid out with space-between, which caught this twice: a
 * title that stretched to fill the row was centred by the button's own default,
 * and a stage dot rendered only for staged scenes pushed those titles a few
 * pixels right of the rest. Neither is visible to a unit test, and both are the
 * first thing the eye sees in a list of eighty scenes.
 */
test('scene titles share one left edge whether or not a scene has a stage', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-rows-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  // Titles of very different lengths: a centred title moves with its own width,
  // so a run of same-length ones would hide the bug.
  const staged = await page.evaluate(async (parent) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [parent, 'Rows', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const titles = ['A', 'A considerably longer scene title', 'Mid-length title', 'B']
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    for (const title of titles) state = await rpc.request('project/createScene', [guid, title])

    const stages = (await rpc.request('stages/list')) as { key: string }[]
    const scenes = ((state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
      .find((c) => c.guid === guid)?.scenes ?? [])
    // Every other scene gets one, so both kinds sit next to each other.
    for (let i = 0; i < scenes.length; i += 2) {
      state = await rpc.request('stages/setSceneStage', [guid, scenes[i].id, stages[0].key])
    }
    window.novalistStores.project.getState().applyState(state as never)
    return Math.ceil(scenes.length / 2)
  }, workDir)
  expect(staged).toBeGreaterThan(0)

  await expect(page.locator('.binder-scene-row')).toHaveCount(4, { timeout: 20_000 })

  const rows = await page.locator('.binder-scene-title').evaluateAll((els) =>
    els.map((el) => ({
      left: Math.round(el.getBoundingClientRect().left),
      align: getComputedStyle(el).textAlign
    }))
  )
  expect(rows).toHaveLength(4)
  // One left edge for all of them, and text that starts at it.
  expect(new Set(rows.map((r) => r.left)).size).toBe(1)
  expect(rows.every((r) => r.align === 'left')).toBe(true)

  await app.close()
})
