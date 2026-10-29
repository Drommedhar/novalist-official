import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

test('tablet property definitions fit beside the Settings categories with and without the binder', async () => {
  const h = await launchApp('nl-tablet-properties-', {
    NOVALIST_FORCE_MOBILE: '1', NOVALIST_FORCE_TABLET: '1'
  })
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.rpc('manuscriptProps/setDefinitions', [[{
      key: 'revision-stage', label: 'Revision stage', scope: 'Scene', type: 'Enum',
      enumOptions: ['Draft', 'Reviewed'], showInOutliner: true
    }]])
    await h.page.evaluate(() => {
      document.documentElement.style.setProperty('--nl-mobile-sidebar-w', '64px')
      window.novalistStores.shell.getState().openSettings('manuscriptProperties')
    })
    const card = h.page.locator('[data-settings-section="manuscriptProperties"]')
    const row = card.locator('.props-row')
    await expect(row.locator('input').first()).toHaveValue('Revision stage')
    const failures: string[] = []
    for (const { width, binder } of [
      { width: 1180, binder: true }, { width: 720, binder: false }, { width: 900, binder: true }
    ]) {
      await resizeWindow(h, width, 800)
      const toggle = h.page.getByRole('button', { name: 'Toggle binder', exact: true })
      if ((await toggle.getAttribute('aria-pressed')) !== String(binder)) await toggle.click()
      await expect(h.page.locator('.tablet-binder')).toHaveCount(binder ? 1 : 0)
      const geometry = await row.evaluate(element => {
        const bounds = element.getBoundingClientRect()
        const form = document.querySelector('.settings-section-surface')!.getBoundingClientRect()
        const nav = document.querySelector('.settings-nav')!.getBoundingClientRect()
        const main = document.querySelector('.tablet-main .main-area')!
        const escaped = Array.from(element.querySelectorAll('input, select, button')).map(control => {
          const rect = control.getBoundingClientRect()
          return { control: control.tagName, left: rect.left, right: rect.right }
        }).filter(rect => rect.left < form.left - 1 || rect.right > form.right + 1)
        return {
          escaped, formWidth: form.width, rowWidth: bounds.width,
          rowOverflow: element.scrollWidth - element.clientWidth,
          mainOverflow: main.scrollWidth - main.clientWidth,
          categoriesBesideForm: nav.right <= form.left
        }
      })
      if (geometry.escaped.length || geometry.rowOverflow > 1 || geometry.mainOverflow > 1 || !geometry.categoriesBesideForm) {
        failures.push(`${width}px, binder=${binder}: ${JSON.stringify(geometry)}`)
      }
    }
    expect(failures, 'all property controls must fit while categories remain on the left').toEqual([])
    await row.locator('input').first().fill('Revision pass')
    await row.locator('select').first().selectOption('Chapter')
    await row.locator('select').nth(1).selectOption('String')
    await card.getByRole('button', { name: 'Save', exact: true }).click()
    await expect.poll(() => h.rpc('manuscriptProps/definitions')).toEqual([
      expect.objectContaining({ key: 'revision-stage', label: 'Revision pass', scope: 'Chapter', type: 'String' })
    ])
  } finally { await h.close() }
})
