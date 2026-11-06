import { test, expect } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

/**
 * A context-menu group whose every row needs a selection is not offered when
 * there is none.
 *
 * The Codex group holds exactly two rows and both act on selected text, so
 * with the caret merely parked the flyout opened onto two dead entries. The
 * group name says nothing about wanting a selection, so it read as broken
 * rather than as unavailable. Individual rows still grey out - that is what
 * keeps a menu the same shape from one open to the next - but a group with
 * nothing live in it is dropped.
 */
test('context-menu groups require a selection and stay within the editor', async ({}, testInfo) => {
  test.setTimeout(120_000)

  const h = await launchApp('nl-ctxgroup-')
  const { page } = h
  let completed = false
  try {
    await h.app.context().tracing.start({ screenshots: true, snapshots: true, sources: true })
    await seedBook(h, { 'Chapter One': ['A scene'] }, 'Menus')
    await dismissTour(page)
    await enterWriting(page)

    await expect(page.locator('.binder-scene-row')).toHaveCount(1, { timeout: 20_000 })
    await page.locator('.binder-scene-row').first().click()

    const frame = page.frameLocator('iframe[title="editor"]')
    const editor = frame.locator('#editor')
    await editor.click()
    // Focus restores the previous scroll position asynchronously. Let it finish
    // before typing and opening a menu whose position depends on that viewport.
    await expect.poll(() => editor.evaluate(element =>
      (element.ownerDocument.defaultView as unknown as {
        NovalistEditorState: { isFocusing: boolean }
      }).NovalistEditorState.isFocusing
    )).toBe(false)
    await editor.pressSequentially('Liam Calder walked to the window.')
    const paragraph = editor.locator('p').filter({ hasText: 'Liam Calder walked to the window.' })
    await expect(paragraph).toHaveCount(1)

    const groupNames = async (): Promise<string[]> =>
      frame.locator('#context-menu .cm-parent > .cm-item > span').allTextContents()

    // Caret only. Scene survives - splitting and inserting an image need no
    // selection - and Codex, which is selection-only throughout, is absent.
    await paragraph.click({ button: 'right', position: { x: 8, y: 8 } })
    await expect(frame.locator('#context-menu.visible')).toBeVisible()
    expect(await groupNames()).toEqual(['Scene'])
    await page.keyboard.press('Escape')

    // With prose selected the group comes back, and its rows are live.
    await editor.press('ControlOrMeta+a')
    await expect.poll(() => editor.evaluate(element =>
      element.ownerDocument.getSelection()?.toString() ?? ''
    )).toBe('Liam Calder walked to the window.')
    await paragraph.click({ button: 'right', position: { x: 8, y: 8 } })
    await expect(frame.locator('#context-menu.visible')).toBeVisible()
    expect(await groupNames()).toEqual(['Scene', 'Codex'])
    await expect(
      frame.locator('#context-menu .cm-parent', { hasText: 'Codex' }).locator('.cm-item.disabled')
    ).toHaveCount(0)

    // Flyouts near the bottom must remain inside the iframe, where they can be clicked.
    await page.keyboard.press('Escape')
    await editor.evaluate((element) => element.dispatchEvent(new MouseEvent('contextmenu', {
      bubbles: true, clientX: window.innerWidth - 8, clientY: window.innerHeight - 8
    })))
    const codex = frame.locator('#context-menu .cm-parent', { hasText: 'Codex' })
    await codex.hover()
    const bounds = await codex.locator('.cm-submenu').evaluate((element) => {
      const rect = element.getBoundingClientRect()
      return { top: rect.top, bottom: rect.bottom, height: window.innerHeight }
    })
    expect(bounds.top).toBeGreaterThanOrEqual(0)
    expect(bounds.bottom).toBeLessThanOrEqual(bounds.height)
    await codex.locator('[data-action="appendToEntitySection"]').click()
    await expect(page.locator('.capture-excerpt')).toContainText('Liam Calder walked to the window.')
    completed = true
  } finally {
    try {
      const path = completed ? undefined : testInfo.outputPath('electron-trace.zip')
      await h.app.context().tracing.stop({ path })
      if (path) await testInfo.attach('electron-trace', { path, contentType: 'application/zip' })
    } finally {
      await h.close()
    }
  }
})
