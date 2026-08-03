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

  await expect(target).toBeVisible()
  await expect(card).toBeVisible()

  // The drag events are dispatched rather than performed with the mouse.
  //
  // HTML5 drag and drop is driven by the operating system's own drag loop, and
  // there is no such loop under the headless X server the e2e job runs on: no
  // amount of mouse input there produces a dragstart, so both dragTo and a
  // hand-rolled press-move-release passed on a desktop and dropped nothing in
  // CI. Sending the three events the browser would have sent is the same
  // sequence the board actually listens for - dragstart naming the card,
  // dragover on the column, drop - and it behaves the same way everywhere.
  //
  // What this no longer covers is the browser's own drag machinery: that the
  // card is draggable at all, and that a real pointer gesture starts a drag.
  // Those hold on every platform a writer runs and on none that CI can drive.
  // Runs in the page, so the event type comes in as an argument rather than
  // through a closure.
  const fire = (el: Element, type: string): void => {
    el.dispatchEvent(
      new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() })
    )
  }

  // One event per step, not all three in a row: the board remembers which card
  // is moving in React state, and a handler that runs in the same tick as the
  // dragstart still sees the state from before it. A real drag has the browser
  // between the events; here the round trip does the same job.
  await card.evaluate(fire, 'dragstart')
  await target.evaluate(fire, 'dragover')

  // The column highlights on dragover, which is the board saying it has taken
  // the drag - and proof that the render carrying the card's identity has
  // landed before the drop asks for it.
  await expect(target).toHaveClass(/\bover\b/)

  await target.evaluate(fire, 'drop')

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
