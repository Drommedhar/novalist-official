import { test, expect, type Page } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

type CompositionWindow = EditorWindow & {
  messages: { type: string; plainText?: string }[]
  invokeCSharpAction(json: string): void
}

// Browser unit tests against the shipped editor, including Chromium's native
// composition range. No installed OS input method or grammar server is needed.
test.beforeEach(async ({ page }) => {
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  await page.evaluate(() => {
    const w = window as unknown as CompositionWindow
    w.messages = []
    w.invokeCSharpAction = (json) => w.messages.push(JSON.parse(json))
    w.setContent('<p>Prefix </p>')
    const editor = document.getElementById('editor')!
    editor.focus()
    window.getSelection()!.setPosition(editor.firstChild!.firstChild!, 7)
  })
})

async function messages(page: Page): Promise<CompositionWindow['messages']> {
  return page.evaluate(() => (window as unknown as CompositionWindow).messages)
}

test('page layout during Chinese composition does not leave pinyin in the manuscript (#21)', async ({ page, context }) => {
  await page.evaluate(() => (window as unknown as EditorWindow).setPageView(true))
  await expect(page.locator('#editor > .nv-page')).toHaveCount(1)
  const cdp = await context.newCDPSession(page)
  await cdp.send('Input.imeSetComposition', { text: 'ni', selectionStart: 2, selectionEnd: 2 })
  // Cross the pagination debounce while Chromium owns an active composition.
  await page.waitForTimeout(300)
  await cdp.send('Input.imeSetComposition', { text: 'nihao', selectionStart: 5, selectionEnd: 5 })
  await page.waitForTimeout(300)
  await cdp.send('Input.insertText', { text: '你好' })
  await expect(page.locator('#editor')).toHaveText('Prefix 你好')
  await expect.poll(async () => (await messages(page)).filter(m => m.type === 'contentChanged').at(-1)?.plainText?.trim())
    .toBe('Prefix 你好')
  expect((await messages(page)).filter(m => m.type === 'contentChanged').some(m => /ni/.test(m.plainText ?? ''))).toBe(false)
})

test('a grammar reply cannot split the active composition range (#21)', async ({ page, context }) => {
  const cdp = await context.newCDPSession(page)
  await cdp.send('Input.imeSetComposition', { text: 'nihao', selectionStart: 5, selectionEnd: 5 })
  await page.evaluate(() => (window as unknown as EditorWindow).setGrammarIssues(JSON.stringify([
    { offset: 7, length: 2, type: 'spelling', message: 'Unknown word' }
  ])))
  await expect(page.locator('.grammar-issue')).toHaveCount(0)
  await cdp.send('Input.insertText', { text: '你好' })
  await expect(page.locator('#editor')).toHaveText('Prefix 你好')
  await expect(page.locator('.grammar-issue')).toHaveCount(0)
})

test('IME candidate keys are not intercepted as editor hotkeys', async ({ page }) => {
  const result = await page.evaluate(() => {
    const editor = document.getElementById('editor')!
    editor.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }))
    return ['Escape', 'Enter', 'ArrowDown', 'Tab'].map(key => {
      const event = new KeyboardEvent('keydown', { key, code: key, bubbles: true, cancelable: true, isComposing: true })
      editor.dispatchEvent(event)
      return event.defaultPrevented
    })
  })
  expect(result).toEqual([false, false, false, false])
  expect((await messages(page)).filter(m => m.type === 'hotkey')).toEqual([])
})

test('composition pauses pending checks and resumes them with committed text', async ({ page, context }) => {
  await page.evaluate(() => (window as unknown as EditorWindow).setGrammarCheckEnabled(true))
  const cdp = await context.newCDPSession(page)
  await cdp.send('Input.imeSetComposition', { text: 'nihao', selectionStart: 5, selectionEnd: 5 })
  await page.waitForTimeout(1700)
  expect((await messages(page)).filter(m => ['contentChanged', 'grammarCheckRequest'].includes(m.type))).toEqual([])
  await cdp.send('Input.insertText', { text: '你好' })
  await expect.poll(async () => (await messages(page)).filter(m => m.type === 'grammarCheckRequest').at(-1)?.plainText)
    .toBe('Prefix 你好')
})

test('cancelled composition leaves no pinyin and normal typing still saves', async ({ page, context }) => {
  const cdp = await context.newCDPSession(page)
  await cdp.send('Input.imeSetComposition', { text: 'nihao', selectionStart: 5, selectionEnd: 5 })
  await cdp.send('Input.imeSetComposition', { text: '', selectionStart: 0, selectionEnd: 0 })
  await expect(page.locator('#editor')).toHaveText('Prefix ')
  await page.keyboard.type('after')
  await expect.poll(async () => (await messages(page)).filter(m => m.type === 'contentChanged').at(-1)?.plainText)
    .toBe('Prefix after')
})

test('IME key events are left alone even at the composition event boundary', async ({ page }) => {
  const prevented = await page.evaluate(() => {
    const editor = document.getElementById('editor')!
    return [{ isComposing: true }, { keyCode: 229 }].map(flags => {
      const event = new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true, ...flags })
      editor.dispatchEvent(event)
      return event.defaultPrevented
    })
  })
  expect(prevented).toEqual([false, false])
  expect((await messages(page)).filter(m => m.type === 'hotkey')).toEqual([])
})
