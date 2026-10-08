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
 * - the mode panel stays docked at every desktop width, above scenes in Write;
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

/** What the window is showing after its panels settle. */
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

  for (const capacity of CAPACITIES) {
    await page.setViewportSize({ width: capacity.width, height: capacity.height })
    await expect(page.locator('.shell')).toHaveAttribute('data-shell-capacity', capacity.name)

    for (const mode of MODES) {
      // The navigation row stays reachable at every desktop width.
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
      await expect(page.locator('.mode-panel:not(.overlay)')).toBeVisible({ timeout: 10_000 })

      const where = `${mode} @ ${capacity.name}`
      // Exiting panels remain inert in the DOM until their fade completes.
      await expect.poll(read, { message: where }).toEqual({
        rail: 1,
        modePanel: 1,
        modePanelOverlay: 0,
        binder: mode === 'write' ? 1 : 0,
        inspector: mode === 'write' && capacity.name === 'wide' ? 1 : 0,
        status: 1,
        projectActions: mode === 'series' ? 0 : capacity.name === 'compact' ? 1 : 2
      } satisfies Cell)
    }
  }

  await h.close()
})
