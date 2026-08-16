import { test, expect, type Page } from '@playwright/test'
import {
  dismissTour,
  enterWriting,
  launchApp,
  resizeWindow,
  seedBook,
  type Book,
  type Harness
} from './harness'

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
 * symptom-level check, and a spec of its own forces the branch where the card
 * cannot fit below the pointer and has to go somewhere else.
 *
 * There is a second way to flicker that has nothing to do with where the card
 * is put, and `recordPeek` is what catches it: the card can blank itself. Its
 * contents arrive from a request, so a card that throws its contents away and
 * asks again blinks empty in between - and an empty card measures zero, which
 * for a sidebar row (placed to the *left* of what it belongs to, by its own
 * width) puts the next placement 460px away from the right one. The two feed
 * each other. Sampling every painted frame is the only way to see it, because
 * the whole storm is over in a tenth of a second and leaves the card sitting
 * exactly where it belongs.
 */

const NAME = 'Mira Vance'

interface Box {
  x: number
  y: number
  width: number
  height: number
}

/** One painted state of the peek anchor. `width` is zero while the card holds
 *  nothing, which is what a blank frame looks like from the outside. */
interface PeekState {
  visible: boolean
  left: number
  top: number
  width: number
  height: number
}

/** Viewport rectangle of the first occurrence of `word` in the prose. */
async function wordBox(page: Page, word: string): Promise<Box> {
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
async function cardBox(page: Page): Promise<Box | null> {
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

/**
 * Every painted state of the peek, recorded in the page.
 *
 * Read back over the bridge, a poll samples a handful of times a second and
 * sees a card that is open, in the right place, and perfectly still. A frame
 * callback sees the twelve times it blinked in the hundred milliseconds after
 * it opened.
 */
async function recordPeek(page: Page): Promise<void> {
  await page.evaluate(() => {
    const w = window as unknown as { __peek?: PeekState[]; __peekFrame?: number }
    w.__peek = []
    let last = ''
    const tick = (): void => {
      const el = document.querySelector('.peek-card-anchor')
      let state: PeekState = { visible: false, left: 0, top: 0, width: 0, height: 0 }
      if (el) {
        const r = el.getBoundingClientRect()
        state = {
          visible: getComputedStyle(el).visibility !== 'hidden',
          left: Math.round(r.left),
          top: Math.round(r.top),
          width: Math.round(r.width),
          height: Math.round(r.height)
        }
      }
      const key = JSON.stringify(state)
      if (key !== last) {
        last = key
        w.__peek!.push(state)
      }
      w.__peekFrame = requestAnimationFrame(tick)
    }
    tick()
  })
}

async function readPeek(page: Page): Promise<PeekState[]> {
  return await page.evaluate(() => {
    const w = window as unknown as { __peek?: PeekState[]; __peekFrame?: number }
    if (w.__peekFrame) cancelAnimationFrame(w.__peekFrame)
    return w.__peek ?? []
  })
}

/** How a recording reads: where the card was drawn, and how often it went away
 *  again after it had been drawn once. */
function summarise(states: PeekState[]): { blanks: number; places: string[] } {
  let drawn = false
  let blanks = 0
  const places = new Set<string>()
  for (const state of states) {
    if (state.visible && state.width > 0) {
      drawn = true
      places.add(`${state.left},${state.top}`)
    } else if (drawn) {
      blanks++
    }
  }
  return { blanks, places: [...places] }
}

/** A project with one character worth drawing a card for, and a scene naming
 *  them. Both halves of this file need it. */
async function seedNamedScene(h: Harness): Promise<Book> {
  const page = h.page
  const book = await seedBook(h, { 'Chapter One': ['Opening', 'Second'] })
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
  return book
}

test('a hovered name keeps its Focus Peek open instead of flickering', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-peek-hover-')
  await seedNamedScene(h)
  const page = h.page

  // ── Hover the name, then drift the way a resting hand does ──
  const word = await wordBox(page, NAME)
  await recordPeek(page)
  await page.mouse.move(centre(word).x, centre(word).y)
  await expect(page.locator('.peek-card-anchor')).toBeVisible({ timeout: 20_000 })

  const box = await cardBox(page)
  expect(box, 'the card should be on screen').not.toBeNull()
  expect(
    overlaps(box!, word),
    'the card must sit clear of the whole name, not merely of the pointer'
  ).toBe(false)

  // Every frame of the first two seconds, with the pointer where the reader put
  // it. A card that throws its contents away and fetches them again blinks
  // empty, which is flicker whether or not it comes back in the same place.
  await page.waitForTimeout(2_000)
  const held = summarise(await readPeek(page))
  expect(held.blanks, 'the card blanked itself while the pointer held still').toBe(0)
  expect(held.places, 'the card moved while the pointer held still').toHaveLength(1)

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

  await h.close()
})

/**
 * The same rule with no room below the name, which is where a clamp used to
 * drag the card back over the very thing it was meant to sit clear of.
 *
 * Skipped on Linux. The window this asks for is 1100x560, and how much of that
 * reaches the page is decided by the window frame: on Windows it is 1084x495
 * and the card goes above the name with room to spare, while under xvfb, which
 * runs no window manager and so draws no decorations, the page gets the whole
 * 1100x560 and the placement lands somewhere else. What the branch does at that
 * exact geometry is not known - it needs a Linux run to answer, and guessing at
 * it from a passing Windows measurement is how this spec came to be written
 * against one platform's chrome in the first place.
 *
 * It is split out rather than skipped wholesale so the flicker invariant above,
 * which does hold on Linux, keeps being checked there.
 */
test('the card sits clear of the name when the window is too short for it', async () => {
  test.skip(
    process.platform === 'linux',
    'the usable viewport at this window size differs without a window manager'
  )
  test.setTimeout(180_000)
  const h = await launchApp('nl-peek-short-')
  await seedNamedScene(h)
  const page = h.page

  await resizeWindow(h, 1100, 560)
  // Let the prose finish reflowing before asking where the word ended up -
  // measuring mid-reflow puts the pointer where the word no longer is, and the
  // peek never opens.
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

/**
 * The same card, raised from an entity row in the inspector.
 *
 * A row asks for the card *beside* it, which resolves to the row's left edge
 * minus the card's own width - so unlike the prose case, where the card is put
 * below the word and its width does not enter into it, here a mismeasured card
 * lands in a completely different place. A card measured while it was still
 * empty was placed 460px to the right of where it belongs: over the row, which
 * is the one thing the placement exists to prevent.
 */
test('a hovered entity row in the inspector holds its Focus Peek still', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-peek-inspector-')
  const book = await seedNamedScene(h)
  const page = h.page
  const chapter = book.chapters[0]

  // The panel lists who is in the scene, which the backend answers from the
  // saved file - so wait for the typing to have reached it rather than guessing
  // at the autosave delay.
  await expect
    .poll(
      async () => {
        const ctx = await h.rpc<{ characters: unknown[] }>('context/analyze', [
          chapter.guid,
          chapter.scenes[0].id
        ])
        return ctx.characters.length
      },
      { timeout: 30_000 }
    )
    .toBeGreaterThan(0)
  // The panel asks that question when the scene opens, and this scene opened
  // before the name was in it. Leave and return.
  await page.locator('.binder-scene-row').nth(1).click()
  await page.locator('.binder-scene-row').first().click()

  const row = page.locator('.ctx-card').first()
  await expect(row).toBeVisible({ timeout: 30_000 })
  const rowBox = await row.boundingBox()
  expect(rowBox, 'the entity row should be on screen').not.toBeNull()

  await recordPeek(page)
  await page.mouse.move(rowBox!.x + rowBox!.width / 2, rowBox!.y + rowBox!.height / 2)
  await expect(page.locator('.peek-card-anchor')).toBeVisible({ timeout: 20_000 })
  await page.waitForTimeout(2_000)

  const states = await readPeek(page)
  const held = summarise(states)
  expect(held.places, 'the card was drawn somewhere').not.toHaveLength(0)
  expect(held.blanks, 'the card blanked itself while the pointer held still').toBe(0)
  expect(held.places, 'the card moved while the pointer held still').toHaveLength(1)

  // Not one painted frame of it may sit on the row it belongs to: the row is
  // host DOM like the card, so a card over it takes the row's own hover.
  const covering = states.filter(
    (s) =>
      s.visible &&
      s.width > 0 &&
      overlaps(
        { x: s.left, y: s.top, width: s.width, height: s.height },
        { x: rowBox!.x, y: rowBox!.y, width: rowBox!.width, height: rowBox!.height }
      )
  )
  expect(covering, 'the card covered the row it is anchored to').toHaveLength(0)

  await h.close()
})
