import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

/**
 * Every view says what it is for, the first time it is opened.
 *
 * The walkthrough visits seven views. Novalist has twenty-two, so most of them
 * a writer meets cold - and a screen met cold has to be worked out from its own
 * controls, which is the complaint the whole interface restructure started
 * from.
 *
 * What is asserted is the shape of the promise rather than the words: it
 * appears once per view, closing it is final for that view and touches no
 * other, it survives a restart, and switching the guidance off in Settings
 * takes every one of them away.
 */

const intro = (page: import('@playwright/test').Page) => page.locator('.view-intro')

test('a view introduces itself once, and only once', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-view-intro-')
  await seedBook(h, { 'Chapter One': ['Opening'] })
  const page = h.page
  await dismissTour(page)
  await enterWriting(page)

  // Every core view has copy: the table is exhaustive by type, so this is
  // really a check that nothing renders a raw key.
  await page.locator('.mode-rail-item[data-mode="world"]').click()
  await expect(intro(page)).toBeVisible({ timeout: 15_000 })
  await expect(intro(page)).toHaveAttribute('data-view-intro', 'codex')
  await expect(intro(page)).not.toContainText('intro.')

  // Closing is final for that view...
  await page.locator('.view-intro-close').click()
  await expect(intro(page)).toHaveCount(0)
  await page.locator('.mode-panel-row[data-view="codex"]').click()
  await expect(intro(page)).toHaveCount(0)

  // ...and touches no other view.
  await page.locator('.mode-panel-row[data-view="maps"]').click()
  await expect(intro(page)).toBeVisible({ timeout: 10_000 })
  await expect(intro(page)).toHaveAttribute('data-view-intro', 'maps')

  // It is remembered, or it is not "once" - it is "once per session", which is
  // a good deal worse than never showing it at all.
  const stored = await page.evaluate(() => localStorage.getItem('nl.onboarding'))
  expect(stored).toContain('viewIntros')
  expect(stored).toContain('codex')

  // One switch turns the whole system off, the same one the contextual tips
  // answer to - Settings, Accessibility, "Show contextual guidance while I
  // learn". Guidance you cannot stop is not guidance.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
  await expect(intro(page)).toBeVisible({ timeout: 10_000 })
  await page.evaluate(() =>
    window.novalistStores.onboarding.getState().setTipsEnabled(false)
  )
  await expect(intro(page)).toHaveCount(0)
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
  await page.waitForTimeout(500)
  await expect(intro(page)).toHaveCount(0)

  await h.close()
})

test('the walkthrough opens by saying where everything is', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-tour-modes-')
  await seedBook(h, { 'Chapter One': ['Opening'] })
  const page = h.page

  // The tour is offered unasked on a first run, and its first stop is the one
  // thing a returning writer will not recognise: the five workspaces.
  await expect(page.locator('.tour-card')).toBeVisible({ timeout: 20_000 })
  await expect(page.locator('.tour-card')).toContainText('Five workspaces')
  // And while it is running it is the only explanation on screen - a stop
  // describing a view and that view's own introduction at once is worse than
  // either alone.
  await expect(intro(page)).toHaveCount(0)

  await h.close()
})
