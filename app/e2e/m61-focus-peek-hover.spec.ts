import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, resizeWindow, seedBook } from './harness'

/**
 * A hovered name holds its Focus Peek still.
 *
 * The peek is host DOM drawn over the editor iframe, so a card placed under the
 * pointer takes the hover away from the editor beneath it. The editor then
 * reports that the pointer left the name, the host hides the card, the pointer
 * is over the name again, and it reopens - a loop that reads as flicker and
 * that no debounce settles, because each half of it is behaving correctly.
 *
 * What is asserted is the invariant that makes the loop impossible rather than
 * the absence of flicker over some sampling window: the card never covers the
 * point it is anchored to. The stationary-pointer sampling is kept as the
 * symptom-level check, and the second half forces the branch where the card
 * cannot fit below the pointer and has to go somewhere else.
 */

const NAME = 'Mira Vance'

interface Box {
  x: number
  y: number
  width: number
  height: number
}

/** Viewport rectangle of the first occurrence of `word` in the prose. */
async function wordBox(page: import('@playwright/test').Page, word: string): Promise<Box> {
  const frame = await page.locator('.editor-frame').boundingBox()
  if (!frame) throw new Error('the editor frame has no box')
  const inFrame = await page
    .frameLocator('.editor-frame')
    .locator('#editor')
    .evaluate((el, needle: string) => {
      const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT)
      let node = walker.nextNode()
      while (node) {
        const index = (node.textContent ?? '').indexOf(needle)
        if (index >= 0) {
          const range = document.createRange()
          range.setStart(node, index)
          range.setEnd(node, index + needle.length)
          const rect = range.getBoundingClientRect()
          return { x: rect.left, y: rect.top, width: rect.width, height: rect.height }
        }
        node = walker.nextNode()
      }
      return null
    }, word)
  if (!inFrame) throw new Error(`"${word}" is not in the prose`)
  return { ...inFrame, x: frame.x + inFrame.x, y: frame.y + inFrame.y }
}

const centre = (box: Box): { x: number; y: number } => ({
  x: box.x + box.width / 2,
  y: box.y + box.height / 2
})

/** The card's box, or null while nothing is shown. */
async function cardBox(page: import('@playwright/test').Page): Promise<Box | null> {
  const anchor = page.locator('.peek-card-anchor')
  if ((await anchor.count()) === 0) return null
  if (!(await anchor.isVisible())) return null
  return await anchor.boundingBox()
}

/** Do the two rectangles touch at all? The card must not touch the word. */
function overlaps(a: Box, b: Box): boolean {
  return (
    a.x < b.x + b.width &&
    a.x + a.width > b.x &&
    a.y < b.y + b.height &&
    a.y + a.height > b.y
  )
}

test('a hovered name keeps its Focus Peek open instead of flickering', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-peek-hover-')
  const book = await seedBook(h, { 'Chapter One': ['Opening'] })
  const page = h.page
  await dismissTour(page)
  // Closing the tour hands back the workspace it borrowed, which is the one the
  // project opened on - the Dashboard, and no binder. Back to Write.
  await enterWriting(page)

  // The name has to be a Codex entry before the scene opens: the editor is
  // handed the names to look for when it loads the scene.
  const created = await h.rpc<{ id: string }>('entities/create', ['character', NAME])
  // A card with something in it. A near-empty entry draws a card short enough to
  // fit anywhere, which is exactly the case that never exercised the placement.
  await h.rpc('entities/update', [
    'character',
    created.id,
    {
      description:
        'Harbourmaster of Hillsford, keeper of the tide tables, and the only '
        + 'person on either pier who still remembers what the water did the '
        + 'winter the second jetty went in. She counts hulls the way other '
        + 'people count sheep, and she has never once been wrong about a draught.'
    }
  ])
  for (const [title, text] of [
    ['Sketch', 'Weathered, unhurried, and entirely unimpressed by the harbour board.'],
    ['Notes', 'Do not reveal the brother until part two. She knows before anyone.'],
    ['Voice', 'Short sentences. Never raises her voice; never needs to.']
  ]) {
    await h.rpc('entities/appendToSection', ['character', created.id, title, text])
  }
  await page.evaluate(() => window.novalistStores.codex.getState().refresh())

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.type(`${NAME} stopped in the doorway and waited.`)
  await expect
    .poll(async () => (await editor.innerText()).includes(NAME), { timeout: 15_000 })
    .toBe(true)
  expect(book.chapters[0].scenes.length).toBe(1)

  // ── Hover the name, then drift the way a resting hand does ──
  const word = await wordBox(page, NAME)
  await page.mouse.move(centre(word).x, centre(word).y)
  await expect(page.locator('.peek-card-anchor')).toBeVisible({ timeout: 20_000 })

  const box = await cardBox(page)
  expect(box, 'the card should be on screen').not.toBeNull()
  expect(
    overlaps(box!, word),
    'the card must sit clear of the whole name, not merely of the pointer'
  ).toBe(false)

  // The symptom, and the thing that made it so hard to sit still through: the
  // card must neither blink nor move while the pointer wanders inside the name
  // it is anchored to. Anchored to the pointer, every one of these drifts
  // re-placed the card; several of them put it under the cursor and started the
  // hide/show loop.
  const seen = new Set<string>()
  let hidden = 0
  const onWord = [
    [0.2, 0.3], [0.8, 0.4], [0.5, 0.7], [0.3, 0.8], [0.9, 0.2], [0.5, 0.5]
  ]
  for (const [fx, fy] of onWord) {
    // Off the name for slightly longer than the editor's 200ms exit debounce,
    // then back onto it - a hand resting near a short word does this constantly.
    // The excursion is what makes the next hover a *fresh* one, and a card
    // anchored to the pointer re-places itself on every one of them.
    await page.mouse.move(word.x + word.width / 2, word.y - word.height * 1.5)
    await page.waitForTimeout(230)
    await page.mouse.move(word.x + word.width * fx, word.y + word.height * fy)
    await page.waitForTimeout(160)
    const current = await cardBox(page)
    if (current === null) hidden++
    else seen.add(`${Math.round(current.x)},${Math.round(current.y)}`)
  }
  expect(hidden, 'the card blinked out while the pointer stayed on the name').toBe(0)
  expect([...seen], 'the card moved while the pointer stayed on the name').toHaveLength(1)

  // ── The same, with no room below the name ──
  // A short window forces the placement out of its preferred band, which is the
  // case where a clamp used to drag the card back over the name.
  await resizeWindow(h, 1100, 560)
  // Move off the name so the next hover is a fresh one, and let the prose finish
  // reflowing before asking where the word ended up - measuring mid-reflow puts
  // the pointer where the word no longer is, and the peek never opens.
  await page.mouse.move(word.x - 40, word.y - 40)
  await page.waitForTimeout(1200)
  const moved = await wordBox(page, NAME)
  await page.mouse.move(centre(moved).x, centre(moved).y)

  // Polled rather than read once: the card is placed after it has measured
  // itself, so the first box may belong to a frame that is still settling.
  await expect
    .poll(
      async () => {
        const current = await cardBox(page)
        return current === null
          ? 'not shown'
          : overlaps(current, moved)
            ? 'covers the name'
            : 'clear'
      },
      { timeout: 20_000 }
    )
    .toBe('clear')

  await h.close()
})
