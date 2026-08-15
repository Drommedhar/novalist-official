import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * Nothing in the toolbar strip is swallowed by the window drag region.
 *
 * On macOS the toolbar still sits under a hidden title bar, so it carries
 * `-webkit-app-region: drag` and every control in it has to opt back out. Miss
 * one and the click moves the window instead of operating the control - which
 * is indistinguishable, from the writer's side, from a control that does
 * nothing. That is how the Layouts dropdown shipped unopenable: the opt-out
 * named buttons, and it was the one control there that is a select.
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

/**
 * Named pane arrangements survive being saved.
 *
 * They used to be a dropdown on the main toolbar, which put a control over the
 * shape of the whole window on the bar that belongs to the open project. Under
 * the placement law splitting the content area is application scope, so it and
 * its saved arrangements moved to the View menu - and the menu opens this
 * dialog, which is what these three tests drive.
 */
async function openPaneLayouts(h: Awaited<ReturnType<typeof launchApp>>): Promise<void> {
  await h.page.evaluate(() =>
    window.novalistStores.shell.getState().openDialog('paneLayouts')
  )
  await expect(h.page.locator('.dialog-card')).toBeVisible({ timeout: 10_000 })
}

test('a named pane layout is saved and written down', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-layouts-save-')
  await seedBook(h, { One: ['A'] })

  await openPaneLayouts(h)
  const dialog = h.page.locator('.dialog-card')
  await dialog.locator('input').first().fill('Drafting')
  await dialog.locator('.layouts-save button').click()

  // In the list to pick from...
  await expect(dialog.locator('.layouts-apply', { hasText: 'Drafting' })).toHaveCount(1)

  // ...and written down, or it is gone at the next launch, which is not a
  // saved layout.
  const stored = await h.page.evaluate(() => localStorage.getItem('nl.shell.paneLayouts'))
  expect(stored, 'the layout was never persisted').toContain('Drafting')

  await h.close()
})

/**
 * The dialog says which arrangement the window is in.
 *
 * The dropdown it replaced used to show "Layouts" always - its own name,
 * offered as an entry that could be picked and then did nothing - so there was
 * no way to tell which of your layouts you were looking at, or whether you were
 * in one at all.
 *
 * Reported from the checklist as "why does the layouts dropdown show 'Layouts'
 * even in its dropdown list".
 */
test('the pane layouts dialog names the arrangement the window is in', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-layouts-current-')
  await seedBook(h, { One: ['A'] })

  // Two layouts with genuinely different arrangements, saved straight through
  // the store so this test is about the label rather than the dialog's form.
  await h.page.evaluate(() => {
    const shell = window.novalistStores.shell.getState()
    shell.saveLayout('Drafting')
    shell.splitActivePane('row')
    window.novalistStores.shell.getState().saveLayout('Revision')
  })

  await openPaneLayouts(h)
  const current = h.page.locator('.layouts-apply[aria-current="true"]')
  // Saving names what you are in: the split window is Revision.
  await expect(current).toHaveText('Revision')

  await h.page.evaluate(() => window.novalistStores.shell.getState().applyLayout('Drafting'))
  await openPaneLayouts(h)
  await expect(h.page.locator('.layouts-apply[aria-current="true"]')).toHaveText('Drafting')

  // Take the arrangement apart and it stops claiming to be a saved layout,
  // rather than going on naming one the window is no longer in.
  await h.page.evaluate(() => window.novalistStores.shell.getState().splitActivePane('column'))
  await openPaneLayouts(h)
  await expect(h.page.locator('.layouts-apply[aria-current="true"]')).toHaveCount(0)

  await h.close()
})

/**
 * There is always a way back to one pane.
 *
 * Reported from the checklist as "there is no way to jump back to a default
 * layout". The default is built in rather than stored, which is what stops it
 * being forgotten along with the layouts the writer named.
 */
test('the pane layouts dialog always offers an undeletable default', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-layouts-default-')
  await seedBook(h, { One: ['A'] })

  await openPaneLayouts(h)
  const dialog = h.page.locator('.dialog-card')
  // Offered from the start, with nothing saved, and named as where you are.
  await expect(dialog.locator('.layouts-row')).toHaveCount(1)
  await expect(dialog.locator('.layouts-apply[aria-current="true"]')).toHaveCount(1)

  // Split the window up and go back in one click.
  await h.page.evaluate(() => {
    const shell = window.novalistStores.shell.getState()
    shell.splitActivePane('row')
    window.novalistStores.shell.getState().splitActivePane('column')
  })
  expect(await h.page.evaluate(() => window.novalistStores.shell.getState().panes.kind))
    .toBe('split')

  await openPaneLayouts(h)
  await dialog.locator('.layouts-apply').first().click()
  await expect
    .poll(() => h.page.evaluate(() => window.novalistStores.shell.getState().panes.kind))
    .toBe('leaf')

  // Saving and forgetting your own layouts never offers to forget this one:
  // the default row has no delete button beside it.
  await h.page.evaluate(() => window.novalistStores.shell.getState().saveLayout('Mine'))
  await openPaneLayouts(h)
  await expect(dialog.locator('.layouts-row')).toHaveCount(2)
  await expect(dialog.locator('.layouts-row .icon-button')).toHaveCount(1)

  await h.close()
})
