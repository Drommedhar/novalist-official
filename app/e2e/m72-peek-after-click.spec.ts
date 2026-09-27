import { test, expect, type Page } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook, type Harness } from './harness'

/**
 * Clicking a name is not a way of switching its Focus Peek off.
 *
 * The editor only tells the host about a hover when the name under the pointer
 * *changes* - otherwise every mouse move over a long name would be a message.
 * But the host hides the card on a click, and the editor was not told, so its
 * idea of what the host already knew went stale: the next move inside the same
 * name read as "still the same name, nothing to say" and no card came back.
 *
 * The writer's way into this is the ordinary one. Put a name in with `@`, or
 * click into one already in the prose to fix a typo, then move the mouse a
 * little - which is what you do when you want to see who they were. Nothing.
 * The only way out was to move the pointer off the word entirely and return,
 * which is not a thing anyone does deliberately.
 *
 * Both kinds of reference are checked because they are two code paths that made
 * the same assumption: an explicit `@` mention, which carries an entity id, and
 * a plain name in the prose, which is matched by text.
 */

const NAME = 'Mira Vance'

const peek = (page: Page): ReturnType<Page['locator']> => page.locator('.peek-card-anchor')

type Box = { x: number; y: number; width: number; height: number }

/** Follow the name while pagination and editor layout settle after typing. */
async function hover(page: Page, measure: () => Promise<Box>): Promise<Box> {
  await expect(page.frameLocator('.editor-frame').locator('#editor > .nv-page')).toHaveCount(1)
  let box!: Box
  await expect.poll(async () => {
    // A card anchored before reflow can cover the name's new position. Leave
    // it first so the next move reaches the editor instead of the old card.
    await page.mouse.move(0, 0)
    await expect(peek(page)).toHaveCount(0)
    box = await measure()
    const x = box.x + box.width / 2
    const y = box.y + box.height / 2
    await page.mouse.move(x, y)
    await page.waitForTimeout(200)
    await page.mouse.move(x + 2, y)
    // A visible card alone is insufficient: Chromium can hit-test the nearest
    // name even when pagination moved it away from the old pointer position.
    const current = await measure()
    const stable = (['x', 'y', 'width', 'height'] as const)
      .every((key) => Math.abs(current[key] - box[key]) < 1)
    return stable && await peek(page).isVisible()
  }, { timeout: 20_000 }).toBe(true)
  return box
}

async function seedNamedScene(h: Harness): Promise<void> {
  const page = h.page
  await seedBook(h, { 'Chapter One': ['Opening'] })
  await dismissTour(page)
  await enterWriting(page)

  const created = await h.rpc<{ id: string }>('entities/create', ['character', NAME])
  await h.rpc('entities/update', [
    'character',
    created.id,
    {
      description:
        'Harbourmaster of Hillsford, keeper of the tide tables, and the only '
        + 'person on either pier who still remembers what the water did the '
        + 'winter the second jetty went in.'
    }
  ])
  await page.evaluate(() => window.novalistStores.codex.getState().refresh())

  await page.locator('.binder-scene-row').first().click()
  await expect(page.frameLocator('.editor-frame').locator('#editor')).toBeVisible({
    timeout: 30_000
  })
}

test('a mention put in with @ still peeks after it has been clicked', async () => {
  test.setTimeout(240_000)
  const h = await launchApp('nl-peek-click-')
  await seedNamedScene(h)
  const page = h.page

  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await editor.click()
  await page.keyboard.type('The door opened and ')
  await page.keyboard.type('@Mira', { delay: 60 })
  await expect(page.frameLocator('.editor-frame').locator('#mention-picker')).toContainText(NAME, {
    timeout: 15_000
  })
  await page.keyboard.press('Enter')

  const mention = page.frameLocator('.editor-frame').locator('.nv-entity-mention').first()
  await expect(mention).toBeVisible({ timeout: 15_000 })
  const box = await hover(page, async () => (await mention.boundingBox())!)

  // A click is how a writer puts the caret in a name they are about to edit.
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
  await expect(peek(page)).toHaveCount(0, { timeout: 10_000 })

  // Moving inside the same name has to bring it back. This was the dead state:
  // the pointer never left the word, so nothing announced it again.
  await page.mouse.move(box.x + box.width / 2 + 4, box.y + box.height / 2)
  await page.waitForTimeout(150)
  await page.mouse.move(box.x + box.width / 2 + 6, box.y + box.height / 2)
  await expect(peek(page)).toBeVisible({ timeout: 20_000 })

  await h.close()
})

test('a plain name in the prose still peeks after it has been clicked', async () => {
  test.setTimeout(240_000)
  const h = await launchApp('nl-peek-click2-')
  await seedNamedScene(h)
  const page = h.page

  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await editor.click()
  await page.keyboard.type(`${NAME} stopped in the doorway and waited.`)
  await expect
    .poll(async () => (await editor.innerText()).includes(NAME), { timeout: 15_000 })
    .toBe(true)

  // Re-measure both the text and its frame on each initial hover attempt.
  const box = await hover(page, async () => {
    const local = await editor.evaluate((el, needle: string) => {
      const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT)
      let node = walker.nextNode()
      while (node) {
        const at = (node.textContent ?? '').indexOf(needle)
        if (at >= 0) {
          const range = document.createRange()
          range.setStart(node, at)
          range.setEnd(node, at + needle.length)
          // A name can wrap; hover a text fragment rather than the gap between lines.
          const r = range.getClientRects()[0]
          return { x: r.x, y: r.y, width: r.width, height: r.height }
        }
        node = walker.nextNode()
      }
      throw new Error('the name is not in the prose')
    }, NAME)
    const frame = (await page.locator('.editor-frame').boundingBox())!
    return { ...local, x: frame.x + local.x, y: frame.y + local.y }
  })

  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2)
  await expect(peek(page)).toHaveCount(0, { timeout: 10_000 })

  await page.mouse.move(box.x + box.width / 2 + 4, box.y + box.height / 2)
  await page.waitForTimeout(150)
  await page.mouse.move(box.x + box.width / 2 + 6, box.y + box.height / 2)
  await expect(peek(page)).toBeVisible({ timeout: 20_000 })

  await h.close()
})
