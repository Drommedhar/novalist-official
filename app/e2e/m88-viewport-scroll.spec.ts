import { test, expect } from '@playwright/test'
import { launchApp, resizeWindow, seedBook } from './harness'

/**
 * A 1080p display at 150% scaling leaves roughly 720 device-independent
 * pixels for an application window. Content still has to remain operable at
 * that height: a long dialog must scroll to its action row, and the welcome
 * screen must scroll instead of being clipped by the fixed shell.
 */

test('a long dialog scrolls to its actions in a short desktop window', async () => {
  const h = await launchApp('nl-dialog-scroll-')
  await seedBook(h, { Chapter: ['Scene'] })
  await resizeWindow(h, 1080, 720)

  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
  await h.page.getByRole('button', { name: 'Add Event' }).click()

  const card = h.page.getByRole('dialog', { name: 'Title', exact: true })
  await expect(card).toBeVisible()
  const before = await card.evaluate((element) => {
    const style = getComputedStyle(element)
    const actions = element.querySelector<HTMLElement>('.dialog-actions')!
    return {
      overflowY: style.overflowY,
      clientHeight: element.clientHeight,
      scrollHeight: element.scrollHeight,
      cardBottom: element.getBoundingClientRect().bottom,
      viewportHeight: window.innerHeight,
      actionsBottom: actions.getBoundingClientRect().bottom
    }
  })

  expect(before.overflowY).toBe('auto')
  expect(before.scrollHeight).toBeGreaterThan(before.clientHeight)
  expect(before.cardBottom).toBeLessThanOrEqual(before.viewportHeight)
  expect(before.actionsBottom).toBeGreaterThan(before.cardBottom)

  await card.evaluate((element) => {
    element.scrollTop = element.scrollHeight
  })
  const after = await card.locator('.dialog-actions').evaluate((element) => ({
    top: element.getBoundingClientRect().top,
    bottom: element.getBoundingClientRect().bottom,
    viewportHeight: window.innerHeight
  }))
  expect(after.top).toBeGreaterThanOrEqual(0)
  expect(after.bottom).toBeLessThanOrEqual(after.viewportHeight)

  await h.close()
})

test('the welcome screen becomes a vertical scroller when it is taller than the window', async () => {
  const h = await launchApp('nl-welcome-scroll-')
  await resizeWindow(h, 1080, 520)

  const welcome = h.page.locator('.start-screen')
  await expect(welcome).toBeVisible()
  const before = await welcome.evaluate((element) => ({
    overflowY: getComputedStyle(element).overflowY,
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
    scrollTop: element.scrollTop
  }))
  expect(before.overflowY).toBe('auto')
  expect(before.scrollHeight).toBeGreaterThan(before.clientHeight)
  expect(before.scrollTop).toBe(0)

  await welcome.evaluate((element) => {
    element.scrollTop = element.scrollHeight
  })
  await expect
    .poll(() => welcome.evaluate((element) => element.scrollTop))
    .toBeGreaterThan(0)

  const addNote = h.page.getByRole('button', { name: 'Jot it down' })
  await expect(addNote).toBeInViewport()

  await h.close()
})
