import { test, expect, type FrameLocator, type Page } from '@playwright/test'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

/**
 * Footnotes and suggested edits: the two things somebody puts in a scene that
 * are not the prose.
 *
 * Three failures the writer hit in one sitting, none of which any unit test
 * could reach because all three live in the gap between the editor iframe and
 * the panels beside it:
 *
 *   - a footnote's number is where its marker stands in the prose, and only the
 *     new note's number was ever recorded - so a note put in ahead of an
 *     existing one left two notes both called 1 while the page read 1 and 2;
 *   - the list showed whatever it had read when the scene opened, so a note
 *     made in the prose did not appear in it at all;
 *   - somebody else's proposed edits were listed under Footnotes, which is
 *     where a writer's own asides live and the last place anybody looks for a
 *     change another person asked for.
 */

type Footnote = { id: string; number: number; text: string }
type Annotations = { footnotes: Footnote[] }

const TWO_PARAGRAPHS = '<p>Alpha beta gamma.</p><p>Delta epsilon zeta.</p>'

/** The prose, once it is actually showing the scene that was written. */
async function editorShowing(h: Harness, contains: string): Promise<FrameLocator> {
  const frame = h.page.frameLocator('.editor-frame')
  await expect(frame.locator('#editor')).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () => (await frame.locator('#editor').innerText()).includes(contains), {
      timeout: 20_000
    })
    .toBe(true)
  return frame
}

/** Puts the caret at the end of a paragraph and drops a footnote marker there. */
async function footnoteAtEndOf(page: Page, paragraph: number, id: string): Promise<void> {
  await page.frameLocator('.editor-frame').locator('#editor p').nth(paragraph).click()
  await page.keyboard.press('End')
  await page.evaluate((footnoteId: string) => {
    const frame = document.querySelector('.editor-frame') as HTMLIFrameElement
    ;(frame.contentWindow as unknown as { insertFootnoteAtSelection(id: string): void })
      .insertFootnoteAtSelection(footnoteId)
  }, id)
}

test('a footnote put in ahead of another renumbers both', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-fn-number-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id
  await dismissTour(h.page)

  await h.rpc('scenes/write', [guid, sceneId, TWO_PARAGRAPHS, 'Alpha beta gamma. Delta epsilon zeta.'])
  await h.page.evaluate(
    (a: { g: string; s: string }) => window.novalistStores.project.getState().openScene(a.g, a.s),
    { g: guid, s: sceneId }
  )
  const frame = await editorShowing(h, 'Alpha')

  // The second paragraph first, then the first one - so the newer note stands
  // ahead of the older one in the prose and takes its number off it.
  await footnoteAtEndOf(h.page, 1, 'fn-later-in-the-scene')
  await expect(frame.locator('sup.nv-fn')).toHaveCount(1, { timeout: 10_000 })
  await footnoteAtEndOf(h.page, 0, 'fn-earlier-in-the-scene')
  await expect(frame.locator('sup.nv-fn')).toHaveCount(2, { timeout: 10_000 })

  // What the page says.
  expect(await frame.locator('sup.nv-fn').allInnerTexts()).toEqual(['1', '2'])

  // What the book says. These have to be the same thing, and were not: both
  // notes came back as number 1.
  await expect
    .poll(
      async () => {
        const stored = await h.rpc<Annotations>('scenes/getAnnotations', [guid, sceneId])
        return stored.footnotes.map((f) => `${f.number}:${f.id}`)
      },
      { timeout: 15_000 }
    )
    .toEqual(['1:fn-earlier-in-the-scene', '2:fn-later-in-the-scene'])

  await h.close()
})

test('a new footnote shows in the list at once, with the caret in its box', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-fn-live-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id
  await dismissTour(h.page)

  await h.rpc('scenes/write', [guid, sceneId, TWO_PARAGRAPHS, 'Alpha beta gamma. Delta epsilon zeta.'])
  await h.page.evaluate(
    (a: { g: string; s: string }) => window.novalistStores.project.getState().openScene(a.g, a.s),
    { g: guid, s: sceneId }
  )
  await editorShowing(h, 'Alpha')

  // Standing somewhere else entirely, which is the ordinary case: the writer is
  // writing, not watching a panel.
  await h.page.evaluate(() => window.novalistStores.shell.getState().setInspectorTab('context'))
  await expect(h.page.locator('.annotation-num')).toHaveCount(0)

  await footnoteAtEndOf(h.page, 0, 'fn-written-now')

  // The list is where the note goes, so the list has to be showing.
  await expect
    .poll(() => h.page.evaluate(() => window.novalistStores.shell.getState().inspectorTab), {
      timeout: 15_000
    })
    .toBe('footnotes')
  await expect(h.page.locator('.annotation-num')).toHaveCount(1, { timeout: 15_000 })

  // And the caret is in it, because the moment a note is worth writing is the
  // moment its marker goes in.
  await expect
    .poll(
      () =>
        h.page.evaluate(() => {
          const active = document.activeElement as HTMLElement | null
          return active?.closest('.annotation-row') ? active.tagName : ''
        }),
      { timeout: 15_000 }
    )
    .toBe('INPUT')

  await h.close()
})

test('suggested edits are in the Inbox, and a scene row leads to one', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-sugg-home-')
  const book = await seedBook(h, { One: ['A', 'B'] })
  const { guid } = book.chapters[0]
  const [first, second] = book.chapters[0].scenes
  await dismissTour(h.page)

  await h.rpc('scenes/write', [
    guid,
    first.id,
    '<p>Alpha beta gamma<ins data-nl-change="s1" data-nl-author="Reader" data-nl-at="2026-01-01">, indeed</ins>.</p>' +
      '<p><del data-nl-change="s2" data-nl-author="Reader" data-nl-at="2026-01-01">Delta </del>epsilon zeta.</p>',
    'Alpha beta gamma, indeed. epsilon zeta.'
  ])
  await h.rpc('scenes/write', [guid, second.id, '<p>Somewhere else.</p>', 'Somewhere else.'])

  // Standing in the scene with the edits, which is where the writer was when
  // they went looking for them.
  await h.page.evaluate(
    (a: { g: string; s: string }) => window.novalistStores.project.getState().openScene(a.g, a.s),
    { g: guid, s: first.id }
  )
  const frame = await editorShowing(h, 'Alpha')

  // Footnotes is for footnotes. An edit somebody else proposed is not one.
  await h.page.evaluate(() => window.novalistStores.shell.getState().setInspectorTab('footnotes'))
  await expect(h.page.locator('.suggestions-panel')).toHaveCount(0)

  // The Inbox is the tab for things waiting on an answer, and it already held
  // the book-wide list of scenes with edits.
  await h.page.evaluate(() => window.novalistStores.shell.getState().setInspectorTab('inbox'))
  await expect(h.page.locator('.suggestions-panel .suggestion-row')).toHaveCount(2, {
    timeout: 15_000
  })
  const sceneRow = h.page.locator('.inbox-suggestion-row')
  await expect(sceneRow).toHaveCount(1, { timeout: 15_000 })

  // Following the row while already standing in that scene did nothing at all:
  // it opened a scene that was open. It has to end at one of the edits.
  await sceneRow.click()
  await expect(frame.locator('.nv-change-flash')).toHaveCount(1, { timeout: 15_000 })

  await h.close()
})
