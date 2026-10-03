import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

test('F11 works in prose and panel fields, and focus width follows editor zoom', async () => {
  const h = await launchApp('nl-focus-zoom-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await resizeWindow(h, 1500, 950)
    await h.page.evaluate(async (chapter) => {
      await window.novalistStores.settings.getState().update('global', { editorFontSize: 17, pageViewEnabled: true })
      await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
    }, book.chapters[0])
    const frame = h.page.locator('iframe.editor-frame')
    const editor = h.page.frameLocator('iframe.editor-frame').locator('#editor')
    await editor.click()
    await h.page.keyboard.type('A page with room to grow.')
    await editor.evaluate((element) => { element.dataset.focusProbe = 'retained' })
    await h.page.keyboard.press('F11')
    await expect(h.page.locator('.shell-focus')).toBeVisible()
    await expect.poll(() => h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isFullScreen())).toBe(true)
    await expect.poll(() => h.app.evaluate(({ Menu }) => {
      const view = Menu.getApplicationMenu()?.items.find((item) => item.label === 'View')
      return view?.submenu?.items.find((item) => item.label === 'Focus Mode')?.accelerator
    })).toBe('F11')
    if (process.platform !== 'darwin') {
      expect(await h.app.evaluate(({ Menu }) => {
        const view = Menu.getApplicationMenu()?.items.find((item) => item.label === 'View')
        const fullscreen = view?.submenu?.items.find((item) => item.role === 'togglefullscreen')
        return { accelerator: fullscreen?.accelerator, registered: fullscreen?.registerAccelerator }
      })).toEqual({ accelerator: '', registered: false })
    }

    const initialWidth = (await frame.boundingBox())!.width
    const initialHoverWidth = (await h.page.locator('.focus-edge-left').boundingBox())!.width
    expect(initialHoverWidth).toBeGreaterThan(24)
    expect(await h.page.locator('.focus-edge-left').evaluate((element) => element.getBoundingClientRect().right))
      .toBeCloseTo((await frame.boundingBox())!.x, 0)
    const paper = h.page.frameLocator('iframe.editor-frame').locator('.nv-page').first()
    const initialPaperWidth = (await paper.boundingBox())!.width
    await editor.evaluate((element) => element.dispatchEvent(new WheelEvent('wheel', { ctrlKey: true, deltaY: -100, bubbles: true, cancelable: true })))
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.settings.getState().view?.effective.editorFontSize)).toBe(18)
    await expect.poll(async () => (await frame.boundingBox())!.width).toBeGreaterThan(initialWidth)
    await expect.poll(async () => (await paper.boundingBox())!.width).toBeGreaterThan(initialPaperWidth)
    const zoomedWidth = (await frame.boundingBox())!.width
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { editorFontSize: 30 }))
    await expect.poll(async () => (await frame.boundingBox())!.width).toBeGreaterThan(zoomedWidth)
    await expect.poll(async () => (await h.page.locator('.focus-edge-left').boundingBox())!.width).toBeLessThan(initialHoverWidth)
    expect(await frame.evaluate((element) => element.getBoundingClientRect().right <= window.innerWidth)).toBe(true)
    await expect(editor).toHaveAttribute('data-focus-probe', 'retained')
    await expect(editor).toContainText('A page with room to grow.')
    await h.page.screenshot({ path: 'test-results/focus-zoom.png' })

    await h.page.keyboard.press('Control+Shift+n')
    const notes = h.page.getByRole('dialog', { name: 'Synopsis and notes' })
    await notes.locator('#dock-synopsis').fill('F11 works while editing a panel.')
    await h.page.keyboard.press('F11')
    await expect(h.page.locator('.shell-focus')).toHaveCount(0)
    await expect.poll(() => h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isFullScreen())).toBe(false)
    await expect.poll(() => h.rpc<{ synopsis: string }>('scenes/getMeta', [book.chapters[0].guid, book.chapters[0].scenes[0].id]).then((scene) => scene.synopsis))
      .toBe('F11 works while editing a panel.')
    await expect(editor).toHaveAttribute('data-focus-probe', 'retained')
  } finally { await h.close() }
})

test('edge hover reveals overlay panels without taking the caret or resizing the page', async () => {
  const h = await launchApp('nl-focus-edges-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await resizeWindow(h, 1300, 900)
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), book.chapters[0])
    const frame = h.page.locator('iframe.editor-frame')
    const editor = h.page.frameLocator('iframe.editor-frame').locator('#editor')
    await editor.click()
    await h.page.keyboard.type('The caret stays here.')
    // Analysis reads saved prose; wait for autosave before opening its fields.
    await expect.poll(() => h.rpc<{ html: string }>('scenes/read', [book.chapters[0].guid, book.chapters[0].scenes[0].id]).then((scene) => scene.html))
      .toContain('The caret stays here.')
    await h.page.keyboard.press('F11')
    await expect.poll(() => h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isFullScreen())).toBe(true)
    const bounds = (await frame.boundingBox())!
    const returnToPage = (): Promise<void> => h.page.mouse.move(bounds.x + bounds.width / 2, bounds.y + 100)

    await h.page.locator('.focus-edge-left').hover()
    const binder = h.page.getByRole('dialog', { name: 'Chapters and scenes' })
    await expect(binder).toBeVisible()
    await expect(binder).toHaveAttribute('aria-modal', 'false')
    await expect(editor).toBeFocused()
    expect(await frame.boundingBox()).toEqual(bounds)
    await binder.hover()
    await expect(binder.locator('.binder-scene-row')).toHaveCount(1)
    await returnToPage()
    await expect(binder).toHaveCount(0)
    await expect(editor).toBeFocused()

    await h.page.locator('.focus-edge-right').hover()
    const inspector = h.page.getByRole('dialog', { name: 'Scene context' })
    await expect(inspector).toBeVisible()
    await expect(editor).toBeFocused()
    expect(await frame.boundingBox()).toEqual(bounds)
    const conflict = inspector.getByPlaceholder('Conflict', { exact: true })
    await conflict.fill('Keep this edit while the pointer moves away.')
    await returnToPage()
    // Cross the 300 ms retreat delay: an active field must keep its panel.
    await h.page.waitForTimeout(400)
    await expect(conflict).toBeFocused()
    await expect(inspector).toBeVisible()
    await editor.focus()
    await expect(inspector).toHaveCount(0)
    await h.page.locator('.focus-edge-right').hover()
    await expect(conflict).toHaveValue('Keep this edit while the pointer moves away.')
    await h.page.keyboard.press('Escape')
    await expect(inspector).toHaveCount(0)
    await expect(editor).toBeFocused()
    await returnToPage()
    await h.page.keyboard.type(' Still here.')
    await expect(editor).toContainText('The caret stays here. Still here.')

    await h.page.keyboard.press('Control+Alt+i')
    await expect(inspector).toHaveAttribute('aria-modal', 'true')
    const leftEdge = (await h.page.locator('.focus-edge-left').boundingBox())!
    await h.page.mouse.move(leftEdge.x + 2, leftEdge.y + 150)
    await expect(inspector).toBeVisible()
    await expect(binder).toHaveCount(0)
    await h.page.keyboard.press('Escape')
    await expect(inspector).toHaveCount(0)
  } finally { await h.close() }
})

test('context is prepared before reveal, retained between hovers, and follows scene changes', async () => {
  const h = await launchApp('nl-focus-ready-')
  try {
    const book = await seedBook(h, { Chapter: ['First scene', 'Second scene'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    for (const scene of chapter.scenes) {
      await h.rpc('scenes/write', [chapter.guid, scene.id, `<p>${scene.title} has some words.</p>`, `${scene.title} has some words.`, null])
    }
    await h.page.evaluate(async (chapter) => {
      await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      // Hold real data until the test releases it, so busy/ready assertions
      // do not depend on how quickly this machine completes the UI actions.
      document.body.dataset.holdAnalysis = 'true'
      window.novalistRpc.request = async (method, params) => {
        if (method === 'context/analyze') {
          document.body.dataset.analysisCalls = String(Number(document.body.dataset.analysisCalls ?? 0) + 1)
          if (document.body.dataset.holdAnalysis === 'true') {
            await new Promise<void>((resolve) => window.addEventListener('release-focus-analysis', () => resolve(), { once: true }))
          }
        }
        return original(method, params)
      }
      window.novalistStores.shell.getState().toggleFocusMode()
    }, chapter)
    await expect.poll(() => h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].isFullScreen())).toBe(true)
    const editor = h.page.frameLocator('iframe.editor-frame').locator('#editor')
    await editor.focus()
    const retained = h.page.locator('.focus-panel-inspector')
    const inspector = h.page.getByRole('dialog', { name: 'Scene context' })
    const releaseAnalysis = () => h.page.evaluate(() => {
      delete document.body.dataset.holdAnalysis
      window.dispatchEvent(new Event('release-focus-analysis'))
    })
    await expect(retained).toHaveCount(1)
    await h.page.locator('.focus-edge-right').hover()
    await expect(h.page.locator('.focus-edge-right')).toHaveAttribute('aria-busy', 'true')
    await expect(inspector).toHaveCount(0)
    await releaseAnalysis()
    await expect(inspector).toBeVisible()
    await expect(inspector.getByPlaceholder('Conflict', { exact: true })).toBeVisible()
    await expect(inspector.locator('.inspector-header')).toHaveText('First scene')
    await retained.evaluate((element) => { element.dataset.retained = 'yes' })
    const calls = await h.page.locator('body').getAttribute('data-analysis-calls')
    await h.page.keyboard.press('Escape')
    await expect(retained).toHaveAttribute('inert', '')
    // Closing exposes the same edge under the stationary pointer. It must not
    // reopen after its hover delay; a deliberate leave/re-enter below still works.
    await h.page.locator('.focus-edge-right').dispatchEvent('pointerover', { pointerType: 'mouse' })
    await h.page.waitForTimeout(450)
    await expect(retained).toBeHidden()
    await editor.hover()
    await h.page.locator('.focus-edge-right').hover()
    await expect(inspector).toBeVisible()
    await expect(inspector).toHaveAttribute('data-retained', 'yes')
    await expect(h.page.locator('body')).toHaveAttribute('data-analysis-calls', calls!)
    expect(await retained.evaluate((element) => getComputedStyle(element).transitionProperty)).toContain('transform')

    // Closing with the pointer already over the manuscript must allow the next hover.
    await editor.hover()
    await h.page.keyboard.press('Escape')
    await h.page.locator('.focus-edge-right').hover()
    await expect(inspector).toBeVisible()

    await h.page.keyboard.press('Escape')
    await editor.hover()
    await editor.click()
    await h.page.evaluate(() => { document.body.dataset.holdAnalysis = 'true' })
    await h.page.keyboard.press('Control+End')
    await h.page.keyboard.type(' More saved prose for the prepared context.')
    await expect.poll(() => h.page.locator('body').getAttribute('data-analysis-calls')).not.toBe(calls)
    await h.page.locator('.focus-edge-right').hover()
    await expect(h.page.locator('.focus-edge-right')).toHaveAttribute('aria-busy', 'true')
    await expect(inspector).toHaveCount(0)
    await releaseAnalysis()
    await expect(inspector).toBeVisible()

    await h.page.keyboard.press('Escape')
    await editor.hover()
    await h.page.evaluate(() => { document.body.dataset.holdAnalysis = 'true' })
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[1].id), chapter)
    await h.page.locator('.focus-edge-right').hover()
    await expect(h.page.locator('.focus-edge-right')).toHaveAttribute('aria-busy', 'true')
    await expect(inspector).toHaveCount(0)
    await releaseAnalysis()
    await expect(inspector).toBeVisible()
    await expect(inspector.locator('.inspector-header')).toHaveText('Second scene')
    await expect(inspector.getByPlaceholder('Conflict', { exact: true })).toBeVisible()
    await retained.evaluate(async (element) => {
      await Promise.all(element.getAnimations().map((animation) => animation.finished))
    })
    await h.page.screenshot({ path: 'test-results/focus-context-ready.png' })
    await h.page.emulateMedia({ reducedMotion: 'reduce' })
    expect(await retained.evaluate((element) => getComputedStyle(element).transitionDuration)).toBe('0s')
  } finally { await h.close() }
})

test('focus restores window chrome and preserves a pre-existing full screen session', async () => {
  test.skip(process.platform === 'linux' && !!process.env.CI,
    'Linux CI uses Xvfb without a window manager; native window state runs in Windows CI')
  const h = await launchApp('nl-focus-window-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    const size = await h.app.evaluate(({ screen }) => {
      const area = screen.getPrimaryDisplay().workAreaSize
      return { width: Math.min(1200, area.width), height: Math.min(800, area.height) }
    })
    await resizeWindow(h, size.width, size.height)
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), book.chapters[0])
    const windowState = () => h.app.evaluate(({ BrowserWindow }) => {
      const win = BrowserWindow.getAllWindows()[0]
      return { fullScreen: win.isFullScreen(), maximized: win.isMaximized(), bounds: win.getBounds(), menu: win.isMenuBarVisible(), autoHide: win.isMenuBarAutoHide() }
    })
    const previous = await windowState()
    await h.page.keyboard.press('F11')
    await expect.poll(async () => (await windowState()).fullScreen).toBe(true)
    if (process.platform !== 'darwin') expect((await windowState()).menu).toBe(false)
    await h.page.keyboard.press('F11')
    await expect.poll(windowState).toEqual(previous)

    await h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].setFullScreen(true))
    await expect.poll(async () => (await windowState()).fullScreen).toBe(true)
    await h.page.keyboard.press('F11')
    await expect(h.page.locator('.shell-focus')).toBeVisible()
    await h.page.keyboard.press('F11')
    await expect(h.page.locator('.shell-focus')).toHaveCount(0)
    expect((await windowState()).fullScreen).toBe(true)

    await resizeWindow(h, size.width, size.height)
    await h.page.keyboard.press('F11')
    await expect.poll(async () => (await windowState()).fullScreen).toBe(true)
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await expect.poll(windowState).toEqual(previous)

    await h.page.evaluate(() => window.novalistStores.project.getState().openProject(window.novalistStores.project.getState().recentProjects[0].path))
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), book.chapters[0])
    await h.app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0].maximize())
    await expect.poll(async () => (await windowState()).maximized).toBe(true)
    const maximized = await windowState()
    await h.page.evaluate(() => {
      const shell = window.novalistStores.shell.getState()
      shell.toggleFocusMode()
      setTimeout(() => shell.toggleFocusMode(), 20)
    })
    await expect(h.page.locator('.shell-focus')).toHaveCount(0)
    await expect.poll(windowState).toEqual(maximized)
    await h.page.keyboard.press('F11')
    await expect.poll(async () => (await windowState()).fullScreen).toBe(true)
    await h.page.reload()
    await expect.poll(windowState).toEqual(maximized)
  } finally { await h.close() }
})
