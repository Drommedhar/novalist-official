import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * A picture lands where the writer asked for it.
 *
 * The frame owns no file picker, so inserting an image is a round trip through
 * the host: a native file dialog, then a dialog asking for alt text. Both take
 * focus out of the frame and leave it with no selection, so reading the caret
 * when the answer came back read nothing and every picture went to the end of
 * the scene. Where it goes has to be settled at the moment of asking.
 *
 * Reported from the checklist as "Its not adding it where the caret currently
 * is".
 */

/** Puts the caret in the editor's Nth paragraph, the way a click would. */
async function caretInParagraph(page: import('@playwright/test').Page, index: number) {
  await page.frameLocator('.editor-frame').locator('#editor').evaluate((el, i) => {
    const p = el.querySelectorAll('p')[i]
    const range = document.createRange()
    range.setStart(p.firstChild ?? p, 0)
    range.collapse(true)
    const sel = window.getSelection()!
    sel.removeAllRanges()
    sel.addRange(range)
  }, index)
}

/** The editor's paragraphs, an image shown as "[img]". */
async function shape(page: import('@playwright/test').Page): Promise<string[]> {
  return page
    .frameLocator('.editor-frame')
    .locator('#editor')
    .evaluate((el) =>
      Array.from(el.querySelectorAll('p')).map((p) =>
        p.querySelector('img') ? '[img]' : (p.textContent ?? '').trim()
      )
    )
}

test('an image is placed at the caret, not at the end of the scene', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insert-image-')
  await seedBook(h, { One: ['A'] })

  await h.page.locator('.binder-scene-row').first().click()
  const editor = h.page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })

  await h.page
    .frameLocator('.editor-frame')
    .locator('#editor')
    .evaluate(() =>
      (window as unknown as { setContent(html: string): void }).setContent(
        '<p>First</p><p>Second</p><p>Third</p>'
      )
    )
  expect(await shape(h.page)).toEqual(['First', 'Second', 'Third'])

  // Ask for the image from the middle paragraph, which is where right-clicking
  // puts the caret.
  await caretInParagraph(h.page, 1)
  await editor.locator('p').nth(1).click({ button: 'right' })
  const frame = h.page.frameLocator('.editor-frame')
  // The scene actions are folded into a flyout, so the group has to be opened
  // before its rows are reachable.
  const group = frame.locator('.cm-parent', {
    has: frame.locator('.cm-item[data-action="insertImage"]')
  })
  await expect(group).toBeVisible({ timeout: 10_000 })
  await group.hover()
  const row = frame.locator('.cm-item[data-action="insertImage"]')
  await expect(row).toBeVisible({ timeout: 10_000 })
  await row.click()

  // Everything the host does next takes focus out of the frame: the native file
  // dialog cannot run in a test, so this is what it leaves behind.
  await h.page.frameLocator('.editor-frame').locator('#editor').evaluate(() => {
    window.getSelection()?.removeAllRanges()
  })

  await h.page.frameLocator('.editor-frame').locator('#editor').evaluate(() =>
    (window as unknown as { insertImageAtCaret(p: string, a: string): void }).insertImageAtCaret(
      'Images/plan.png',
      'the map'
    )
  )

  expect(await shape(h.page), 'the image did not land at the caret').toEqual([
    'First',
    'Second',
    '[img]',
    'Third'
  ])

  // And it is a real image with its alt text, not an empty paragraph.
  const img = await editor.locator('img').first()
  await expect(img).toHaveAttribute('alt', 'the map')
  await expect(img).toHaveAttribute('data-nv-src', 'Images/plan.png')

  await h.close()
})

test('an image lands at the caret in page view too', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insert-image-page-')
  await seedBook(h, { One: ['A'] })

  await h.page.locator('.binder-scene-row').first().click()
  const editor = h.page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })

  await h.page.frameLocator('.editor-frame').locator('#editor').evaluate(() => {
    const w = window as unknown as {
      setContent(html: string): void
      setPageView(on: boolean): void
    }
    w.setContent('<p>First</p><p>Second</p><p>Third</p>')
    w.setPageView(true)
  })

  // Page view moves every paragraph inside a .nv-page wrapper, so the paragraph
  // the caret is in is no longer a child of the editor. Asking whether it was
  // sent every image in page view to the end of the scene, and outside the
  // paper surface at that.
  await expect(h.page.frameLocator('.editor-frame').locator('.nv-page')).not.toHaveCount(0)

  await caretInParagraph(h.page, 1)
  await h.page.frameLocator('.editor-frame').locator('#editor').evaluate(() => {
    ;(window as unknown as { insertImageAtCaret(p: string, a: string): void }).insertImageAtCaret(
      'Images/plan.png',
      ''
    )
  })

  expect(await shape(h.page), 'the image did not land at the caret in page view').toEqual([
    'First',
    'Second',
    '[img]',
    'Third'
  ])

  // Inside the page it belongs to, rather than loose at the end of the editor.
  const insidePage = await h.page
    .frameLocator('.editor-frame')
    .locator('#editor')
    .evaluate((el) => {
      const img = el.querySelector('img')
      return !!img?.closest('.nv-page')
    })
  expect(insidePage, 'the image sits outside the paper surface').toBe(true)

  await h.close()
})
