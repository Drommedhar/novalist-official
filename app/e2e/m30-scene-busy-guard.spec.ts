import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'

/**
 * The guard that stops an extension writing over the scene being typed in.
 *
 * The backend cannot see the editor, so the renderer has to tell it which
 * scene is open and whether it has unsaved changes. That report is the whole
 * mechanism: if the renderer ever stops sending it, the guard silently allows
 * everything again and nothing else fails - no error, no failing unit test,
 * just an extension quietly overwriting somebody's paragraph again.
 *
 * The guard itself is unit-tested. What is asserted here is the half no unit
 * test can reach: that the running app reports at all, and reports the right
 * scene at the right moment.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('the editor tells the backend which scene it holds', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-busy-'))
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

  const reports = await page.evaluate(async () => {
    // Record what goes over the wire, because the report is the contract.
    const seen: unknown[][] = []
    const rpc = window.novalistRpc as unknown as {
      request: (method: string, params?: unknown[]) => Promise<unknown>
    }
    const original = rpc.request.bind(rpc)
    rpc.request = (method, params) => {
      if (method === 'scenes/setEditing') seen.push(params ?? [])
      return original(method, params)
    }

    const project = window.novalistStores.project.getState()
    const chapter = project.chapters[0]
    const scene = chapter.scenes[0]
    await project.openScene(chapter.guid, scene.id)
    // Typing is what marks a scene dirty; going through the store's own path
    // keeps this about the app's behaviour rather than the test's.
    window.novalistStores.project.getState().onEditorContentChanged('<p>Unsaved.</p>', 'Unsaved.')

    // The report rides a store subscription, so let it land.
    await new Promise((r) => setTimeout(r, 400))
    rpc.request = original
    return { seen, chapterGuid: chapter.guid, sceneId: scene.id }
  })

  // Opening reports the scene as clean; typing reports it dirty.
  expect(reports.seen).toContainEqual([reports.chapterGuid, reports.sceneId, false])
  expect(reports.seen).toContainEqual([reports.chapterGuid, reports.sceneId, true])

  await app.close()
})
