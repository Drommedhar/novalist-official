import { expect, test, type Page } from '@playwright/test'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

type CloseProbe = {
  flushHeld: boolean
  backupHeld: boolean
  releaseFlush(): void
  releaseBackup(): void
  restore(): void
}

async function holdClose(page: Page, rejectSave = false): Promise<void> {
  await page.evaluate((rejectSave) => {
    const store = window.novalistStores.project
    const originalFlush = store.getState().flushPendingSave
    const client = window.novalistRpc
    const originalRequest = client.request.bind(client)
    let releaseFlush!: () => void, releaseBackup!: () => void
    const flushGate = new Promise<void>((resolve) => { releaseFlush = resolve })
    const backupGate = new Promise<void>((resolve) => { releaseBackup = resolve })
    const probe: CloseProbe = {
      flushHeld: false, backupHeld: false, releaseFlush, releaseBackup,
      restore: () => {
        store.setState({ flushPendingSave: originalFlush })
        client.request = originalRequest
        releaseFlush()
        releaseBackup()
      }
    }
    ;(window as unknown as { closeProbe: CloseProbe }).closeProbe = probe
    store.setState({ flushPendingSave: async () => {
      probe.flushHeld = true
      await flushGate
      if (rejectSave) throw new Error('Simulated save refusal')
      await originalFlush()
    } })
    client.request = async <T>(method: string, params?: unknown): Promise<T> => {
      if (method === 'backup/create' && (params as string[])?.[0] === 'close') {
        probe.backupHeld = true
        await backupGate
      }
      return originalRequest<T>(method, params)
    }
  }, rejectSave)
}

async function requestMainClose(h: Harness): Promise<void> {
  await h.app.evaluate(({ BrowserWindow }) => {
    BrowserWindow.getAllWindows().find((win) => !new URL(win.webContents.getURL()).searchParams.has('pane'))!.close()
  })
}

test('close immediately blocks every window, explains save and backup waits, and retains pending prose', async () => {
  const h = await launchApp('novalist-close-feedback-')
  try {
    const book = await seedBook(h, { Chapter: ['Main', 'Detached'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    await h.page.evaluate((scene) => window.novalistStores.project.getState().openScene(scene.chapterGuid, scene.sceneId), {
      chapterGuid: chapter.guid, sceneId: chapter.scenes[0].id
    })
    const opened = h.app.waitForEvent('window')
    await h.page.evaluate((scene) => window.novalist.openPaneWindow({
      view: 'write', projectPath: window.novalistStores.project.getState().projectPath, ...scene
    }), { chapterGuid: chapter.guid, sceneId: chapter.scenes[1].id })
    const second = await opened
    await expect(second.locator('.app-shell.detached')).toBeVisible()
    const path = (await h.page.evaluate(() => window.novalistStores.project.getState().projectPath))!
    for (const [page, marker] of [[h.page, 'Main closing prose'], [second, 'Detached closing prose']] as const) {
      await page.frameLocator('.editor-frame').locator('#editor').evaluate((element, marker) => {
        element.innerHTML = `<p><strong>${marker}</strong></p>`
        element.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText' }))
      }, marker)
    }
    const settingsButton = await h.page.locator('.mode-rail-settings').boundingBox()
    expect(settingsButton).not.toBeNull()
    await holdClose(h.page)
    await requestMainClose(h)

    for (const page of [h.page, second]) {
      await expect(page.getByRole('dialog', { name: 'Closing window…', exact: true })).toBeVisible()
      await expect(page.locator('.window-closing-dialog')).toContainText('Saving changes before closing…')
      await expect(page.locator('.shell, .app-shell')).toHaveAttribute('inert', '')
      await page.keyboard.press('Escape')
      await expect(page.locator('.window-closing-dialog')).toBeVisible()
      await page.keyboard.press('ControlOrMeta+Shift+P')
      expect(await page.evaluate(() => window.novalistStores.shell.getState().commandPaletteOpen)).toBe(false)
    }
    // Real pointer input must hit the modal backdrop rather than navigate.
    await h.page.mouse.click(settingsButton!.x + settingsButton!.width / 2, settingsButton!.y + settingsButton!.height / 2)
    expect(await h.page.evaluate(() => window.novalistStores.shell.getState().mainView)).toBe('write')
    expect(h.app.windows()).toHaveLength(2)
    await h.page.evaluate(() => (window as unknown as { closeProbe: CloseProbe }).closeProbe.releaseFlush())
    await expect.poll(() => h.page.evaluate(() => (window as unknown as { closeProbe: CloseProbe }).closeProbe.backupHeld)).toBe(true)
    await expect(h.page.locator('.window-closing-dialog')).toContainText('Creating a backup before closing…')
    await expect(second.locator('.window-closing-dialog')).toBeVisible()
    expect(h.app.windows()).toHaveLength(2)
    await h.page.evaluate(() => (window as unknown as { closeProbe: CloseProbe }).closeProbe.releaseBackup())
    await expect.poll(() => h.app.windows().length).toBe(0)
    const prose = readdirSync(path, { recursive: true }).filter(file => String(file).includes('scene-') && String(file).endsWith('.novalist'))
      .map(file => readFileSync(join(path, String(file)), 'utf8')).join('\n')
    expect(prose).toContain('<strong>Main closing prose</strong>')
    expect(prose).toContain('<strong>Detached closing prose</strong>')
  } finally { await h.close().catch(() => {}) }
})

test('close covers an already open native dialog and save failure restores its input', async () => {
  const h = await launchApp('novalist-close-modal-')
  try {
    await h.page.locator('.library-scratchpad-button').click()
    const scratchpad = h.page.locator('.library-scratchpad-dialog:not(.library-removed-dialog)')
    const input = scratchpad.locator('textarea')
    await input.fill('Keep this draft')
    const inputBounds = await input.boundingBox()
    expect(inputBounds).not.toBeNull()
    await holdClose(h.page, true)
    await requestMainClose(h)
    await expect(h.page.locator('.window-closing-dialog')).toBeVisible()
    await expect.poll(() => h.page.evaluate(() => (window as unknown as { closeProbe: CloseProbe }).closeProbe.flushHeld)).toBe(true)
    await h.page.mouse.click(inputBounds!.x + inputBounds!.width / 2, inputBounds!.y + inputBounds!.height / 2)
    await h.page.keyboard.type(' must not enter')
    await h.page.keyboard.press('Escape')
    await expect(input).toHaveValue('Keep this draft')
    await expect(h.page.locator('.window-closing-dialog')).toBeVisible()
    await h.page.evaluate(() => (window as unknown as { closeProbe: CloseProbe }).closeProbe.releaseFlush())
    await expect(h.page.locator('.window-closing-dialog')).toHaveCount(0)
    await expect(h.page.locator('.shell')).not.toHaveAttribute('inert')
    await expect(scratchpad).toBeVisible()
    await input.fill('Editing works again')
    await expect(input).toHaveValue('Editing works again')
    expect(h.app.windows()).toHaveLength(1)
    await h.page.evaluate(() => (window as unknown as { closeProbe: CloseProbe }).closeProbe.restore())
  } finally { await h.close() }
})
