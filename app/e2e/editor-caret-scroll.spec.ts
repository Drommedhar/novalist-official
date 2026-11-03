import { expect, test, type Page } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

async function caret(page: Page) {
  return page.evaluate(() => {
    const selection = window.getSelection()!
    const range = selection.getRangeAt(0)
    let rect = range.getBoundingClientRect()
    const paragraph = (selection.focusNode!.nodeType === Node.ELEMENT_NODE
      ? selection.focusNode as Element : selection.focusNode!.parentElement!).closest('p')!
    if (!rect.height) rect = paragraph.getBoundingClientRect()
    const wrapper = document.getElementById('editor-wrapper')!
    return { top: rect.top - wrapper.getBoundingClientRect().top,
      bottom: rect.bottom - wrapper.getBoundingClientRect().top,
      height: wrapper.clientHeight, id: paragraph.id }
  })
}

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 600 })
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  await page.evaluate(async () => {
    await document.fonts.ready
    const editor = window as unknown as EditorWindow
    editor.setContent(Array.from({ length: 60 }, (_, index) => `<p id="line-${index}">${index === 0 || index === 59 ? "A line of prose." : "<br>"}</p>`).join(''))
    document.getElementById('editor')!.focus()
  })
  await expect.poll(() => page.evaluate(() => (window as unknown as { NovalistEditorState: { isFocusing: boolean } }).NovalistEditorState.isFocusing)).toBe(false)
  await page.evaluate(() => window.getSelection()!.setPosition(document.getElementById('line-20')!, 0))
})

test('every arrow step keeps an empty line at the typewriter anchor', async ({ page }) => {
  await page.evaluate(() => (window as unknown as EditorWindow).setTypewriterScroll(true, 'middle'))
  await expect.poll(async () => Math.abs((await caret(page)).top - 300)).toBeLessThan(2)
  for (let line = 21; line <= 25; line++) {
    await page.keyboard.press('ArrowDown')
    await expect.poll(async () => (await caret(page)).id).toBe(`line-${line}`)
    await expect.poll(async () => Math.abs((await caret(page)).top - 300)).toBeLessThan(2)
  }
})

for (const typewriter of [false, true]) {
  test(`arrow navigation resumes following an empty line after wheel scrolling, typewriter=${typewriter}`, async ({ page }) => {
    await page.evaluate((enabled) => {
      const editor = window as unknown as EditorWindow
      editor.setTypewriterScroll(enabled, 'middle')
      const wrapper = document.getElementById('editor-wrapper')!
      wrapper.scrollTop += document.getElementById('line-20')!.getBoundingClientRect().top - 300
    }, typewriter)
    await expect.poll(async () => Math.abs((await caret(page)).top - 300)).toBeLessThan(2)
    await page.mouse.move(450, 300)
    await page.mouse.wheel(0, 500)
    await expect.poll(async () => (await caret(page)).top).toBeLessThan(0)
    await page.keyboard.press('ArrowDown')
    await expect.poll(async () => (await caret(page)).id).toBe('line-21')
    if (typewriter) {
      await expect.poll(async () => Math.abs((await caret(page)).top - 300)).toBeLessThan(2)
    } else {
      await expect.poll(async () => (await caret(page)).top).toBeGreaterThanOrEqual(39)
      const position = await caret(page)
      expect(position.bottom).toBeLessThanOrEqual(position.height - 39)
    }
  })
}

for (const typewriter of [false, true]) {
  for (const zoom of [1, 1.5]) {
    test(`arrow navigation follows an empty first line onto the next book page, typewriter=${typewriter}, zoom=${zoom}`, async ({ page }) => {
      const nextId = await page.evaluate(({ enabled, zoom }) => {
        const editor = window as unknown as EditorWindow
        editor.setPageView(true)
        editor.setWritingZoom(zoom)
        const pages = document.querySelectorAll('.nv-page')
        const previous = pages[0].lastElementChild!
        window.getSelection()!.setPosition(previous, 0)
        editor.setTypewriterScroll(enabled, 'middle')
        const wrapper = document.getElementById('editor-wrapper')!
        wrapper.scrollTop += previous.getBoundingClientRect().top - (enabled ? 300 : 500)
        return pages[1].firstElementChild!.id
      }, { enabled: typewriter, zoom })
      for (let step = 0; step < 4; step++) {
        await page.keyboard.press('ArrowDown')
        await expect.poll(async () => (await caret(page)).id).toBe(`line-${Number(nextId.slice(5)) + step}`)
        if (typewriter) {
          await expect.poll(async () => Math.abs((await caret(page)).top - 300)).toBeLessThan(2)
        } else {
          await expect.poll(async () => (await caret(page)).bottom).toBeLessThanOrEqual(561)
          expect((await caret(page)).top).toBeGreaterThanOrEqual(39)
        }
      }
    })
  }
}
