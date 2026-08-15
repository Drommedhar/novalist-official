import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * A pane torn out into its own window, and two panes side by side.
 *
 * The Codex on a second monitor while the manuscript stays where it is. What
 * matters, and what no unit test can reach, is that the second window is a
 * real one: its own connection to the same backend, showing the real view
 * rather than a picture of it. A window that opens blank, or opens showing the
 * main shell again, would look fine in every other test - as would a split that
 * puts the same scene in both halves.
 */

test('a pane opens in its own window and shows the real view', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-popout-'))
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

  // Tear the Codex out.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await page.waitForTimeout(600)
  const opened = app.waitForEvent('window')
  await page.evaluate(() =>
    window.novalist.openPaneWindow({
      view: 'codex',
      projectPath: window.novalistStores.project.getState().projectPath,
      chapterGuid: null,
      sceneId: null
    })
  )
  const second = await opened

  // The real Codex, in the second window: entries the backend gave it, not an
  // empty shell.
  await expect(second.locator('.codex-hub, .codex-list, .entity-list').first()).toBeVisible({
    timeout: 30_000
  })
  // And it is a torn-off pane rather than another copy of the whole app.
  await expect(second.locator('.app-shell.detached')).toBeVisible({ timeout: 15_000 })
  await expect(second.locator('.mode-rail')).toHaveCount(0)
  // It opened on the project the pane came from, not on whichever was most
  // recent, and its header is what lets a window with no activity bar be
  // pointed somewhere else.
  expect(
    await second.evaluate(() => window.novalistStores.project.getState().projectPath)
  ).toBe(projectCopy)
  await expect(second.locator('.pane-header')).toBeVisible()

  // The window it came from is untouched.
  await expect(page.locator('.mode-rail')).toBeVisible()

  await app.close()
})

test('splitting the editor gives each pane its own scene', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-panes-'))
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

  // Two scenes to tell apart.
  const scenes = await page.evaluate(() => {
    const chapters = window.novalistStores.project.getState().chapters
    const flat = chapters.flatMap((c) =>
      c.scenes.map((s) => ({ chapterGuid: c.guid, sceneId: s.id }))
    )
    return flat.slice(0, 2)
  })
  test.skip(scenes.length < 2, 'project has fewer than two scenes')

  await page.evaluate(
    (scene) => window.novalistStores.project.getState().openScene(scene.chapterGuid, scene.sceneId),
    scenes[0]
  )
  await expect(page.locator('.editor-pane')).toHaveCount(1)

  // Split, then put the second scene in the pane that appeared. It used to be
  // the same scene twice with no way to change either half.
  await page.evaluate(() => window.novalistStores.shell.getState().splitActivePane('row'))
  await expect(page.locator('.pane-header')).toHaveCount(2)
  await expect(page.locator('.pane-divider')).toHaveCount(1)

  await page.evaluate(
    (scene) => window.novalistStores.project.getState().openScene(scene.chapterGuid, scene.sceneId),
    scenes[1]
  )
  await expect(page.locator('.editor-pane')).toHaveCount(2)

  // Two panes, two different scenes.
  const open = await page.evaluate(() =>
    Object.values(window.novalistStores.project.getState().editors).map((e) => e.sceneId)
  )
  expect(open.filter(Boolean)).toHaveLength(2)
  expect(new Set(open.filter(Boolean)).size).toBe(2)

  // A pane can be pointed at something that is not the editor.
  const paneIds = await page.evaluate(() => {
    const leaves: string[] = []
    const walk = (node: { kind: string; id: string; children?: unknown[] }): void => {
      if (node.kind === 'leaf') leaves.push(node.id)
      else (node.children as typeof node[]).forEach(walk)
    }
    walk(window.novalistStores.shell.getState().panes as never)
    return leaves
  })
  await page.evaluate(
    (id) => window.novalistStores.shell.getState().setPaneView(id, 'codex'),
    paneIds[1]
  )
  await expect(page.locator('.editor-pane')).toHaveCount(1)
  // The pane that stopped being an editor let go of its scene.
  expect(
    await page.evaluate(
      () =>
        Object.values(window.novalistStores.project.getState().editors).filter((e) => e.sceneId)
          .length
    )
  ).toBe(1)

  await app.close()
})
