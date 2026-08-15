import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

test('Settings shows one categorized section and finds translated controls', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-settings-nav-')
  await seedBook(h, { One: ['A'] })

  await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings())
  const surface = h.page.locator('.settings-section-surface')
  await expect(surface).toHaveAttribute('data-settings-section', 'appearance')
  await expect(surface).toHaveCount(1)
  await expect(h.page.locator('#set-theme')).toBeVisible()
  await expect(h.page.locator('#set-font')).toHaveCount(0)

  const groupHeadings = h.page.locator('.settings-nav-heading')
  await expect(groupHeadings).toHaveCount(4)
  await expect(groupHeadings).toHaveText(['General', 'Writing', 'Project', 'System'])
  await expect(h.page.locator('.settings-scope-badge')).toHaveText('Global default')
  const search = h.page.getByRole('searchbox', { name: 'Search settings...' })

  // Interface size is live and explicitly separate from manuscript font size.
  // Reset is part of the same control, so browser zoom cannot become hidden
  // state that only a keyboard shortcut can undo.
  await h.page.selectOption('#set-ui-scale', '125')
  await expect
    .poll(() =>
      h.app.evaluate(({ BrowserWindow }) =>
        BrowserWindow.getAllWindows()[0].webContents.getZoomFactor()
      )
    )
    .toBeCloseTo(1.25, 2)
  await h.page.getByRole('button', { name: 'Reset to 100%' }).click()
  await expect
    .poll(() =>
      h.app.evaluate(({ BrowserWindow }) =>
        BrowserWindow.getAllWindows()[0].webContents.getZoomFactor()
      )
    )
    .toBeCloseTo(1, 2)

  await h.page.locator('.settings-nav-item', { hasText: 'Accessibility' }).click()
  const contextualTips = h.page.locator('#set-contextual-tips')
  await expect(contextualTips).toBeChecked()
  await contextualTips.uncheck()
  expect(
    await h.page.evaluate(() =>
      JSON.parse(localStorage.getItem('nl.onboarding') ?? '{}').tipsEnabled
    )
  ).toBe(false)
  await search.fill('contextual guidance')
  await expect(
    h.page.locator('.settings-result-title', {
      hasText: 'Show contextual guidance while I learn'
    })
  ).toBeVisible()
  await search.fill('')

  // Search returns controls, not only their parent section. Choosing a result
  // mounts that one section and focuses the stable destination target.
  await search.fill('letter spacing')
  const letterSpacing = h.page.locator('.settings-result', {
    has: h.page.locator('.settings-result-title', { hasText: 'Letter spacing' })
  })
  await expect(letterSpacing).toBeVisible()
  await letterSpacing.click()
  await expect(surface).toHaveAttribute('data-settings-section', 'editor')
  await expect(h.page.locator('#set-letterspacing')).toBeFocused()
  await expect(h.page.locator('#set-theme')).toHaveCount(0)

  // The same index is built from translated labels and descriptions, not a
  // second English-only keyword list.
  await h.page.locator('.settings-nav-item', { hasText: 'Appearance' }).click()
  await h.page.evaluate(() =>
    window.novalistStores.settings.getState().update('global', { language: 'de' })
  )
  await expect(h.page.getByRole('searchbox', { name: 'Einstellungen durchsuchen...' }))
    .toBeVisible()
  await h.page
    .getByRole('searchbox', { name: 'Einstellungen durchsuchen...' })
    .fill('Zeichenabstand')
  await expect(
    h.page.locator('.settings-result-title', { hasText: 'Zeichenabstand' })
  ).toBeVisible()

  await h.close()
})

test('Settings accepts exact control routes and folds long operating-system help', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-settings-route-')
  await seedBook(h, { One: ['A'] })

  // Compatibility bridge used by shell callers while the typed destination
  // adapter is integrated: section and control are stable, untranslated keys.
  await h.page.evaluate(() =>
    window.novalistStores.shell.getState().openSettings('settings/editor/font-size')
  )
  await expect(h.page.locator('.settings-section-surface')).toHaveAttribute(
    'data-settings-section',
    'editor'
  )
  await expect(h.page.locator('#set-fontsize')).toBeFocused()

  // The Windows installation explanation used to occupy the page at all times
  // and even showed Markdown markers as literal text. It is now optional help.
  const disclosure = h.page.locator('.settings-help-disclosure')
  await expect(disclosure.locator('summary')).toBeVisible()
  await expect(disclosure.locator('.settings-help-copy')).not.toBeVisible()
  await disclosure.locator('summary').click()
  await expect(disclosure.locator('.settings-help-copy')).toBeVisible()
  await expect(disclosure.locator('strong')).not.toHaveCount(0)
  await expect(disclosure.locator('.settings-help-copy')).not.toContainText('**')

  await h.page.evaluate(() =>
    window.novalistStores.shell
      .getState()
      .openSettings('settings/diagnostics/display-information')
  )
  const readDisplay = h.page.getByRole('button', { name: 'Read display information' })
  await expect(readDisplay).toBeFocused()
  await readDisplay.click()
  const display = h.page.getByTestId('display-diagnostics')
  await expect(display).toBeVisible()
  const facts = await display.evaluate((element) => ({
    zoom: Number(element.getAttribute('data-zoom-factor')),
    scale: Number(element.getAttribute('data-scale-factor')),
    text: element.textContent ?? ''
  }))
  expect(facts.zoom).toBeGreaterThan(0)
  expect(facts.scale).toBeGreaterThan(0)
  expect(facts.text).toContain('Window size')
  expect(facts.text).toContain('Content size')
  expect(facts.text).toContain('Monitor work area')

  await h.close()
})

test('global Settings remains useful when no project is open', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-settings-global-')

  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
  await expect(h.page.locator('.settings-view')).toBeVisible({ timeout: 30_000 })
  await expect(h.page.locator('.settings-section-surface')).toHaveAttribute(
    'data-settings-section',
    'appearance'
  )
  await expect(h.page.locator('#set-theme')).toBeVisible()
  await expect(h.page.locator('.settings-scope')).toHaveCount(0)
  await expect(h.page.locator('.settings-scope-badge')).toHaveText('Global default')
  await expect(h.page.locator('.settings-nav-heading', { hasText: 'Project' })).toHaveCount(0)

  await h.close()
})
