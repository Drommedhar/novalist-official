import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

/**
 * A character is recognised by their surname too.
 *
 * Only the composed display name and the bare given name were ever matched, so
 * "Mira" lit up in the prose and "Frost" did not - and a character is called by
 * their surname by everyone who does not know them well, which in most books is
 * most people. Half the mentions of a name went unrecognised.
 *
 * Two characters sharing a surname make it ambiguous, and an ambiguous text is
 * dropped rather than pointed at one of them arbitrarily. That rule already
 * governs every other matched text, and the second half here checks the
 * surname is not an exception to it - which is the "rare cases" the writer who
 * asked for this was willing to live with, and it costs them nothing.
 */

/**
 * Whether the editor treats `word` in the prose as a name it knows.
 *
 * Plain names are not wrapped in the DOM - they are found by hit-testing the
 * prose - so the only honest way to ask is to hover one and see whether the
 * card the writer would see comes up.
 */
async function isRecognised(
  page: import('@playwright/test').Page,
  word: string
): Promise<boolean> {
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  const frameBox = (await page.locator('.editor-frame').boundingBox())!
  const local = await editor.evaluate((el, needle: string) => {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT)
    let node = walker.nextNode()
    while (node) {
      const at = (node.textContent ?? '').indexOf(needle)
      if (at >= 0) {
        const range = document.createRange()
        range.setStart(node, at)
        range.setEnd(node, at + needle.length)
        const r = range.getBoundingClientRect()
        return { x: r.x, y: r.y, width: r.width, height: r.height }
      }
      node = walker.nextNode()
    }
    return null
  }, word)
  if (!local) throw new Error(`"${word}" is not in the prose`)

  const x = frameBox.x + local.x + local.width / 2
  const y = frameBox.y + local.y + local.height / 2
  // Away first, so a card left over from the previous word cannot be mistaken
  // for this one's.
  await page.mouse.move(x, y + 160)
  await page.waitForTimeout(400)
  await page.mouse.move(x, y)
  await page.waitForTimeout(150)
  await page.mouse.move(x + 2, y)
  return page
    .locator('.peek-card-anchor')
    .waitFor({ state: 'visible', timeout: 6_000 })
    .then(() => true)
    .catch(() => false)
}

test('a surname is recognised in the prose', async () => {
  test.setTimeout(240_000)
  const h = await launchApp('nl-surname-')
  const page = h.page

  await seedBook(h, { One: ['A'] })
  await dismissTour(page)
  await enterWriting(page)

  const mira = await h.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
  await h.rpc('entities/update', ['character', mira.id, { surname: 'Frost' }])
  await page.evaluate(() => window.novalistStores.codex.getState().refresh())

  // The backend offers the surname as its own target, which is what the editor
  // is handed to look for.
  const listed = await page.evaluate(
    async () =>
      (await window.novalistRpc.request('entities/list', ['character'])) as {
        name: string
        firstName: string | null
        surname: string | null
      }[]
  )
  expect(listed[0].name).toBe('Mira Frost')
  expect(listed[0].firstName).toBe('Mira')
  expect(listed[0].surname).toBe('Frost')

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.type('Frost put the kettle on. Nobody called her Mira any more.')
  await expect(editor).toContainText('kettle', { timeout: 15_000 })

  // Both halves of the name, not just the one her friends use.
  expect(await isRecognised(page, 'Mira')).toBe(true)
  expect(await isRecognised(page, 'Frost')).toBe(true)

  await h.close()
})

test('a surname two characters share is left alone rather than guessed at', async () => {
  test.setTimeout(240_000)
  const h = await launchApp('nl-surname2-')
  const page = h.page

  await seedBook(h, { One: ['A'] })
  await dismissTour(page)
  await enterWriting(page)

  for (const given of ['Mira', 'Tomas']) {
    const made = await h.rpc<{ id: string }>('entities/create', ['character', given])
    await h.rpc('entities/update', ['character', made.id, { surname: 'Frost' }])
  }
  await page.evaluate(() => window.novalistStores.codex.getState().refresh())

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await editor.click()
  await page.keyboard.type('Frost put the kettle on, and Mira watched.')
  await expect(editor).toContainText('kettle', { timeout: 15_000 })

  // The given name still resolves; the shared surname points at nobody, which
  // is better than pointing at whichever entry happened to be loaded first.
  expect(await isRecognised(page, 'Mira')).toBe(true)
  expect(await isRecognised(page, 'Frost')).toBe(false)

  await h.close()
})
