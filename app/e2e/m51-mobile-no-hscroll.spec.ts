import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * Nothing the phone shell shows may scroll sideways.
 *
 * The single-pane mobile layout scrolls vertically; a view that is even
 * slightly too wide turns the whole screen into something that can be dragged
 * left and right into empty space, which is what Dashboard and Settings did.
 *
 * This drives the REAL MobileShell - NOVALIST_FORCE_MOBILE puts the renderer in
 * the phone layout, and navigation goes through window.__novalistTab and
 * __novalistPlanSelect, the same callbacks the native iOS tab bar invokes. The
 * measured container is .mobile-content, the scroller iOS actually shows. An
 * earlier version of this spec dressed the desktop shell up with the .mobile
 * class instead, and measured a tree the device never renders.
 *
 * The window is resized rather than a container pinned: the responsive rules
 * these views rely on are width media queries, which read the viewport.
 *
 * Sizes cover every device family the app ships on, in both orientations.
 */

type Stop = { name: string; go: () => Promise<void> }

/**
 * Every screen the app ships on, in both orientations. The iOS build declares
 * UIDeviceFamily 1 and 2, so the phone shell is what an iPad runs too - an
 * override written for a 393px phone applies at 1180px unless it says otherwise.
 *
 * `inset` stands in for the safe-area padding a notched iPhone reserves down
 * each side in landscape.
 */
const SIZES = [
  { name: 'iPhone SE portrait', w: 320, h: 568 },
  { name: 'iPhone SE landscape', w: 568, h: 320 },
  { name: 'iPhone portrait', w: 393, h: 852 },
  { name: 'iPhone landscape', w: 852, h: 393, inset: 59 },
  { name: 'iPad mini portrait', w: 768, h: 1024 },
  { name: 'iPad mini landscape', w: 1024, h: 768 },
  { name: 'iPad Air portrait', w: 820, h: 1180 },
  { name: 'iPad Air landscape', w: 1180, h: 820 }
]

test('no mobile view overflows a phone width', async () => {
  test.setTimeout(240_000)
  const h = await launchApp('nl-hscroll-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A', 'Scene B'], 'Chapter Two': ['Scene C'] })
  const page = h.page

  // Two characters with a relationship between them: the entity page's
  // relationship rows are the widest thing on it, so an entity without one
  // would not exercise the row that overflowed.
  const mira = await h.rpc<{ id: string }>('entities/create', ['character', 'Mira Vance'])
  await h.rpc('entities/create', ['character', 'Tomas Vance'])
  await h.rpc('entities/setRelationships', [
    mira.id,
    [{ role: 'Brother', target: 'Tomas Vance', inverseRole: 'Sister' }],
    'character'
  ])
  await page.evaluate(() => window.novalistStores.codex.getState().refresh())

  const tab = (key: string): Promise<void> =>
    page.evaluate((k) => (window as unknown as {
      __novalistTab: (key: string) => void
    }).__novalistTab(k), key)

  // Every screen the phone shell can show, reached the way the tab bar reaches
  // it. Plan is a menu over the current view, so its modes come from the same
  // selection callback the native popover fires.
  const stops: Stop[] = [
    { name: 'dashboard', go: () => tab('dashboard') },
    { name: 'binder', go: () => tab('manuscript') },
    { name: 'codex', go: () => tab('codex') },
    {
      name: 'wiki',
      go: async () => {
        await tab('codex')
        await page.locator('.mobile-segment-btn').nth(1).click()
      }
    },
    {
      // The entity page is a sub-view of the Codex tab, reached by opening an
      // entry. It scrolls in .codex-detail rather than .mobile-content, which is
      // how its relationship rows overflowed unnoticed while the tab root was
      // measured as clean.
      name: 'codex entity',
      go: async () => {
        await tab('codex')
        // The Codex/Wiki toggle is remembered across tab switches, so the Wiki
        // stop above leaves this tab reading rather than editing; put it back or
        // there are no entity rows to open.
        await page.locator('.mobile-segment-btn').first().dispatchEvent('click')
        await page.waitForTimeout(800)
        // Dispatched rather than clicked: the first-run tour overlays the
        // content on a fresh profile and blocks hit-testing. What is under test
        // is the layout the row opens, not how the tap reaches it.
        await page.locator('.codex-row').first().dispatchEvent('click')
        await page.waitForTimeout(400)
        // Fail loudly if the detail never opened, rather than measuring the list.
        expect(await page.locator('.codex-detail').count(), 'entity detail should open').toBe(1)
      }
    },
    { name: 'settings', go: () => tab('settings') },
    { name: 'timeline', go: () => planning(0) },
    { name: 'plotGrid', go: () => planning(1) },
    { name: 'calendar', go: () => planning(2) }
  ]

  function planning(index: number): Promise<void> {
    return page.evaluate((i) => (window as unknown as {
      __novalistPlanSelect: (index: number) => void
    }).__novalistPlanSelect(i), index)
  }

  // iOS scrollbars are overlays and take no layout width. Desktop Chromium's
  // take ~10px out of the scroller, which shrinks clientWidth and reports an
  // overflow the phone does not have.
  await page.addStyleTag({
    content: '.mobile-content::-webkit-scrollbar { width: 0; height: 0; }'
  })

  const failures: string[] = []
  for (const size of SIZES) {
    await h.app.evaluate(async ({ BrowserWindow }, s: { w: number; h: number }) => {
      const win = BrowserWindow.getAllWindows()[0]
      win.setMinimumSize(300, 300)
      win.setBounds({ width: s.w, height: s.h })
    }, { w: size.w, h: size.h })
    // A notched iPhone in landscape reserves ~59pt down each side, which
    // .mobile-content takes as padding from env(safe-area-inset-*). env() is 0
    // in a desktop Chromium, so the inset is applied here instead - without it
    // the widest landscape layouts are measured ~118px roomier than they are.
    await page.evaluate((inset: number) => {
      const id = 'nl-safe-area-probe'
      document.getElementById(id)?.remove()
      if (!inset) return
      const style = document.createElement('style')
      style.id = id
      style.textContent =
        `.mobile-content { padding-left: ${inset}px !important; padding-right: ${inset}px !important; }`
      document.head.appendChild(style)
    }, size.inset ?? 0)
    await page.waitForTimeout(600)

    for (const stop of stops) {
      await stop.go()
      // Views load their data after the switch; measuring straight away would
      // size whatever was still on screen.
      await page.waitForTimeout(1200)
      const result = await page.evaluate(() => {
        const content = document.querySelector('.mobile-content') as HTMLElement | null
        if (!content) return { over: -1, widest: [] as string[] }

        // Containers that scroll sideways because they are meant to: strips of
        // tabs or controls that cannot fit a phone and are scrolled rather than
        // wrapped or hidden.
        const deliberate = (el: HTMLElement): boolean =>
          el.closest(
            '.codex-tabs, .binder-tabs, .binder-scene-filter, .binder-sort-row, .timeline-toolbar, .cm-editor'
          ) !== null ||
          el.tagName === 'INPUT' ||
          el.tagName === 'TEXTAREA'

        const widest: string[] = []
        // Every scroller on screen, not only .mobile-content: a sub-view with
        // its own scroller (the Codex entity page) overflows inside itself and
        // leaves the outer one measuring clean.
        let worst = 0
        for (const el of [content, ...Array.from(content.querySelectorAll<HTMLElement>('*'))]) {
          const cs = getComputedStyle(el)
          if (cs.overflowX !== 'auto' && cs.overflowX !== 'scroll') continue
          if (deliberate(el)) continue
          const over = el.scrollWidth - el.clientWidth
          if (over <= 0) continue
          worst = Math.max(worst, over)
          widest.push(
            `${(el.className?.toString() || el.tagName).slice(0, 55)} scrolls sideways by ${over}px`
          )
        }
        return { over: worst, widest: widest.slice(0, 6) }
      })
      // Fitting is not enough on a big screen: the phone reshapes that make a
      // 320px view fit must not follow the iPad around. Collapsing the Dashboard
      // to one column on an 1180px iPad is the shape that regression takes, and
      // no overflow check would notice it.
      if (stop.name === 'dashboard' && size.w >= 768) {
        const tracks = await page.evaluate(() => {
          const g = document.querySelector('.dashboard-columns') as HTMLElement | null
          return g ? getComputedStyle(g).gridTemplateColumns.split(' ').length : 0
        })
        if (tracks < 2) {
          failures.push(
            `dashboard @ ${size.name} (${size.w}x${size.h}): collapsed to ${tracks} column, a phone layout on a tablet`
          )
        }
      }

      if (result.over === -1) {
        failures.push(`${stop.name} @ ${size.name}: no .mobile-content (phone shell did not render)`)
      } else if (result.over > 0) {
        failures.push(
          `${stop.name} @ ${size.name} (${size.w}x${size.h}): overflows by ${result.over}px\n      ${result.widest.join('\n      ')}`
        )
      }
    }
  }

  expect(failures.join('\n  '), 'mobile views must fit a phone width').toBe('')

  await h.close()
})
