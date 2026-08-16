import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

/**
 * Every bound gesture survives the caret being in the prose.
 *
 * The editor runs in an iframe, so a keystroke typed while writing never
 * reaches the window listener that owns the app's hotkeys. editor.html decides
 * what to keep and what to forward, and anything it keeps by mistake is a
 * hotkey that quietly does not exist - not everywhere, only in the one place
 * the writer spends their time, which is why it goes unnoticed.
 *
 * That is what happened to the pane-splitting gestures: Ctrl+Arrow walks by
 * word and Ctrl+Shift+Arrow selects by word, so the arrows were reserved
 * wholesale - and Ctrl+Alt+Right went with them, despite contenteditable
 * having no use for it.
 *
 * So this reads every gesture the app actually has, out of Settings rather than
 * out of a list written twice, and asks the editor about each one. A new
 * command bound to something the editor eats fails here on the day it lands.
 */

/** What the editor is entitled to keep, and why. */
const RESERVED: Record<string, string> = {
  'ctrl+a': 'select all',
  'ctrl+c': 'copy',
  'ctrl+v': 'paste',
  'ctrl+x': 'cut',
  'ctrl+z': 'undo',
  'ctrl+y': 'redo',
  'ctrl+shift+z': 'redo',
  'ctrl+arrowleft': 'walk left by word',
  'ctrl+arrowright': 'walk right by word',
  'ctrl+arrowup': 'walk up by paragraph',
  'ctrl+arrowdown': 'walk down by paragraph',
  'ctrl+home': 'to the top',
  'ctrl+end': 'to the end'
}

/** The synthetic keydown the editor's own reservation test reads. */
function eventFor(gesture: string): Record<string, unknown> {
  const parts = gesture.split('+')
  const raw = parts[parts.length - 1]
  const key = raw.length === 2 && /^D\d$/.test(raw) ? raw.slice(1) : raw
  return {
    key,
    code: key,
    ctrlKey: parts.includes('Ctrl'),
    metaKey: false,
    shiftKey: parts.includes('Shift'),
    altKey: parts.includes('Alt')
  }
}

test('every bound gesture still reaches the app from inside the prose', async () => {
  test.setTimeout(180_000)
  const app = await launchApp('nl-gestures-')
  await seedBook(app, { One: ['A'] })
  const page = app.page

  await dismissTour(page)

  // The gestures the app has, read from the screen that lists them.
  await page.evaluate(() => window.novalistStores.shell.getState().openSettings('hotkeys'))
  await expect(page.locator('.settings-hotkey-row').first()).toBeVisible({ timeout: 20_000 })
  const bound = await page.locator('.settings-hotkey-row').evaluateAll((rows) =>
    rows
      .map((row) => ({
        id: row.getAttribute('data-action-id') ?? '',
        gesture: row.getAttribute('data-gesture') ?? ''
      }))
      .filter((row) => row.gesture.length > 0)
  )
  expect(bound.length).toBeGreaterThan(15)

  // No two commands may want the same keystroke; the first one in the list
  // would win and the second would look broken rather than taken.
  const byGesture = new Map<string, string[]>()
  for (const row of bound) {
    const key = row.gesture.toLowerCase()
    byGesture.set(key, [...(byGesture.get(key) ?? []), row.id])
  }
  const clashes = [...byGesture.entries()].filter(([, ids]) => ids.length > 1)
  expect(clashes, `two commands share a gesture: ${JSON.stringify(clashes)}`).toEqual([])

  await enterWriting(page)
  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })

  // Ask the editor itself, rather than a copy of its rules kept here.
  const swallowed = await page.locator('.editor-frame').evaluate(
    (frame: HTMLIFrameElement, payload: { gesture: string; event: Record<string, unknown> }[]) => {
      const win = frame.contentWindow as unknown as {
        isTextEditingShortcut(e: Record<string, unknown>): boolean
      }
      return payload.filter((row) => win.isTextEditingShortcut(row.event)).map((row) => row.gesture)
    },
    bound.map((row) => ({ gesture: row.gesture, event: eventFor(row.gesture) }))
  )

  const wrong = swallowed.filter((gesture) => !(gesture.toLowerCase() in RESERVED))
  expect(
    wrong,
    `the editor keeps these to itself, so they do nothing while writing: ${wrong.join(', ')}`
  ).toEqual([])

  // And the reservations themselves are still the ones we meant: walking by
  // word must keep working, or this test would pass by the editor forwarding
  // everything and breaking ordinary movement instead.
  const stillReserved = await page.locator('.editor-frame').evaluate(
    (frame: HTMLIFrameElement, events: Record<string, unknown>[]) => {
      const win = frame.contentWindow as unknown as {
        isTextEditingShortcut(e: Record<string, unknown>): boolean
      }
      return events.map((event) => win.isTextEditingShortcut(event))
    },
    ['Ctrl+ArrowRight', 'Ctrl+Shift+ArrowLeft', 'Ctrl+A', 'Ctrl+Z'].map(eventFor)
  )
  expect(stillReserved).toEqual([true, true, true, true])

  await app.close()
})

/**
 * The gesture the writer reported, end to end: pressed with the caret in the
 * prose, it has to actually split the pane.
 */
test('Ctrl+Alt+Right splits the pane while the caret is in the prose', async () => {
  test.setTimeout(180_000)
  const app = await launchApp('nl-split-')
  await seedBook(app, { One: ['A'] })
  const page = app.page

  await dismissTour(page)
  await enterWriting(page)
  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.type('a sentence')

  // The pane, not the editor in it: a fresh half has no scene yet and shows
  // the placeholder rather than an editor.
  await expect(page.locator('.pane-leaf')).toHaveCount(1)
  await page.keyboard.press('Control+Alt+ArrowRight')
  await expect(page.locator('.pane-leaf')).toHaveCount(2, { timeout: 15_000 })

  // And closing it again, from the same place.
  await page.keyboard.press('Control+Alt+w')
  await expect(page.locator('.pane-leaf')).toHaveCount(1, { timeout: 15_000 })

  await app.close()
})
