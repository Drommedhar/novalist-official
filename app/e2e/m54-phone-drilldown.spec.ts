import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

/**
 * The phone shows one thing at a time.
 *
 * Settings and the Codex entry both carry more editors than fit a 393px screen,
 * so on a phone each is an index of grouped rows that opens one at a time and
 * comes back (MobileNav). What this guards is the drill-down itself: the rows
 * exist, tapping one opens that page and nothing else, and the back bar returns
 * to the index rather than leaving the writer stranded a level down.
 *
 * The width bound matters as much as the behaviour: an iPad is wide enough for
 * the full layouts, so the index must NOT appear there. That regression - phone
 * reshapes following the app onto a tablet - is the one this file is really
 * about, because it looks like nothing is wrong.
 */

test('phone: Settings opens one section at a time', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-drill-s-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await resizeWindow(h, 393, 852)
  await page.evaluate(() => (window as unknown as {
    __novalistTab: (k: string) => void
  }).__novalistTab('settings'))
  await page.waitForTimeout(1400)

  // The index: grouped rows, one per section, and no section body on screen.
  // Not the action row above them - closing the project acts where it stands
  // rather than opening anything, which is why it carries no chevron.
  const rows = page.locator('.mobile-row:not(.mobile-row-action)')
  expect(await rows.count(), 'every section is a row').toBeGreaterThan(8)
  expect(await page.locator('.mobile-group-header').count(), 'rows are grouped').toBeGreaterThan(1)
  expect(await page.locator('.settings-phone-section').count(), 'no section is open yet').toBe(0)

  const firstLabel = await rows.first().locator('.mobile-row-label').textContent()
  await rows.first().dispatchEvent('click')
  await page.waitForTimeout(800)

  // One section, titled by the row that opened it, with a way back.
  await expect(page.locator('.mobile-nav-bar')).toBeVisible()
  expect(await page.locator('.settings-phone-section').count(), 'exactly one section opens').toBe(1)
  expect(await page.locator('.mobile-nav-title').textContent()).toBe(firstLabel)

  await page.locator('.mobile-nav-back').dispatchEvent('click')
  await page.waitForTimeout(600)
  expect(await page.locator('.settings-phone-section').count(), 'back returns to the index').toBe(0)
  expect(await rows.count(), 'the index is intact').toBeGreaterThan(8)

  await h.close()
})

test('phone: a Codex entry keeps its editors behind rows', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-drill-c-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await h.rpc('entities/create', ['character', 'Mira Vance'])
  await page.evaluate(() => window.novalistStores.codex.getState().refresh())
  await resizeWindow(h, 393, 852)
  await page.evaluate(() => (window as unknown as {
    __novalistTab: (k: string) => void
  }).__novalistTab('codex'))
  await page.waitForTimeout(1200)
  await page.locator('.codex-row').first().dispatchEvent('click')
  await page.waitForTimeout(1200)

  // The fields stay on the page; the editors are rows below them.
  await expect(page.locator('.codex-detail')).toBeVisible()
  expect(await page.locator('.codex-phone-sections').count(), 'editors are behind rows').toBe(1)

  await page.locator('.codex-phone-sections .mobile-row').first().dispatchEvent('click')
  await page.waitForTimeout(800)
  expect(await page.locator('.codex-phone-page').count(), 'one editor opens').toBe(1)

  await page.locator('.mobile-nav-back').dispatchEvent('click')
  await page.waitForTimeout(600)
  expect(await page.locator('.codex-phone-page').count(), 'back returns to the entry').toBe(0)
  await expect(page.locator('.codex-detail')).toBeVisible()

  await h.close()
})

test('tablet: the phone index does not follow the app onto an iPad', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-drill-t-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await resizeWindow(h, 1180, 820)
  await page.evaluate(() => (window as unknown as {
    __novalistTab: (k: string) => void
  }).__novalistTab('settings'))
  await page.waitForTimeout(1400)

  expect(await page.locator('.settings-phone').count(), 'an iPad gets the full layout').toBe(0)
  expect(await page.locator('.settings-layout').count(), 'the two-column layout stands').toBe(1)

  await h.close()
})

/**
 * The iPad shell itself, not merely the absence of the phone one.
 *
 * The case above passes at iPad width while still rendering MobileShell, because
 * without a size class to report the layout stays 'phone' - so for a full release
 * cycle "the iPad is fine" was asserted by a test that never built the two-pane
 * tree at all. NOVALIST_FORCE_TABLET supplies the size class the simulator would,
 * and this asserts the panes a writer is actually asking for: a binder standing
 * beside the routed main view, both on screen at once.
 */
test('tablet: an iPad gets the binder and the main view side by side', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-tablet-', {
    NOVALIST_FORCE_MOBILE: '1',
    NOVALIST_FORCE_TABLET: '1'
  })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await resizeWindow(h, 1180, 820)
  await page.waitForTimeout(1400)

  await expect(page.locator('.tablet-shell')).toBeVisible()
  expect(await page.locator('.mobile-shell').count(), 'the phone shell stands down').toBe(0)

  const binder = page.locator('.tablet-binder')
  const main = page.locator('.tablet-main')
  await expect(binder).toBeVisible()
  await expect(main).toBeVisible()

  // Side by side, not stacked: the binder ends where the main view begins.
  const b = await binder.boundingBox()
  const m = await main.boundingBox()
  expect(b, 'the binder is laid out').not.toBeNull()
  expect(m, 'the main view is laid out').not.toBeNull()
  expect(b!.x + b!.width, 'the binder sits left of the main view').toBeLessThanOrEqual(m!.x + 1)
  expect(m!.width, 'the main view keeps a usable column').toBeGreaterThan(400)

  await h.close()
})

/**
 * A push starts at the top of the new page; a pop returns to where the writer
 * was on the old one.
 *
 * The tab's scroller outlives every page shown in it, so without this a push
 * inherited whatever offset the previous page happened to be at, and a pop put
 * the writer back at the top. On a Codex entry that is the worst case: the rows
 * sit below the fields, so every editor cost a scroll back down to reach the
 * next one.
 */
test('phone: back returns to where the entry was scrolled to', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-drill-scroll-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await h.rpc('entities/create', ['character', 'Mira Vance'])
  await page.evaluate(() => window.novalistStores.codex.getState().refresh())
  await resizeWindow(h, 393, 852)
  await dismissTour(page)
  await page.evaluate(() => (window as unknown as {
    __novalistTab: (k: string) => void
  }).__novalistTab('codex'))
  await page.waitForTimeout(1200)
  await page.locator('.codex-row').first().dispatchEvent('click')
  await page.waitForTimeout(1200)

  // The Codex entry scrolls inside .codex-detail, not in the tab's scroller -
  // which is exactly the trap the fix had to handle, so the test reads whatever
  // is actually scrolled rather than assuming.
  const offset = (): Promise<number> =>
    page.evaluate(() => {
      const scrolled = Array.from(document.querySelectorAll<HTMLElement>('*')).find(
        (el) => el.scrollTop > 0
      )
      return scrolled ? Math.round(scrolled.scrollTop) : 0
    })

  // Scroll down the entry, the way a writer reaching the editor rows does.
  await page.locator('.codex-detail').hover()
  await page.mouse.wheel(0, 700)
  await page.waitForTimeout(600)
  const before = await offset()
  expect(before, 'the entry should have scrolled').toBeGreaterThan(100)

  await page.locator('.codex-phone-sections .mobile-row').first().dispatchEvent('click')
  await page.waitForTimeout(800)
  expect(await offset(), 'the opened editor starts at its top').toBe(0)

  await page.locator('.mobile-nav-back').dispatchEvent('click')
  await page.waitForTimeout(800)
  expect(
    Math.abs((await offset()) - before),
    'back returns to where the entry was'
  ).toBeLessThanOrEqual(2)

  await h.close()
})

/**
 * A view's chrome does not crowd out the view.
 *
 * The timeline stacked a five-picker filter bar on a toolbar of a dozen setup
 * controls: about 790px of an 852px phone before the first event, with the
 * toolbar cut off at the edge on top of that. Both fold to a single row, and
 * the primary action stays out where it can be reached.
 *
 * A filter that is ON must never hide behind a folded row, so the bar shows
 * itself whenever something is narrowing the view.
 */
test('phone: the timeline opens with its chrome folded', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-chrome-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A', 'Scene B'] })
  const page = h.page
  for (const [title, date] of [
    ['The lighthouse', '1847-10-19'],
    ['A letter arrives', '1848-03-09']
  ] as const) {
    await h.rpc('timeline/saveEvent', [null, title, date, '', null, null, [], [], null, [], null, null])
  }
  await resizeWindow(h, 393, 852)
  await page.evaluate(() => (window as unknown as {
    __novalistPlanSelect: (i: number) => void
  }).__novalistPlanSelect(0))
  await page.waitForTimeout(1800)

  const chromeHeight = (): Promise<number> =>
    page.evaluate(() => {
      const body = document.querySelector('.timeline-body') as HTMLElement | null
      return body ? Math.round(body.getBoundingClientRect().top) : -1
    })

  // Folded: the filter bar is a row, the toolbar keeps only its primary action.
  expect(await page.locator('.filter-bar').count(), 'the filter bar starts folded').toBe(0)
  expect(await page.locator('.filter-bar-toggle').count(), 'and leaves a row behind').toBe(1)
  const folded = await chromeHeight()
  expect(folded, 'chrome must leave the events most of the screen').toBeLessThan(220)

  // Nothing is cut off horizontally - a control scrolled out of sight reads as
  // a missing button, which is what the toolbar did before it wrapped.
  const cut = await page.evaluate(() => {
    const tb = document.querySelector('.timeline-toolbar') as HTMLElement | null
    return tb ? tb.scrollWidth - tb.clientWidth : -1
  })
  expect(cut, 'the toolbar must not be cut off').toBeLessThanOrEqual(0)

  // Unfolding gives the controls back.
  await page.locator('.filter-bar-toggle').dispatchEvent('click')
  await page.waitForTimeout(600)
  expect(await page.locator('.filter-bar').count(), 'the bar opens').toBe(1)
  expect(await chromeHeight(), 'and takes room when it does').toBeGreaterThan(folded)

  await h.close()
})
