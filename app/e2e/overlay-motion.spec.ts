import { expect, test, type Page } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'
import { holdMotion } from './motion'

async function notifyHost(page: Page, method: string, params: unknown[]): Promise<void> {
  await page.evaluate(({ method, params }) => {
    const client = window.novalistRpc as unknown as { dispatch(message: unknown): void }
    client.dispatch({ method, params })
  }, { method, params })
}

test('binder dialogs discard cancelled drafts when another target opens during exit', async () => {
  const h = await launchApp('nl-motion-binder-dialog-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await seedBook(h, { Opening: ['Arrival'], 'Next Chapter': ['Departure'] })
    await dismissTour(h.page)
    await enterWriting(h.page)
    const motion = await holdMotion(h.page, '.dialog-overlay')
    await h.page.locator('.binder-chapter-row').first().click({ button: 'right' })
    await h.page.locator('.context-menu').getByRole('menuitem', { name: 'Rename Chapter', exact: true }).click()
    const dialog = h.page.getByRole('dialog')
    await expect(dialog.locator('input').first()).toBeFocused()
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading && animation.duration <= 200)).toBe(true)
    await motion.evaluate(control => control.finish())
    await dialog.locator('input').first().fill('Uncommitted chapter title')
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    const closing = h.page.locator('.binder [data-motion-presence="closing"]').filter({ has: h.page.locator('.dialog-overlay') })
    await expect(closing).toHaveAttribute('inert', '')
    await expect(h.page.getByRole('dialog')).toHaveCount(0)
    await expect(h.page.locator('.binder-chapter-title').first()).toHaveText('Opening')
    await h.page.locator('.binder-chapter-row').nth(1).click({ button: 'right' })
    await h.page.locator('.context-menu').getByRole('menuitem', { name: 'Rename Chapter', exact: true }).click()
    await expect(dialog.locator('input').first()).toHaveValue('Next Chapter')
    await expect(dialog.locator('input').first()).toBeFocused()
    await motion.evaluate(control => control.finish())
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(closing).toHaveAttribute('inert', '')
    await motion.evaluate(control => control.finish())
    await expect(h.page.locator('.binder .dialog-overlay')).toHaveCount(0)
    await motion.evaluate(control => control.stop())
  } finally {
    await h.close()
  }
})

test('a snapshot label prompt exits without closing or disabling its parent dialog', async () => {
  const h = await launchApp('nl-motion-snapshot-prompt-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const book = await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await h.page.evaluate(async chapter => {
      await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
      window.novalistStores.shell.getState().openDialog('snapshots')
    }, book.chapters[0])
    const snapshots = h.page.getByRole('dialog', { name: 'Scene snapshots', exact: true })
    await expect(snapshots).toBeVisible()
    const motion = await holdMotion(h.page, '.dialog-overlay .dialog-overlay')
    await snapshots.getByRole('button', { name: 'Take snapshot', exact: true }).click()
    const prompt = h.page.getByRole('dialog', { name: 'Take snapshot', exact: true })
    await expect(prompt.locator('input')).toBeFocused()
    await motion.evaluate(control => control.finish())
    await prompt.locator('input').fill('Draft checkpoint')
    await h.page.keyboard.press('Escape')
    await expect(h.page.locator('.dialog-overlay [data-motion-presence="closing"]').filter({ has: h.page.locator('.dialog-overlay') })).toHaveAttribute('inert', '')
    await expect(snapshots).toBeVisible()
    await expect(h.page.getByRole('dialog', { name: 'Take snapshot', exact: true })).toHaveCount(0)
    await motion.evaluate(control => control.finish())
    await expect(h.page.locator('.dialog-overlay .dialog-overlay')).toHaveCount(0)
    await motion.evaluate(control => control.stop())
    const checkpoints = await h.rpc<{ label: string }[]>('snapshots/list', [book.chapters[0].guid, book.chapters[0].scenes[0].id])
    expect(checkpoints.some(checkpoint => checkpoint.label === 'Draft checkpoint')).toBe(false)
  } finally {
    await h.close()
  }
})

test('pane picker fades out while the chosen view becomes active immediately', async () => {
  const h = await launchApp('nl-motion-pane-picker-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await enterWriting(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().splitActivePane('row'))
    const pane = h.page.locator('.pane-leaf').first()
    const motion = await holdMotion(h.page, '.pane-picker')
    await pane.locator('.pane-header-view').click()
    await expect(h.page.getByRole('menu')).toBeVisible()
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.finish())
    await h.page.getByRole('menuitemradio', { name: 'Settings', exact: true }).click()
    await expect(pane).toHaveAttribute('data-view', 'settings')
    await expect(h.page.locator('[data-motion-presence="closing"]').filter({ has: h.page.locator('.pane-picker') })).toHaveAttribute('inert', '')
    await expect(h.page.getByRole('menu')).toHaveCount(0)
    await motion.evaluate(control => control.finish())
    await expect(h.page.locator('.pane-picker')).toHaveCount(0)
    await motion.evaluate(control => control.stop())
    await pane.locator('.settings-search').fill('font')
    await expect(pane.locator('.settings-search')).toHaveValue('font')
  } finally {
    await h.close()
  }
})

test('individual toast dismissal preserves neighbouring messages and respects reduced motion', async () => {
  const h = await launchApp('nl-motion-toasts-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    await seedBook(h, {})
    await dismissTour(h.page)
    await notifyHost(h.page, 'ui/showNotification', ['First message'])
    await notifyHost(h.page, 'ui/showNotification', ['Second message'])
    await notifyHost(h.page, 'ui/showNotification', ['Third message'])
    await expect(h.page.locator('.toast-card')).toHaveCount(3)
    const motion = await holdMotion(h.page, '.toast-card')
    const second = h.page.locator('.toast-card').filter({ hasText: 'Second message' })
    await second.locator('button').click()
    await expect(h.page.locator('[data-motion-presence="closing"]').filter({ has: second })).toHaveAttribute('inert', '')
    await expect(h.page.locator('.toast-message')).toHaveText(['First message', 'Second message', 'Third message'])
    await expect(h.page.getByRole('status').filter({ hasText: 'Second message' })).toHaveCount(0)
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading)).toBe(true)
    await motion.evaluate(control => control.finish())
    await expect(h.page.locator('.toast-message')).toHaveText(['First message', 'Third message'])
    await motion.evaluate(control => control.stop())
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    await h.page.locator('.toast-card').first().locator('button').click()
    await expect(h.page.locator('.toast-message')).toHaveText(['Third message'])
    expect(await h.page.locator('.toast-host').evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0)
  } finally {
    await h.close()
  }
})

test('entity peek fades after its data loads and releases interaction when closed', async () => {
  const h = await launchApp('nl-motion-entity-peek-')
  try {
    await h.page.emulateMedia({ reducedMotion: 'no-preference' })
    const book = await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await enterWriting(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    await h.rpc('entities/create', ['character', 'Mira Vance'])
    await h.rpc('scenes/write', [book.chapters[0].guid, book.chapters[0].scenes[0].id, '<p>Mira Vance entered.</p>', 'Mira Vance entered.'])
    await h.page.evaluate(async chapter => {
      await window.novalistStores.codex.getState().refresh()
      await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
    }, book.chapters[0])
    const row = h.page.locator('.ctx-card').filter({ hasText: 'Mira Vance' })
    await expect(row).toBeVisible()
    const motion = await holdMotion(h.page, '.peek-card-anchor')
    await row.hover()
    const card = h.page.locator('.peek-card')
    await expect(card).toBeVisible()
    expect((await motion.evaluate(control => control.details())).some(animation => animation.fading && animation.duration <= 200)).toBe(true)
    await motion.evaluate(control => control.finish())
    await card.locator('.peek-action').last().click()
    await expect(h.page.locator('[data-motion-presence="closing"]').filter({ has: card })).toHaveAttribute('inert', '')
    await motion.evaluate(control => control.finish())
    await expect(card).toHaveCount(0)
    await motion.evaluate(control => control.stop())
  } finally {
    await h.close()
  }
})
