import { test, expect } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'
import { previewRule } from '../src/renderer/src/views/settings/replacementPreview'

const rule = { kind: 'literal' as const, start: "'", end: "'", startReplace: '“', endReplace: '”' }

test.beforeEach(async ({ page }) => {
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  await page.evaluate((pair) => {
    const editor = window as unknown as EditorWindow
    editor.setAutoReplacements(JSON.stringify([pair]))
    editor.setContent('<p><br></p>')
  }, rule)
  await page.locator('#editor').click()
})

for (const [sample, expected] of [
  ["don't", "don't"],
  ["'don't go,' she said.", "“don't go,” she said."],
  ["the boys' coats", "the boys' coats"],
  ["She said 'hello there' and 'goodbye'.", 'She said “hello there” and “goodbye”.'],
  ["'für's Buch'", "“für's Buch”"],
  ["'René's here.'", "“René's here.”"],
  ["'cafe\u0301's open'", "“cafe\u0301's open”"],
  ["'the 1980's'", "“the 1980's”"],
  ["('hello')", '(“hello”)']
]) {
  test(`quotes use word boundaries: ${sample}`, async ({ page }) => {
    await page.keyboard.type(sample)
    await expect(page.locator('#editor')).toHaveText(expected)
    expect(previewRule(rule, sample).result).toBe(expected)
  })
}

test('an apostrophe inserted between formatted letters is not dialogue', async ({ page }) => {
  await page.evaluate(() => {
    const editor = window as unknown as EditorWindow
    editor.setContent('<p>“don<b>t</b> go”</p>')
    const text = document.querySelector('#editor p')!.firstChild!
    window.getSelection()!.setPosition(text, 4)
  })
  await page.keyboard.type("'")
  await expect(page.locator('#editor')).toHaveText("“don't go”")
  await expect(page.locator('#editor b')).toHaveText('t')
})

test('a custom quotation pair keeps contractions separate from its closing mark', async ({ page }) => {
  const custom = { ...rule, startReplace: '« ', endReplace: ' »' }
  await page.evaluate(pair => (window as unknown as EditorWindow).setAutoReplacements(JSON.stringify([pair])), custom)
  const sample = "'don't go'"
  await page.keyboard.type(sample)
  await expect(page.locator('#editor')).toHaveText("« don't go »")
  expect(previewRule(custom, sample).result).toBe("« don't go »")
})

test('moving the caret commits a closing quote before further editing', async ({ page }) => {
  await page.keyboard.type("'hello'")
  await page.keyboard.press('ArrowLeft')
  await page.keyboard.press('ArrowRight')
  await page.keyboard.type('s')
  await expect(page.locator('#editor')).toHaveText('“hello”s')
})

test('pasted quotes and ordinary replacement rules remain independent', async ({ page }) => {
  await page.evaluate((pair) => {
    const editor = window as unknown as EditorWindow
    editor.setContent('<p>“don”</p>')
    const text = document.querySelector('#editor p')!.firstChild!
    window.getSelection()!.setPosition(text, text.textContent!.length)
    editor.setAutoReplacements(JSON.stringify([
      pair,
      { start: '--', end: '--', startReplace: '—', endReplace: '—' },
      { kind: 'regex', start: '(\\d+)x(\\d+)', startReplace: '$1×$2', end: '', endReplace: '' }
    ]))
  }, rule)
  await page.keyboard.type('t -- 12x9')
  await expect(page.locator('#editor')).toHaveText('“don”t — 12×9')
})
