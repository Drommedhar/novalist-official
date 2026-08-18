import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { dismissTour } from './harness'

/**
 * The drafts of a book, as things rather than as entries in a dropdown.
 *
 * Renaming a draft existed on the project service and was called by nothing -
 * exactly the shape of failure the project rules describe. This goes through
 * the view, so it fails if the route disappears again, and it sends a chapter
 * from one draft to another because a copy that silently writes nothing looks
 * identical to one that worked.
 */
test('drafts can be renamed, reordered, and fed chapters from each other', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-drafts-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  // A fresh Chromium profile, like the harness gives every other spec. Without
  // it the run inherits this machine's localStorage - where the first-run tour
  // is long since marked seen - so the tour never appears locally and appears
  // on a clean CI runner, where its card takes the pointer events for the
  // button this spec has to press.
  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(
    page,
    async (parent) => {
      const state = await window.novalistRpc.request('project/create', [parent, 'Drafts', 'Book One'])
      window.novalistStores.project.getState().applyState(state as never)
    },
    workDir
  )
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })
  // A fresh profile is offered the tour, and its card takes the pointer events
  // for everything it covers - including the button this spec has to press.
  await dismissTour(page)

  // A chapter with a scene in the first draft, and an empty second draft to
  // send it to.
  await page.evaluate(async () => {
    const store = window.novalistStores.project.getState()
    await store.createChapter('The letter')
    const guid = window.novalistStores.project.getState().chapters[0].guid
    await window.novalistStores.project.getState().createScene(guid, 'The kitchen')
    await window.novalistRpc.request('project/createDraft', ['Beta cut', null])
  })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('drafts'))
  const rows = page.locator('.drafts-row')
  await expect(rows).toHaveCount(2, { timeout: 15_000 })

  // Renaming: the thing the backend could always do and nothing could reach.
  const first = rows.first()
  await first.locator('.drafts-name').fill('Zero draft')
  await first.locator('.drafts-name').blur()
  await expect(page.locator('.drafts-name').first()).toHaveValue('Zero draft')

  // A note saying what the draft is for - a field that existed on the record
  // and that nothing read or wrote.
  await first.locator('.drafts-notes').fill('the one nobody reads')
  await first.locator('.drafts-notes').blur()

  // Both survive the round trip to disk rather than only the render.
  const stored = await page.evaluate(
    async () =>
      (await window.novalistRpc.request('drafts/list', [])) as {
        name: string
        notes: string
        chapters: number
      }[]
  )
  expect(stored[0].name).toBe('Zero draft')
  expect(stored[0].notes).toBe('the one nobody reads')

  // Reordering, through the store rather than a synthetic drag: the list is
  // the order, and it comes back in the order asked for.
  const reordered = await page.evaluate(async () => {
    const rows = (await window.novalistRpc.request('drafts/list', [])) as { id: string }[]
    return (await window.novalistRpc.request('drafts/reorder', [
      [rows[1].id, rows[0].id]
    ])) as { name: string }[]
  })
  expect(reordered.map((d) => d.name)).toEqual(['Beta cut', 'Zero draft'])

  // Sending a chapter across. The picker reads the draft the writer is in, so
  // the chapter is there to tick.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('drafts'))
  const source = page.locator('.drafts-panes .drafts-pane').first()
  const pick = source.locator('.drafts-pick input').first()
  await expect(pick).toBeVisible({ timeout: 15_000 })
  await pick.check()

  // The other draft says what it will look like afterwards. Ticking boxes and
  // being handed a number never answered "what does this do over there".
  const target = page.locator('.drafts-panes .drafts-pane').nth(1)
  await expect(target.locator('.drafts-mark.new')).toHaveCount(2, { timeout: 10_000 })
  await expect(target.locator('.drafts-mark.rewritten')).toHaveCount(0)

  await page.locator('.drafts-send-actions .dialog-button.primary').click()

  // It arrived, with the chapter and its scene, in the other draft.
  await expect
    .poll(
      async () =>
        await page.evaluate(async () => {
          const rows = (await window.novalistRpc.request('drafts/list', [])) as {
            name: string
            chapters: number
          }[]
          return rows.find((d) => d.name === 'Beta cut')?.chapters ?? 0
        }),
      { timeout: 15_000 }
    )
    .toBe(1)

  // Sending the same chapter a second time rewrites what is there rather than
  // leaving a second copy, and the preview says so before it happens.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('drafts'))
  await page.locator('.drafts-panes .drafts-pane').first().locator('.drafts-pick input').first().check()
  await expect(
    page.locator('.drafts-panes .drafts-pane').nth(1).locator('.drafts-mark.rewritten')
  ).toHaveCount(1, { timeout: 10_000 })

  // Opening a draft from its own row moves "You are here" with it. The list was
  // fetched once and never told that the shell had gone somewhere else, so the
  // mark stayed on the draft that had been open when the view was drawn.
  const other = page.locator('.drafts-row').filter({ hasNot: page.locator('.drafts-current') })
  const otherName = await other.first().locator('.drafts-name').inputValue()
  await other.first().getByRole('button', { name: /Open|Öffnen/ }).click()
  await expect(
    page.locator('.drafts-row', { has: page.locator('.drafts-current') }).locator('.drafts-name')
  ).toHaveValue(otherName, { timeout: 10_000 })

  await app.close()
})
