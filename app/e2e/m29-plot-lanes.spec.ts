import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * Threads as tracks, rather than a column of ticks.
 *
 * The grid answers "is this thread in this scene", one cell at a time. What a
 * revision asks is where two threads meet - the scene carrying the romance and
 * the mystery at once is the scene doing structural work, and a matrix hides it
 * among four hundred other cells.
 */
test('the plot lanes draw each thread as a track and mark where two meet', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-lanes-'))
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
    let state = await rpc.request('project/create', [parent, 'Threads', 'Book One'])
    state = await rpc.request('project/createChapter', ['One'])
    const guid = (state as { chapters: { guid: string }[] }).chapters.at(-1)!.guid
    const sceneIds: string[] = []
    for (const title of ['A', 'B', 'C']) {
      state = await rpc.request('project/createScene', [guid, title])
      const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
        .find((c) => c.guid === guid)!.scenes
      sceneIds.push(scenes.at(-1)!.id)
    }
    window.novalistStores.project.getState().applyState(state as never)

    await rpc.request('plot/createPlotline', ['The debt'])
    await rpc.request('plot/createPlotline', ['The romance'])
    const grid = (await rpc.request('plot/grid', ['plotline'])) as {
      plotlines: { id: string; name: string }[]
    }
    const debt = grid.plotlines.find((p) => p.name === 'The debt')!.id
    const romance = grid.plotlines.find((p) => p.name === 'The romance')!.id
    // Scene B carries both: the crossing worth finding.
    await rpc.request('plot/toggle', [guid, sceneIds[0], debt])
    await rpc.request('plot/toggle', [guid, sceneIds[1], debt])
    await rpc.request('plot/toggle', [guid, sceneIds[1], romance])
    await rpc.request('plot/toggle', [guid, sceneIds[2], romance])
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('plotGrid'))
  await expect(page.locator('.plotgrid-table')).toBeVisible({ timeout: 20_000 })

  await page.locator('.plotgrid-toolbar .dialog-button').click()
  await expect(page.locator('.plot-lanes')).toBeVisible({ timeout: 15_000 })

  // Two tracks, four stops, and exactly one scene marked as a crossing.
  await expect(page.locator('.plot-lane-track')).toHaveCount(2)
  await expect(page.locator('.plot-lane-stop')).toHaveCount(4)
  await expect(page.locator('.plot-lane-crossing')).toHaveCount(1)

  // Each thread has its own colour, or a lane view says nothing about which
  // track is which.
  const colours = await page.locator('.plot-lane-track').evaluateAll((lines) =>
    lines.map((l) => (l as SVGElement).style.stroke)
  )
  expect(new Set(colours).size).toBe(2)

  await app.close()
})
