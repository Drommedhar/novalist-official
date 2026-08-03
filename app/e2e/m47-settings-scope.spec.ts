import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * A shortcut that writes a setting has to write it where that setting lives.
 *
 * Appearance, Editor and Writing assistance can each be pinned to the project,
 * and once one is, the project's value shadows the app's. The Accessibility
 * section is a set of shortcuts to settings that belong to those sections, so
 * every one of them has to follow the same scope - a shortcut that always
 * writes the app-level value does nothing at all on a project that pins its
 * own, and does it silently: the click is accepted, the file is written, and
 * the screen never changes.
 *
 * Reported from the checklist as "Settings > Accessibility: none of these
 * work", on a project whose appearance was pinned.
 */
test('the High Contrast shortcut works on a project that pins its own appearance', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-a11y-scope-')
  await seedBook(h, { One: ['A'] })

  // Pin appearance to the project, which is what the Settings switch does.
  await h.rpc('settings/pinSection', ['appearance'])
  await h.page.evaluate(async () => {
    const view = await window.novalistRpc.request('settings/get')
    window.novalistStores.settings.setState({ view: view as never })
  })

  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
  await h.page.locator('.settings-nav-item', { hasText: /Accessibility|Barrierefreiheit|无障碍/ })
    .first()
    .click()

  const theme = (): Promise<string | undefined> =>
    h.page.evaluate(() => document.documentElement.dataset.theme)

  expect(await theme()).not.toBe('high-contrast')

  await h.page.getByRole('button', { name: /High Contrast|Hoher Kontrast|高对比度/ }).first().click()

  // The screen actually changes, which is the whole point of the button.
  await expect.poll(theme, { timeout: 15_000 }).toBe('high-contrast')

  // And it changed because the project's own value changed, not the app's -
  // writing the app-level one is exactly the bug this covers.
  const scoped = await h.page.evaluate(() => {
    const v = window.novalistStores.settings.getState().view!
    return {
      effective: v.effective.theme,
      overrides: (v.overrides ?? {})['theme'] ?? null,
      global: v.global['theme'] ?? null
    }
  })
  expect(scoped.effective, 'the effective theme is not the one asked for').toBe('High Contrast')
  expect(scoped.overrides, 'the theme was not written into the project that pins it')
    .toBe('High Contrast')
  expect(scoped.global, 'the app-level theme was changed instead').not.toBe('High Contrast')

  await h.close()
})
