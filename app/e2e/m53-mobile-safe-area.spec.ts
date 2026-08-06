import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * Nothing scrolls into the status bar.
 *
 * The safe-area insets have to sit on the phone shell, not on the scroller
 * inside it. On the scroller they are part of the scrollable region, so content
 * scrolled up through the top inset and appeared in the status-bar strip - and
 * the sticky header could not hide it, because sticky pins to the scrollport's
 * content edge, which is below that padding. On a pushed Settings page the
 * section's intro text was visible behind the clock.
 *
 * env(safe-area-inset-*) is 0 in a desktop Chromium, so the inset is simulated
 * on .mobile-shell. What is asserted is the structural property that made the
 * defect possible: the scroller must start where the shell's padding ends, and
 * a sticky header inside it must sit flush with that edge.
 */

const INSET = 47 // a notched iPhone's top inset, in points

test('mobile: content cannot scroll into the safe area', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-safearea-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A', 'Scene B'] })
  const page = h.page
  await h.app.evaluate(async ({ BrowserWindow }) => {
    const win = BrowserWindow.getAllWindows()[0]
    win.setMinimumSize(300, 300)
    win.setBounds({ width: 393, height: 852 })
  })

  // Injected as the token, not onto a chosen element: the inset then lands
  // wherever the production CSS puts it, which is the thing under test. Setting
  // it directly on .mobile-shell would assert the fix against itself and pass
  // just as happily with the defect in place - it did.
  await page.addStyleTag({
    content: `.shell.mobile { --nl-safe-top: ${INSET}px; }`
  })

  // A pushed Settings section: long enough to scroll, and it carries the sticky
  // back bar the defect showed through.
  await page.evaluate(() => (window as unknown as {
    __novalistTab: (k: string) => void
  }).__novalistTab('settings'))
  await page.waitForTimeout(1400)
  await page.locator('.mobile-row').first().dispatchEvent('click')
  await page.waitForTimeout(1000)
  await expect(page.locator('.mobile-nav-bar')).toBeVisible()

  await page.mouse.move(196, 500)
  await page.mouse.wheel(0, 800)
  await page.waitForTimeout(500)

  const geometry = await page.evaluate(() => {
    const shell = document.querySelector('.mobile-shell') as HTMLElement
    const content = document.querySelector('.mobile-content') as HTMLElement
    const bar = document.querySelector('.mobile-nav-bar') as HTMLElement
    return {
      contentPaddingTop: parseFloat(getComputedStyle(content).paddingTop),
      shellPaddingTop: parseFloat(getComputedStyle(shell).paddingTop),
      contentTop: Math.round(content.getBoundingClientRect().top),
      barTop: Math.round(bar.getBoundingClientRect().top)
    }
  })

  // The inset is the frame's, so the scroller carries none of it itself.
  expect(geometry.contentPaddingTop, 'the scroller must not pad its own top').toBe(0)
  expect(geometry.shellPaddingTop, 'the shell carries the inset').toBe(INSET)
  // And the sticky header covers the very top of the scroller, so no scrolled
  // content can show above it.
  expect(
    Math.abs(geometry.barTop - geometry.contentTop),
    'the sticky header must sit flush with the top of the scroller'
  ).toBeLessThanOrEqual(1)

  await h.close()
})

/**
 * A sticky heading sits flush with the top of whatever scrolls it.
 *
 * This is the same defect as the safe-area one above, in a second place: a
 * scroller's padding is part of the scrollable region, and a `position: sticky`
 * child pins to the content edge BELOW it. The timeline's month headings were
 * pinned 16px down, and the previous month's last event slid through the strip
 * above them, clipped and half-visible.
 *
 * Stated generally because it has now happened twice: for every sticky element
 * on the phone, the box it sticks in must not pad the side it sticks to.
 */
test('mobile: sticky headings are flush with their scroller', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-sticky-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A', 'Scene B'], 'Chapter Two': ['Scene C'] })
  const page = h.page
  await h.app.evaluate(async ({ BrowserWindow }) => {
    const win = BrowserWindow.getAllWindows()[0]
    win.setMinimumSize(300, 300)
    win.setBounds({ width: 393, height: 852 })
  })
  // Events months apart, so the timeline renders more than one sticky heading.
  // Created before the view opens, and asserted below - a timeline with no
  // headings would let this test pass without checking anything.
  for (const [title, date] of [
    ['The lighthouse', '1847-10-19'],
    ['A letter arrives', '1848-03-09'],
    ['Low tide', '1848-04-02']
  ] as const) {
    await h.rpc('timeline/saveEvent', [
      null, title, date, '', null, null, [], [], null, [], null, null
    ])
  }
  await page.evaluate(() => (window as unknown as {
    __novalistPlanSelect: (i: number) => void
  }).__novalistPlanSelect(0))
  await page.waitForTimeout(1800)

  // Without headings there is nothing to measure and the check would pass
  // vacuously - which is exactly how the first version of this test fooled me.
  expect(
    await page.locator('.timeline-group-label').count(),
    'the timeline must render sticky headings for this to mean anything'
  ).toBeGreaterThan(1)

  const offenders = await page.evaluate(() => {
    const bad: string[] = []
    for (const el of Array.from(document.querySelectorAll<HTMLElement>('*'))) {
      const cs = getComputedStyle(el)
      if (cs.position !== 'sticky' || cs.top === 'auto') continue
      // The box it sticks inside: the nearest scrolling ancestor.
      let sc: HTMLElement | null = el.parentElement
      while (sc) {
        const s = getComputedStyle(sc)
        if (s.overflowY === 'auto' || s.overflowY === 'scroll') break
        sc = sc.parentElement
      }
      if (!sc) continue
      const pad = parseFloat(getComputedStyle(sc).paddingTop) || 0
      const offset = parseFloat(cs.top) || 0
      // Padding above the point it sticks to opens a strip nothing can cover.
      if (pad > offset + 1) {
        bad.push(
          `${(el.className?.toString() || el.tagName).slice(0, 40)} sticks at top:${offset}px inside ` +
            `${(sc.className?.toString() || sc.tagName).slice(0, 40)} which pads ${pad}px above it`
        )
      }
    }
    return bad
  })

  expect(offenders.join('\n  '), 'sticky headings must pin flush').toBe('')
  await h.close()
})
