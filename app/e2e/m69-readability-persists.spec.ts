import { test, expect } from '@playwright/test'
import { launchApp, seedBook, dismissTour } from './harness'

/**
 * Marking hard-to-read sentences, and having the marks stay.
 *
 * The marks are live Ranges painted with the Custom Highlight API, and the
 * prose DOM under them is rebuilt constantly - page view moves every paragraph
 * into a fresh wrapper, the grammar pass wraps flagged words in spans. Moving a
 * node out of its parent collapses every Range inside it, so the highlight
 * stayed registered while covering nothing: the marking appeared for an instant
 * when the writer turned it on and then vanished.
 *
 * Range count alone cannot see this - it stays at one either way. What has to
 * be asserted is that the ranges still cover text.
 */

/** How much text the readability marks actually cover, in characters. */
const markedLength = (page: import('@playwright/test').Page): Promise<number> =>
  page.evaluate(() => {
    const win = document.querySelector<HTMLIFrameElement>('.editor-frame')!
      .contentWindow as unknown as {
      CSS: { highlights: Map<string, Iterable<Range>> }
    }
    let total = 0
    for (const band of ['VeryEasy', 'Easy', 'Moderate', 'Difficult', 'VeryDifficult']) {
      const highlight = win.CSS.highlights.get('nv-read-' + band)
      if (!highlight) continue
      for (const range of highlight) total += range.toString().length
    }
    return total
  })

test('hard-to-read marks survive the prose being re-laid-out', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-readability-')
  await dismissTour(h.page)
  await seedBook(h, { 'Chapter One': ['Scene One'] })

  // Page view is what re-wraps the paragraphs, and it is the default; asked for
  // explicitly so the spec does not rest on that staying true.
  await h.page.evaluate(async () => {
    await window.novalistStores.settings.getState().update('global', { pageViewEnabled: true })
  })

  await h.page.locator('.binder-scene-row').first().click()
  const editor = h.page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })

  await editor.click()
  await h.page.keyboard.press('Control+A')
  await h.page.keyboard.type(
    'Notwithstanding the inordinately protracted deliberations which the assembled magistrates, ' +
      'having convened beneath the crumbling vault, undertook with a solemnity that bordered upon ' +
      'the theatrical, the resolution eventually promulgated proved indistinguishable from the one ' +
      'that had been proposed, dismissed, and resurrected on three prior occasions.'
  )
  await h.page.waitForTimeout(1_500)

  // Turned on the way the Writing options menu turns it on.
  await h.page.evaluate(async () => {
    await window.novalistStores.settings
      .getState()
      .update('global', { readabilityHighlighting: true })
  })

  // The sentence is marked...
  await expect.poll(() => markedLength(h.page), { timeout: 15_000 }).toBeGreaterThan(100)

  // ...and it is still marked once the page has been re-laid-out under it,
  // which happens a beat later. This is the whole defect: the marks used to be
  // gone by now.
  await h.page.waitForTimeout(4_000)
  expect(await markedLength(h.page)).toBeGreaterThan(100)

  await h.close()
})
