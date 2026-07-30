import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * The board groups scenes by a dimension the writer picks, and dropping a card
 * in another column writes that field - which is the only thing that makes a
 * board different from a filtered list.
 */
test('the board groups scenes by stage and a drop rewrites the stage', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-board-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const sceneId = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [parent, 'Board', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    state = await rpc.request('project/createScene', [guid, 'Opening'])
    window.novalistStores.project.getState().applyState(state as never)
    const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
      .find((c) => c.guid === guid)!.scenes
    return scenes[scenes.length - 1].id
  }, workDir)

  await page.evaluate(() => {
    window.novalistStores.shell.getState().setMainView('manuscript')
  })
  // The book in this fixture is also called "Board"; the mode button is the
  // one in the manuscript toolbar.
  await page.locator('.manuscript-modes .codex-tab').getByText('Board', { exact: true }).click()

  // Every stage gets a column, plus the untriaged pile - a board that hides
  // the scenes nobody has classified says the work is finished.
  const columns = page.locator('.board-column')
  await expect(columns).toHaveCount(6, { timeout: 20_000 })
  await expect(page.locator('.board-column').last()).toContainText('Not set')
  await expect(page.locator('.board-column').last().locator('.board-card')).toHaveCount(1)

  // Dropping into "Revised" writes the stage rather than only moving a card.
  const card = page.locator('.board-card').first()
  const target = page.locator('.board-column', { hasText: 'Revised' }).first()
  await card.dragTo(target)

  await expect
    .poll(
      async () =>
        (await page.evaluate(
          (id) =>
            window.novalistStores.project
              .getState()
              .chapters.flatMap((c: { scenes: { id: string; stage?: string | null }[] }) => c.scenes)
              .find((s: { id: string }) => s.id === id)?.stage ?? null,
          sceneId
        )) as string | null,
      { timeout: 10_000 }
    )
    .toBe('revised')

  await app.close()
})
