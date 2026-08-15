import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

/**
 * A view opens at the top, on both shells.
 *
 * The scroll container belongs to the pane (.main-area) and to the phone shell
 * (.mobile-content), not to the view inside it, so a switch used to leave the
 * new view wherever the last one had been scrolled to - opening Settings from a
 * scrolled Dashboard landed the writer in the middle of it.
 *
 * The scrolling here is real wheel input, not an assignment to scrollTop. A
 * writer scrolls whatever is under the pointer, which is not always the element
 * a test would think to set, and an earlier probe that set scrollTop directly
 * reported the bug as absent.
 *
 * Both destinations have to be long enough to hold the offset: when the new view
 * is shorter, the browser clamps the offset away and the defect hides.
 */

const OFFSET = 1500

async function seedLongProject(h: Awaited<ReturnType<typeof launchApp>>): Promise<void> {
  // Enough chapters that the binder and dashboard scroll well past OFFSET.
  const layout: Record<string, string[]> = {}
  for (let c = 1; c <= 12; c += 1) {
    layout[`Chapter ${c}`] = ['Scene A', 'Scene B', 'Scene C', 'Scene D']
  }
  await seedBook(h, layout)
}

test('desktop: switching views starts at the top', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-scrollreset-d-')
  await seedLongProject(h)
  const page = h.page

  const go = (v: string): Promise<void> =>
    page.evaluate((x) => window.novalistStores.shell.getState().setMainView(x as never), v)
  const offsets = (): Promise<number[]> =>
    page.evaluate(() =>
      Array.from(document.querySelectorAll<HTMLElement>('.main-area')).map((el) => el.scrollTop)
    )

  // Dashboard -> Settings, not the reverse: Settings is the long view, so it can
  // hold the offset. Landing on a view too short for it proves nothing, because
  // the browser clamps the offset away whether or not anything resets it.
  await go('dashboard')
  await page.waitForTimeout(1200)
  await page.mouse.move(700, 500)
  await page.mouse.wheel(0, OFFSET)
  await page.waitForTimeout(500)
  expect((await offsets())[0], 'dashboard should have scrolled').toBeGreaterThan(200)

  await go('settings')
  await page.waitForTimeout(1200)
  expect((await offsets())[0], 'settings should open at the top').toBe(0)

  await h.close()
})

test('mobile: switching tabs starts at the top', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-scrollreset-m-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedLongProject(h)
  const page = h.page
  await resizeWindow(h, 393, 852, [300, 400])
  await dismissTour(page)

  const tab = (k: string): Promise<void> =>
    page.evaluate((key) => (window as unknown as {
      __novalistTab: (k: string) => void
    }).__novalistTab(key), k)
  const offset = (): Promise<number> =>
    page.evaluate(() => (document.querySelector('.mobile-content') as HTMLElement)?.scrollTop ?? -1)

  await tab('settings')
  await page.waitForTimeout(1200)
  await page.mouse.move(196, 500)
  await page.mouse.wheel(0, OFFSET)
  await page.waitForTimeout(500)
  expect(await offset(), 'settings should have scrolled').toBeGreaterThan(200)

  await tab('manuscript')
  await page.waitForTimeout(1200)
  expect(await offset(), 'the chapter list should open at the top').toBe(0)

  await h.close()
})
