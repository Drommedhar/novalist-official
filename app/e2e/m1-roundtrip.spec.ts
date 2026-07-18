import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * M1 exit criterion, against the real app (built renderer + real backend):
 * create project -> chapter -> scene -> type in the real editor.html ->
 * autosave persists to disk -> content survives in the scene file.
 */

function findSceneFiles(root: string): string[] {
  const found: string[] = []
  const walk = (dir: string): void => {
    for (const entry of readdirSync(dir)) {
      const full = join(dir, entry)
      if (statSync(full).isDirectory()) walk(full)
      else if (entry.endsWith('.novalist')) found.push(full)
    }
  }
  walk(root)
  return found
}

test('project + binder + editor round-trip', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-e2e-'))

  // VS Code (and other Electron hosts) exports ELECTRON_RUN_AS_NODE, which would
  // make the launched binary behave as plain Node and reject Playwright's flags.
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()

  // Backend handshake: status bar shows the core version like "Core connected (1.13...)".
  await expect(page.locator('.status-backend')).toContainText('(', { timeout: 30_000 })

  // Create + open a project through the app's own RPC client (bypasses the native picker).
  await page.evaluate(async (dir) => {
    const state = await window.novalistRpc.request('project/create', [dir, 'E2E Novel', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  await page.evaluate(() => window.novalistStores.project.getState().createChapter('Chapter One'))
  await expect(page.locator('.binder-chapter-title')).toHaveText('Chapter One')

  await page.evaluate(() => {
    const store = window.novalistStores.project.getState()
    return store.createScene(store.chapters[0].guid, 'Opening')
  })
  const sceneRow = page.locator('.binder-scene-row')
  await expect(sceneRow).toHaveCount(1)

  // Open the scene: the real editor.html loads in the iframe.
  await sceneRow.click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })

  // Type prose and let the 2s autosave fire.
  await editor.click()
  await page.keyboard.type('It was a dark and stormy night.')
  await page.waitForTimeout(4000)

  // The scene file on disk now contains the typed text.
  const projectRoot = join(workDir, 'E2E Novel')
  const sceneFiles = findSceneFiles(projectRoot)
  const hit = sceneFiles.find((f) => readFileSync(f, 'utf8').includes('dark and stormy night'))
  expect(hit, `scene files searched: ${sceneFiles.join(', ')}`).toBeTruthy()

  // Reopen the project fresh: state round-trips with a persisted word count.
  const reopened = (await page.evaluate((root) => {
    return window.novalistRpc.request('project/open', [root])
  }, projectRoot)) as { chapters: { scenes: { wordCount: number }[] }[] }
  expect(reopened.chapters[0].scenes[0].wordCount).toBe(7)

  await app.close()
})
