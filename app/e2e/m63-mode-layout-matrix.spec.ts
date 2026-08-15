import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

/**
 * Five modes by three window widths: fifteen layouts, checked in one place.
 *
 * What this replaces could not be checked at all. Chrome was decided by a
 * twenty-two-entry table crossed with three capacities - sixty-six combinations
 * that nobody could hold in their head and nothing verified, which is how
 * m51-mobile-no-hscroll sat green for months while asserting against a 2560px
 * window it believed was a 393px phone.
 *
 * A mode owns its layout now, so there are fifteen cells and they fit in one
 * test. The rules being asserted:
 *
 * - the mode rail is in every one of them, because it is how you leave;
 * - the mode panel is docked wherever the window can hold it, and an overlay
 *   where it cannot - never absent, and never a shorter list;
 * - the binder and the inspector belong to Write and to nothing else;
 * - the project bar is there wherever the mode is about the open book, which is
 *   every mode but Series;
 * - the status bar is in all of them.
 */

type Mode = 'write' | 'plan' | 'world' | 'publish' | 'series'

const MODES: Mode[] = ['write', 'plan', 'world', 'publish', 'series']

/** The three named capacities, at widths either side of their boundaries. */
const CAPACITIES = [
  { name: 'compact', width: 820, height: 720 },
  { name: 'medium', width: 1100, height: 800 },
  { name: 'wide', width: 1420, height: 900 }
] as const

/** What the window is showing, read once per cell. */
interface Cell {
  rail: number
  modePanel: number
  modePanelOverlay: number
  binder: number
  inspector: number
  status: number
  projectActions: number
}

test('every mode owns its layout, at every width', async () => {
  test.setTimeout(240_000)
  const h = await launchApp('nl-mode-matrix-')
  const book = await seedBook(h, { 'Chapter One': ['Opening'] })
  const page = h.page
  await dismissTour(page)
  // Closing the tour hands back the workspace it borrowed, which is the one the
  // project opened on - the Dashboard. Back to Write before a scene is opened.
  await enterWriting(page)

  // A scene open throughout: the inspector is about the scene in front of the
  // writer, so a Write cell with nothing open would not exercise it.
  await page.evaluate(
    async ({ chapterGuid, sceneId }) => {
      await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
    },
    { chapterGuid: book.chapters[0].guid, sceneId: book.chapters[0].scenes[0].id }
  )
  await expect(page.locator('.editor-frame')).toBeVisible({ timeout: 30_000 })

  const read = async (): Promise<Cell> => ({
    rail: await page.locator('.mode-rail').count(),
    modePanel: await page.locator('.mode-panel').count(),
    modePanelOverlay: await page.locator('.mode-panel.overlay').count(),
    binder: await page.locator('.binder').count(),
    inspector: await page.locator('.inspector').count(),
    status: await page.locator('.status-bar').count(),
    // Scoped to the project bar: a view's own bar uses the same button class,
    // so an unscoped count reads Plan's timeline controls as project actions.
    projectActions: await page.locator('.toolbar > .toolbar-action').count()
  })

  const wrong: string[] = []

  for (const capacity of CAPACITIES) {
    await page.setViewportSize({ width: capacity.width, height: capacity.height })
    await expect(page.locator('.shell')).toHaveAttribute('data-shell-capacity', capacity.name)

    for (const mode of MODES) {
      // Clicked directly, with the overlay panel and its scrim still up: the
      // rail is how you leave a mode, so it stays reachable through the scrim.
      // It did not, once - the scrim covered it, and switching modes in a
      // narrow window took two clicks, the first of which looked like it had
      // done nothing.
      await page.locator(`.mode-rail-item[data-mode="${mode}"]`).click()
      // A pane that stops being an editor lets its scene go, so leaving Write
      // for any other mode closes it. The scene is opened again on the way
      // back, the way a writer reopens it from the binder, because a Write cell
      // with nothing in it would not exercise the inspector.
      if (mode === 'write') {
        await page.evaluate(
          async ({ chapterGuid, sceneId }) => {
            await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
          },
          { chapterGuid: book.chapters[0].guid, sceneId: book.chapters[0].scenes[0].id }
        )
        await expect(page.locator('.editor-frame')).toBeVisible({ timeout: 20_000 })
      }
      // The panel is an overlay at compact, and picking a mode raises it.
      if (capacity.name === 'compact') {
        await expect(page.locator('.mode-panel')).toBeVisible({ timeout: 10_000 })
      }

      const cell = await read()
      const where = `${mode} @ ${capacity.name}`
      const say = (claim: string, actual: unknown, expected: unknown): void => {
        if (actual !== expected) wrong.push(`${where}: ${claim} was ${actual}, expected ${expected}`)
      }

      say('the mode rail', cell.rail, 1)
      say('the mode panel', cell.modePanel, 1)
      say('the panel as an overlay', cell.modePanelOverlay, capacity.name === 'compact' ? 1 : 0)
      // Write's, and nobody else's. At compact the binder is a drawer that is
      // shut, so it is not in the document at all.
      say('the binder', cell.binder, mode === 'write' && capacity.name !== 'compact' ? 1 : 0)
      // The inspector is a persistent column only where there is room for one.
      say('the inspector', cell.inspector, mode === 'write' && capacity.name === 'wide' ? 1 : 0)
      say('the status bar', cell.status, 1)
      // "+ Scene" is there in every mode about the open book; "+ Chapter" moves
      // into the overflow at compact, which is why this is one rather than two.
      const chrome = mode === 'series' ? 0 : capacity.name === 'compact' ? 1 : 2
      say('the project bar actions', cell.projectActions, chrome)
    }
  }

  expect(wrong, `${wrong.length} of the fifteen layouts are not what they should be`).toEqual([])

  await h.close()
})
