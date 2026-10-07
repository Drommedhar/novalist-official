import { test, expect, type Page } from '@playwright/test'
import { launchApp, seedBook, type Harness } from './harness'

async function caretInParagraph(page: Page, index: number): Promise<void> {
  await page.frameLocator('.editor-frame').locator('#editor').evaluate((element, position) => {
    const paragraph = element.querySelectorAll('p')[position]
    const range = document.createRange()
    range.setStart(paragraph.firstChild ?? paragraph, 0)
    range.collapse(true)
    const selection = window.getSelection()!
    selection.removeAllRanges()
    selection.addRange(range)
  }, index)
}

async function shape(page: Page): Promise<string[]> {
  return page.frameLocator('.editor-frame').locator('#editor').evaluate((element) =>
    Array.from(element.querySelectorAll('p')).map((paragraph) =>
      paragraph.querySelector('img') ? '[img]' : (paragraph.textContent ?? '').trim()
    )
  )
}

async function rememberImageTargetThroughMenu(h: Harness): Promise<void> {
  // The OS picker remains pending while the host rebuilds the editor, without
  // depending on a platform's desktop portal or opening an interactive dialog.
  await h.app.evaluate(({ dialog }) => {
    dialog.showOpenDialog = () => new Promise<{ canceled: boolean; filePaths: string[] }>((resolve) => {
      Object.assign(globalThis, { cancelImagePicker: () => resolve({ canceled: true, filePaths: [] }) })
    })
  })
  await caretInParagraph(h.page, 1)
  const frame = h.page.frameLocator('.editor-frame')
  await frame.locator('#editor p').nth(1).click({ button: 'right' })
  const group = frame.locator('.cm-parent', { has: frame.locator('.cm-item[data-action="insertImage"]') })
  await expect(group).toBeVisible()
  await group.hover()
  await frame.locator('.cm-item[data-action="insertImage"]').click()
  await expect.poll(() => h.app.evaluate(() =>
    typeof (globalThis as unknown as { cancelImagePicker?: unknown }).cancelImagePicker
  )).toBe('function')
  expect(await frame.locator('#editor').evaluate(() => {
    const saved = (window as unknown as { NovalistEditorState: { imageTargetBlock: { text: string; tag: string; index: number } | null } }).NovalistEditorState.imageTargetBlock
    return saved && { text: saved.text, tag: saved.tag, index: saved.index }
  })).toEqual({ text: 'Second', tag: 'P', index: 1 })
}

async function rebuildWithoutSelection(h: Harness): Promise<void> {
  await h.page.frameLocator('.editor-frame').locator('#editor').evaluate(() => {
    const frame = window as unknown as { getContent(): string; setContent(html: string): void }
    frame.setContent(frame.getContent())
    window.getSelection()?.removeAllRanges()
  })
  await h.app.evaluate(() =>
    (globalThis as unknown as { cancelImagePicker(): void }).cancelImagePicker()
  )
}

test('an image survives a prose rebuild at the remembered caret', async () => {
  const h = await launchApp('nl-insert-image-')
  try {
    await seedBook(h, { One: ['A'] })
    await h.page.locator('.binder-scene-row').first().click()
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible({ timeout: 30_000 })
    await editor.evaluate(() =>
      (window as unknown as { setContent(html: string): void }).setContent('<p>First</p><p>Second</p><p>Third</p>')
    )
    await rememberImageTargetThroughMenu(h)
    await rebuildWithoutSelection(h)
    await editor.evaluate(() =>
      (window as unknown as { insertImageAtCaret(path: string, alt: string): void }).insertImageAtCaret('Images/plan.png', 'the map')
    )
    expect(await shape(h.page)).toEqual(['First', 'Second', '[img]', 'Third'])
    await expect(editor.locator('img')).toHaveAttribute('alt', 'the map')
    await expect(editor.locator('img')).toHaveAttribute('data-nv-src', 'Images/plan.png')
  } finally { await h.close() }
})

test('an image survives a prose rebuild at the remembered caret in page view', async () => {
  const h = await launchApp('nl-insert-image-page-')
  try {
    await seedBook(h, { One: ['A'] })
    await h.page.locator('.binder-scene-row').first().click()
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible({ timeout: 30_000 })
    await editor.evaluate(() => {
      const frame = window as unknown as { setContent(html: string): void; setPageView(enabled: boolean): void }
      frame.setContent('<p>First</p><p>Second</p><p>Third</p>')
      frame.setPageView(true)
    })
    await expect(h.page.frameLocator('.editor-frame').locator('.nv-page')).not.toHaveCount(0)
    await rememberImageTargetThroughMenu(h)
    await rebuildWithoutSelection(h)
    await editor.evaluate(() =>
      (window as unknown as { insertImageAtCaret(path: string, alt: string): void }).insertImageAtCaret('Images/plan.png', '')
    )
    expect(await shape(h.page)).toEqual(['First', 'Second', '[img]', 'Third'])
    expect(await editor.locator('img').evaluate((element) => !!element.closest('.nv-page'))).toBe(true)
  } finally { await h.close() }
})
