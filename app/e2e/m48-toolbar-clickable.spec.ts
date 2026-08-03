import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * Nothing in the title-bar strip is swallowed by the window drag region.
 *
 * On Windows and Linux the toolbar *is* the title bar, so it carries
 * `-webkit-app-region: drag` and every control in it has to opt back out. Miss
 * one and the click moves the window instead of operating the control - which
 * is indistinguishable, from the writer's side, from a control that does
 * nothing. That is how the Layouts dropdown shipped unopenable: the opt-out
 * named buttons, and it is the one control there that is a select.
 *
 * Reported from the checklist as "Workspace layouts: the dropdown on the top
 * right does nothing".
 */
test('every control in the toolbar can actually be clicked', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-toolbar-drag-')
  await seedBook(h, { One: ['A'] })

  const dragging = await h.page.evaluate(() => {
    const toolbar = document.querySelector('.toolbar')
    if (!toolbar) return ['no toolbar found']
    const controls = Array.from(
      toolbar.querySelectorAll('button, select, input, a, [role="button"]')
    )
    return controls
      .filter((el) => {
        const region = getComputedStyle(el).getPropertyValue('-webkit-app-region').trim()
        return region !== 'no-drag'
      })
      .map((el) => `${el.tagName.toLowerCase()}.${el.className || '(no class)'}`)
  })

  expect(dragging, 'these toolbar controls sit in the window drag region').toEqual([])

  await h.close()
})

test('the layouts dropdown saves a layout and keeps it', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-layouts-save-')
  await seedBook(h, { One: ['A'] })

  const select = h.page.locator('.toolbar-panes-layouts')
  await expect(select).toBeVisible()

  await select.selectOption('__save')
  const dialog = h.page.locator('.dialog-card')
  await expect(dialog).toBeVisible({ timeout: 10_000 })
  await dialog.locator('input').first().fill('Drafting')
  await dialog.getByRole('button', { name: /OK|Save|Speichern|确定/ }).first().click()

  // In the list to pick from...
  await expect(select.locator('option', { hasText: 'Drafting' }).first()).toHaveCount(1)

  // ...and written down, or it is gone at the next launch, which is not a
  // saved layout.
  const stored = await h.page.evaluate(() => localStorage.getItem('nl.shell.paneLayouts'))
  expect(stored, 'the layout was never persisted').toContain('Drafting')

  await h.close()
})

/**
 * The control reads as the layout the window is in.
 *
 * It used to show "Layouts" always - its own name, offered as an entry that
 * could be picked and then did nothing - so there was no way to tell which of
 * your layouts you were looking at, or whether you were in one at all.
 *
 * Reported from the checklist as "why does the layouts dropdown show 'Layouts'
 * even in its dropdown list".
 */
test('the layouts dropdown names the layout the window is in', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-layouts-current-')
  await seedBook(h, { One: ['A'] })

  const select = h.page.locator('.toolbar-panes-layouts')
  await expect(select).toBeVisible()

  // Two layouts with genuinely different arrangements, saved straight through
  // the store so this test is about the label rather than the dialog.
  await h.page.evaluate(() => {
    const shell = window.novalistStores.shell.getState()
    shell.saveLayout('Drafting')
    shell.splitActivePane('row')
    window.novalistStores.shell.getState().saveLayout('Revision')
  })

  // Saving names what you are in: the split window is Revision.
  await expect(select).toHaveValue('Revision')

  await h.page.evaluate(() => window.novalistStores.shell.getState().applyLayout('Drafting'))
  await expect(select).toHaveValue('Drafting')

  // Its own name is never something you can pick.
  const placeholder = await select.locator('option[value=""]').first()
  await expect(placeholder).toHaveAttribute('disabled', '')
  await expect(placeholder).toHaveAttribute('hidden', '')

  // Take the arrangement apart and it stops claiming to be a saved layout,
  // rather than going on naming one the window is no longer in.
  await h.page.evaluate(() => window.novalistStores.shell.getState().splitActivePane('column'))
  await expect(select).toHaveValue('')

  await h.close()
})

/**
 * There is always a way back to one pane.
 *
 * Reported from the checklist as "there is no way to jump back to a default
 * layout". The default is built in rather than stored, which is what stops it
 * being forgotten along with the layouts the writer named.
 */
test('the layouts dropdown always offers an undeletable default', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-layouts-default-')
  await seedBook(h, { One: ['A'] })

  const select = h.page.locator('.toolbar-panes-layouts')
  await expect(select).toBeVisible()

  // Offered from the start, with nothing saved, and named as where you are.
  await expect(select.locator('option[value="__default"]')).toHaveCount(1)
  await expect(select).toHaveValue('__default')

  // Split the window up and go back in one pick.
  await h.page.evaluate(() => {
    const shell = window.novalistStores.shell.getState()
    shell.splitActivePane('row')
    window.novalistStores.shell.getState().splitActivePane('column')
  })
  expect(await h.page.evaluate(() => window.novalistStores.shell.getState().panes.kind))
    .toBe('split')

  await select.selectOption('__default')
  await expect
    .poll(() => h.page.evaluate(() => window.novalistStores.shell.getState().panes.kind))
    .toBe('leaf')
  await expect(select).toHaveValue('__default')

  // Saving and forgetting your own layouts never offers to forget this one.
  await h.page.evaluate(() => window.novalistStores.shell.getState().saveLayout('Mine'))
  const forgettable = await select.locator('option[value^="__forget:"]').allTextContents()
  expect(forgettable.some((t) => t.includes('Default')), 'the default can be deleted').toBe(false)
  expect(forgettable).toHaveLength(1)

  // A layout the writer named wins the label over the built-in one.
  await expect(select).toHaveValue('Mine')

  await h.close()
})
