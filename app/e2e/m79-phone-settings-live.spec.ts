import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

/**
 * A phone screen the writer can actually change something on.
 *
 * Two failures met on an iPhone, both of which made Settings look present and
 * inert. Neither is about Settings, which is why they are guarded here rather
 * than left to the drilldown spec:
 *
 *   - A pushed page was captured as rendered content at the moment its row was
 *     tapped, so everything inside it kept the props the view had then. Every
 *     controlled input on an open section was frozen against the state behind
 *     it. Plot Grid's thread checkboxes went the same way.
 *   - The numeric fields clamped on every keystroke, so a two-digit value could
 *     not be typed: with a minimum of 8, the "2" of "22" became 8 before the
 *     second digit arrived. A desktop stepper hid it; a phone keyboard is the
 *     only way in.
 */

test('phone: an open section follows the state behind it', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-live-set-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await resizeWindow(h, 393, 852)
  await dismissTour(page)
  await page.evaluate(() =>
    (window as unknown as { __novalistTab: (k: string) => void }).__novalistTab('settings')
  )
  await page.waitForTimeout(1200)

  await page.locator('.mobile-row', { hasText: 'Editor' }).first().dispatchEvent('click')
  await expect(page.locator('.settings-phone-section')).toBeVisible()
  const fontSize = page.locator('#set-fontsize')
  await expect(fontSize).toBeVisible()

  // Changed from outside the page, the way saving a setting changes it. A page
  // rendered once shows what it was pushed with and never hears about this.
  await page.evaluate(() =>
    window.novalistStores.settings.getState().update('global', { editorFontSize: 29 })
  )
  await expect(fontSize).toHaveValue('29')

  await h.close()
})

test('phone: a two-digit font size can be typed', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-clamp-set-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await resizeWindow(h, 393, 852)
  await dismissTour(page)
  await page.evaluate(() =>
    (window as unknown as { __novalistTab: (k: string) => void }).__novalistTab('settings')
  )
  await page.waitForTimeout(1200)

  await page.locator('.mobile-row', { hasText: 'Editor' }).first().dispatchEvent('click')
  const fontSize = page.locator('#set-fontsize')
  await expect(fontSize).toBeVisible()

  // Key by key, because that is the failure: filling the box in one go always
  // worked, and is not how anybody types.
  await fontSize.fill('')
  await fontSize.pressSequentially('22', { delay: 60 })
  await fontSize.blur()

  await expect(fontSize).toHaveValue('22')
  // And it is the setting now, not just what is in the box.
  await expect
    .poll(async () =>
      page.evaluate(
        () => window.novalistStores.settings.getState().view?.effective.editorFontSize
      )
    )
    .toBe(22)

  await h.close()
})

test('phone: the welcome screen is a phone screen', async () => {
  test.setTimeout(180_000)
  // No project. The mobile shell only covers an open one, so with nothing open
  // the phone fell through to the desktop shell and drew the mode rail across
  // the top of the screen with the welcome card pushed under it.
  const h = await launchApp('nl-welcome-m-', { NOVALIST_FORCE_MOBILE: '1' })
  const page = h.page
  await resizeWindow(h, 393, 852)
  await dismissTour(page)

  await expect(page.locator('.start-screen')).toBeVisible()
  // The frame that holds the safe-area insets, and none of the desktop panes.
  await expect(page.locator('.mobile-shell')).toBeVisible()
  expect(await page.locator('.mode-rail').count(), 'no mode rail on a phone').toBe(0)
  expect(await page.locator('.binder').count(), 'no binder on a phone').toBe(0)

  // Settings is the one screen reachable from here, and with no rail and no tab
  // bar it needs its own way back or the welcome screen is a one-way door.
  await page.locator('.start-open', { hasText: 'Settings' }).first().dispatchEvent('click')
  await expect(page.locator('.settings-phone')).toBeVisible()
  await page.locator('.settings-phone-back').dispatchEvent('click')
  await expect(page.locator('.start-screen')).toBeVisible()

  await h.close()
})

test('phone: a plot thread ticks a scene and shows it', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-live-pg-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A', 'Scene B'] })
  const page = h.page
  await resizeWindow(h, 393, 852)
  await dismissTour(page)
  await h.rpc('plot/createPlotline', ['Mira'])
  // The Plan tab pops a native menu; picking Plot Grid out of it is what the
  // native side calls back with.
  await page.evaluate(() =>
    (window as unknown as { __novalistPlanSelect: (i: number) => void }).__novalistPlanSelect(1)
  )
  await page.waitForTimeout(1200)

  await page.locator('.plotgrid-phone .mobile-row').first().click()
  const boxes = page.locator('.plotgrid-phone-page .plotgrid-phone-check')
  await expect(boxes.first()).toBeVisible()
  await expect(boxes.first()).not.toBeChecked()

  // The toggle replaces the whole grid. A page rendered once at the tap kept
  // every box at the state it had before it, so nothing appeared to happen.
  await boxes.first().click()
  await expect(boxes.first()).toBeChecked()

  await h.close()
})
