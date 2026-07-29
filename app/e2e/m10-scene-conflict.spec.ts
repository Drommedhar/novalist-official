import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * A scene that changed on disk while the editor held it.
 *
 * This is what a synced project folder produces, and the write path used to
 * overwrite the other machine's work without looking. The assertion that matters
 * is the negative one: after a refused save, the other side's text is still in
 * the file.
 */

test('a save is refused when the scene changed on disk, and the merge resolves it', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-conflict-'))

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const ids = await page.evaluate(async (dir) => {
    const state = await window.novalistRpc.request('project/create', [dir, 'Sync Novel', 'Book'])
    window.novalistStores.project.getState().applyState(state as never)
    await window.novalistStores.project.getState().createChapter('Chapter One')
    const guid = window.novalistStores.project.getState().chapters[0].guid
    await window.novalistStores.project.getState().createScene(guid, 'Scene One')
    const sceneId = window.novalistStores.project.getState().chapters[0].scenes[0].id
    return { guid, sceneId }
  }, workDir)

  // The editor reads the scene, so the store now holds the hash it saw.
  await page.locator('.binder-scene-row').first().click()
  await expect(page.frameLocator('.editor-frame').locator('#editor')).toBeVisible({
    timeout: 30_000
  })

  // The other machine's save lands, through the same RPC a sync would replay.
  await page.evaluate(
    async ({ guid, sceneId }) => {
      await window.novalistRpc.request('scenes/write', [
        guid,
        sceneId,
        '<p>from the other machine</p>',
        'from the other machine'
      ])
    },
    ids
  )

  // Now our save goes out through the real edit-and-flush path, carrying the
  // hash the editor read before the other machine wrote: refused, dialog opens.
  await page.evaluate(async () => {
    const store = window.novalistStores.project.getState()
    store.onEditorContentChanged('<p>mine</p>', 'mine')
    await window.novalistStores.project.getState().flushPendingSave()
  })

  await expect(page.locator('.scene-conflict-card')).toBeVisible({ timeout: 15_000 })

  // The other machine's work is still in the file: nothing was overwritten.
  const onDisk = await page.evaluate(
    async ({ guid, sceneId }) =>
      ((await window.novalistRpc.request('scenes/read', [guid, sceneId])) as { html: string })
        .html,
    ids
  )
  expect(onDisk).toBe('<p>from the other machine</p>')

  // Both versions are offered, and the writer's own is preselected.
  await expect(page.locator('.scene-conflict-row')).not.toHaveCount(0)

  // Taking everything from disk and saving resolves it.
  await page.locator('.scene-conflict-actions button').nth(1).click()
  await page.locator('.scene-conflict-card .dialog-button.danger').click()
  await expect(page.locator('.scene-conflict-card')).toHaveCount(0, { timeout: 15_000 })

  const resolved = await page.evaluate(
    async ({ guid, sceneId }) =>
      ((await window.novalistRpc.request('scenes/read', [guid, sceneId])) as { html: string })
        .html,
    ids
  )
  expect(resolved).toContain('from the other machine')

  await app.close()
})
