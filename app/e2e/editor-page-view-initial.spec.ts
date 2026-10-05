import { test, expect } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

test.beforeEach(async ({ page }) => {
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  await page.evaluate(async () => {
    await document.fonts.ready
    const editor = window as unknown as EditorWindow
    editor.setContent('<p id="paragraph">Prefix </p>')
    document.getElementById('editor')!.focus()
    window.getSelection()!.setPosition(document.getElementById('paragraph')!.firstChild!, 7)
  })
})

test('enabling page view lays out the paper before returning and keeps the caret', async ({ page }) => {
  const immediate = await page.evaluate(() => {
    const editor = window as unknown as EditorWindow
    const text = document.getElementById('paragraph')!.firstChild!
    editor.setPageView(true)
    const paragraph = document.getElementById('paragraph')!
    return {
      onPage: paragraph.parentElement!.classList.contains('nv-page'),
      top: paragraph.getBoundingClientRect().top,
      sameCaretNode: window.getSelection()!.anchorNode === text,
      caretOffset: window.getSelection()!.anchorOffset
    }
  })
  expect(immediate.onPage).toBe(true)
  expect(immediate.sameCaretNode).toBe(true)
  expect(immediate.caretOffset).toBe(7)
  await page.waitForTimeout(300)
  expect(await page.locator('#paragraph').evaluate(el => el.getBoundingClientRect().top)).toBe(immediate.top)
  await page.keyboard.type('after')
  await expect(page.locator('#editor')).toHaveText('Prefix after')
})

test('enabling page view during composition waits for committed text before wrapping', async ({ page, context }) => {
  const cdp = await context.newCDPSession(page)
  await cdp.send('Input.imeSetComposition', { text: 'nihao', selectionStart: 5, selectionEnd: 5 })
  await page.evaluate(() => (window as unknown as EditorWindow).setPageView(true))
  await page.waitForTimeout(300)
  await expect(page.locator('#editor > .nv-page')).toHaveCount(0)
  await cdp.send('Input.insertText', { text: '你好' })
  await expect(page.locator('#editor > .nv-page')).toHaveCount(1)
  await expect(page.locator('#editor')).toHaveText('Prefix 你好')
})
