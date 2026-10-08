import { test, expect } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

test.beforeEach(async ({ page }) => {
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  await page.evaluate(async () => { await document.fonts.ready })
})

for (const width of [390, 600, 800]) {
  for (const comments of [false, true]) {
    test(`zoomed page uses the available writing width (${width}px, comments=${comments})`, async ({ page }) => {
      await page.setViewportSize({ width, height: 700 })
      await page.evaluate((withComments) => {
        const editor = window as unknown as EditorWindow
        editor.setFont('Georgia', 32)
        editor.setContent('<p id="paragraph">A reasonably sized writing column has room for these words.</p>')
        editor.setPageView(true)
        if (withComments) {
          const text = document.getElementById('paragraph')!.firstChild!
          window.getSelection()!.setBaseAndExtent(text, 2, text, 12)
          editor.addCommentToSelection('note')
          editor.setCommentsData([{ id: 'note', anchorText: 'reasonably', text: 'Check this description' }])
        }
      }, comments)
      if (comments) await expect(page.locator('body')).toHaveClass(/has-comments/)
      const bounds = await page.locator('#paragraph').evaluate(el => {
        const text = el.getBoundingClientRect()
        const wrapper = document.getElementById('editor-wrapper')!.getBoundingClientRect()
        const paper = el.closest('.nv-page')!.getBoundingClientRect()
        return { textWidth: text.width, available: wrapper.width, left: paper.left - wrapper.left, right: wrapper.right - paper.right }
      })
      // At these widths and font size, side whitespace must not consume the
      // majority of the writing surface, even with a visible comment gutter.
      expect(bounds.textWidth).toBeGreaterThan(bounds.available * 0.8)
      expect(bounds.left).toBeGreaterThanOrEqual(0)
      expect(bounds.right).toBeGreaterThanOrEqual(0)
    })
  }
}

test('wide pages keep book proportions while a narrow book-width limit reflows prose', async ({ page }) => {
  await page.setViewportSize({ width: 1400, height: 900 })
  await page.evaluate(() => {
    const editor = window as unknown as EditorWindow
    editor.setFont('Georgia', 17)
    editor.setContent('<p id="paragraph">A page with comfortable book margins.</p>')
    editor.setPageView(true)
  })
  expect(await page.locator('.nv-page').evaluate(el => el.getBoundingClientRect().width)).toBeCloseTo(32 * 17)
  expect(await page.locator('#paragraph').evaluate(el => el.getBoundingClientRect().width)).toBeCloseTo(24 * 17)
  await page.evaluate(() => {
    const editor = window as unknown as EditorWindow & { setBookWidth(enabled: boolean, width: number): void }
    editor.setBookWidth(true, 400)
    editor.setFont('Georgia', 32)
  })
  expect(await page.locator('#paragraph').evaluate(el => el.getBoundingClientRect().width)).toBeGreaterThan(320)
})

test('page breaks use the finished text width after zoom, resizing and adding a comment', async ({ page }) => {
  await page.setViewportSize({ width: 1200, height: 800 })
  await page.evaluate(() => {
    const editor = window as unknown as EditorWindow
    editor.setFont('Georgia', 17)
    editor.setContent(Array.from({ length: 70 }, (_, i) =>
      `<p id="line-${i}">Paragraph ${i}: A longer sentence should wrap naturally within the paper margins and remain on its page.</p>`
    ).join(''))
    editor.setPageView(true)
  })
  const expectPagesFit = async (): Promise<void> => {
    await expect.poll(() => page.locator('.nv-page').evaluateAll(pages => pages.every(page => {
      const style = getComputedStyle(page)
      // A page containing ordinary paragraphs should fit its content budget;
      // long indivisible paragraphs are deliberately excluded by this fixture.
      return page.getBoundingClientRect().height <= parseFloat(style.minHeight) + 1
    }))).toBe(true)
  }
  await expectPagesFit()
  await page.evaluate(() => (window as unknown as EditorWindow).setFont('Georgia', 32))
  await expectPagesFit()
  await page.setViewportSize({ width: 800, height: 800 })
  await expectPagesFit()
  await page.evaluate(() => {
    const editor = window as unknown as EditorWindow
    const text = document.getElementById('line-0')!.firstChild!
    window.getSelection()!.setBaseAndExtent(text, 0, text, 9)
    editor.addCommentToSelection('note')
    editor.setCommentsData([{ id: 'note', anchorText: 'Paragraph', text: 'Opening note' }])
  })
  await expect(page.locator('body')).toHaveClass(/has-comments/)
  await expectPagesFit()
  await expect(page.locator('#editor p')).toHaveCount(70)
})

test('pagination preserves imported text between paragraphs and the caret inside it', async ({ page }) => {
  const original = 'Leading text.<p>First paragraph.</p>Between <b>bold text.</b><p>Second paragraph.</p>Last.'
  const result = await page.evaluate((html) => {
    const editor = window as unknown as EditorWindow & { repaginatePageView(): void }
    editor.setContent(html)
    const last = document.getElementById('editor')!.lastChild!
    document.getElementById('editor')!.focus()
    window.getSelection()!.setPosition(last, 3)
    editor.setPageView(true)
    editor.repaginatePageView()
    const result = {
      paged: editor.getContent(),
      sameCaret: window.getSelection()!.anchorNode === last,
      caretOffset: window.getSelection()!.anchorOffset
    }
    editor.setPageView(false)
    return { ...result, continuous: editor.getContent() }
  }, original)
  expect(result).toEqual({ paged: original, continuous: original, sameCaret: true, caretOffset: 3 })
})
