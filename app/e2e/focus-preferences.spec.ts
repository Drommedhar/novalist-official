import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

test('Focus settings persist, apply to the page and surround, and reset independently', async () => {
  const h = await launchApp('nl-focus-preferences-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.page.evaluate(async (chapter) => {
      await window.novalistStores.settings.getState().update('global', { theme: 'Light', pageViewEnabled: true })
      await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
      window.novalistStores.shell.getState().openSettings('focus')
    }, book.chapters[0])
    await h.page.locator('#set-focus-peek-delay').fill('1400')
    await h.page.locator('#set-focus-peek-delay').press('Enter')
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.settings.getState().view?.global.focusPeekDelayMs)).toBe(1400)
    await h.page.locator('#set-focus-peek').click()
    await expect(h.page.locator('#set-focus-peek')).not.toBeChecked()
    await h.page.locator('#set-focus-page-color').fill('#203040')
    await expect(h.page.locator('#set-focus-page-color')).toHaveValue('#203040')
    await h.page.locator('#set-focus-background-color').fill('#abcdee')
    await expect(h.page.locator('#set-focus-background-color')).toHaveValue('#abcdee')
    const backgroundPng = Buffer.from(await h.page.evaluate(() => {
      const canvas = document.createElement('canvas')
      canvas.width = 100
      canvas.height = 100
      const context = canvas.getContext('2d')!
      const gradient = context.createLinearGradient(0, 0, 100, 100)
      gradient.addColorStop(0, '#b0c4de')
      gradient.addColorStop(1, '#c0d0e0')
      context.fillStyle = gradient
      context.fillRect(0, 0, 100, 100)
      return canvas.toDataURL('image/png').split(',')[1]
    }), 'base64')
    await h.page.locator('#set-focus-background-image').setInputFiles({ name: 'clouds.png', mimeType: 'image/png', buffer: backgroundPng })
    await expect(h.page.getByRole('button', { name: 'Remove background image' })).toBeVisible()
    await h.page.evaluate(() => window.novalistStores.settings.getState().load())
    await expect(h.page.locator('#set-focus-peek')).not.toBeChecked()
    const saved = await h.rpc<{ global: Record<string, unknown> }>('settings/get')
    expect(saved.global.focusPeekDelayMs).toBe(1400)
    expect(saved.global.focusBackgroundImage).toBe(`data:image/png;base64,${backgroundPng.toString('base64')}`)

    await enterWriting(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleFocusMode())
    const body = h.page.frameLocator('iframe.editor-frame').locator('body')
    const paper = body.locator('.nv-page').first()
    await expect(body).toHaveClass(/focus-appearance/)
    await expect(h.page.locator('iframe.editor-frame')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
    await expect(h.page.frameLocator('iframe.editor-frame').locator('html')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
    await expect(paper).toHaveCSS('background-color', 'rgb(32, 48, 64)')
    await expect(paper).toHaveCSS('color', 'rgb(255, 255, 255)')
    await expect(body.locator('#editor-wrapper')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
    await expect(h.page.locator('.editor-pane')).toHaveCSS('background-color', 'rgb(171, 205, 238)')
    await expect(h.page.locator('.editor-pane')).toHaveCSS('background-image', /data:image\/png;base64/)
    await h.page.screenshot({ path: 'test-results/focus-custom-background.png' })

    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('focus'))
    await h.page.getByRole('button', { name: 'Remove background image' }).click()
    await expect(h.page.getByRole('button', { name: 'Remove background image' })).toHaveCount(0)
    const resets = h.page.getByRole('button', { name: 'Use theme colour' })
    await resets.first().click()
    await resets.last().click()
    await h.page.locator('#set-focus-background-image').setInputFiles({ name: 'bad.png', mimeType: 'image/png', buffer: Buffer.from('invalid') })
    await expect(h.page.getByRole('alert')).toContainText('Could not save the image')
    await enterWriting(h.page)
    await expect(h.page.locator('.editor-pane')).toHaveCSS('background-image', 'none')
    await expect(paper).not.toHaveCSS('background-color', 'rgb(32, 48, 64)')
    await expect(body.locator('#editor-wrapper')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
    await h.page.screenshot({ path: 'test-results/focus-light-background.png' })
    await h.page.evaluate(() => window.novalistStores.shell.getState().toggleFocusMode())
    await expect(body).not.toHaveClass(/focus-appearance/)
    await expect(body.locator('#editor-wrapper')).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
  } finally { await h.close() }
})

test('update dialog buttons and keyboard work above another modal, including a pending check', async () => {
  const h = await launchApp('nl-update-modal-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.app.evaluate(({ ipcMain }) => {
      ipcMain.removeHandler('novalist:check-app-update')
      ipcMain.handle('novalist:check-app-update', () => null)
    })
    await h.page.evaluate(() => {
      window.novalistStores.extensions.setState({ checkStoreUpdates: async () => 0, storeUpdates: [] })
      window.novalistStores.shell.getState().openDialog('chapter')
    })
    const underlying = h.page.locator('.dialog-overlay [role="dialog"]')
    await expect(underlying).toBeVisible()
    await h.page.evaluate(() => window.postMessage({ novalist: 'menu-command', command: 'help:checkUpdates' }, '*'))
    const update = h.page.locator('.update-dialog')
    await expect(update.getByRole('button', { name: 'Check again' })).toBeEnabled()
    await update.getByRole('button', { name: 'Check again' }).click()
    await expect(update.getByRole('button', { name: 'Check again' })).toBeEnabled()
    await update.getByRole('button', { name: 'Close', exact: true }).focus()
    await h.page.keyboard.press('Tab')
    await expect(update.getByRole('button', { name: 'Check again' })).toBeFocused()
    await update.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(update).toHaveCount(0)
    await expect(underlying.locator('input').first()).toBeEditable()

    await h.app.evaluate(({ ipcMain }) => {
      ipcMain.removeHandler('novalist:check-app-update')
      ipcMain.handle('novalist:check-app-update', () => new Promise<null>((resolve) => setTimeout(() => resolve(null), 2000)))
    })
    await h.page.evaluate(() => window.postMessage({ novalist: 'menu-command', command: 'help:checkUpdates' }, '*'))
    await expect(update.getByRole('button', { name: 'Check again' })).toBeDisabled()
    await update.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(update).toHaveCount(0)
    await underlying.locator('input').first().fill('Still responsive')
    await h.page.keyboard.press('Escape')
    await expect(underlying).toHaveCount(0)
    await expect(h.page.locator('.shell')).not.toHaveAttribute('inert')
  } finally { await h.close() }
})
