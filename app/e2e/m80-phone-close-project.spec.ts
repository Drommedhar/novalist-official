import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

/**
 * The way back out of a project on iOS.
 *
 * Desktop closes a project from the menu bar or the command palette. The phone
 * has neither: it navigates by a five-item tab bar that only moves between
 * screens inside the open project, so once one was open the welcome screen -
 * and with it every other project - could not be reached again short of
 * restarting the app. Settings is the one destination both the phone and the
 * iPad carry, so the door lives there.
 */
test('phone: a project can be closed from Settings', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-close-proj-', { NOVALIST_FORCE_MOBILE: '1' })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await resizeWindow(h, 393, 852)
  await dismissTour(page)
  await page.evaluate(() =>
    (window as unknown as { __novalistTab: (k: string) => void }).__novalistTab('settings')
  )
  await page.waitForTimeout(1200)

  // The group states what is open and offers the one action on it - at the top
  // of the index, not behind a row that opens a screen holding one button.
  const close = page.locator('.settings-phone .mobile-row-action', { hasText: 'Close project' })
  await expect(close).toBeVisible()
  await close.dispatchEvent('click')

  // Closing is also leaving Settings: the welcome content only stands when the
  // view is not an app-scoped one, and pressing this button means the view is
  // Settings.
  await expect(page.locator('.start-screen')).toBeVisible()
  expect(await page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(false)

  await h.close()
})

/**
 * The iPad has the same problem and the same door.
 *
 * Its sidebar lists destinations inside the project and nothing above them, so
 * Settings is where the way out has to be there too - as a section in the
 * two-column layout rather than a row in the phone index, because that is the
 * layout an iPad gets.
 */
test('tablet: a project can be closed from Settings', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-close-proj-t-', {
    NOVALIST_FORCE_MOBILE: '1',
    NOVALIST_FORCE_TABLET: '1'
  })
  await seedBook(h, { 'Chapter One': ['Scene A'] })
  const page = h.page
  await resizeWindow(h, 1180, 820)
  await dismissTour(page)
  await page.evaluate(() =>
    (window as unknown as { __novalistTab: (k: string) => void }).__novalistTab('settings')
  )
  await page.waitForTimeout(1200)

  await page.locator('.settings-nav-item', { hasText: 'Project' }).first().dispatchEvent('click')
  const section = page.locator('[data-settings-section="project"]')
  await expect(section).toBeVisible()
  // It names what it would close, so the button is not an unlabelled leap.
  await expect(section).toContainText('Spec')

  await section.locator('.dialog-button', { hasText: 'Close project' }).dispatchEvent('click')
  await expect(page.locator('.start-screen')).toBeVisible()
  expect(await page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(false)

  await h.close()
})
