import { expect, test } from '@playwright/test'
import { build } from 'vite'
import { resolve } from 'node:path'
import * as Y from 'yjs'
import type {} from './fixtures/markdownCollaboration'

test('concurrent Markdown edits merge and each writer undoes only their own text', async ({ page }) => {
  const output = resolve('out/markdown-collaboration-test')
  await build({ configFile: false, logLevel: 'error', build: { outDir: output,
    lib: { entry: resolve('e2e/fixtures/markdownCollaboration.ts'), name: 'MarkdownCollaboration', formats: ['iife'], fileName: () => 'harness.js' } } })
  const document = new Y.Doc()
  document.getText('prose').insert(0, '**Original** <literal>.')
  const state = Array.from(Y.encodeStateAsUpdate(document))
  document.destroy()
  await page.setContent('<div id="writer-0"></div><div id="writer-1"></div>')
  await page.addScriptTag({ path: resolve(output, 'harness.js') })
  await page.evaluate(state => window.markdownCollaboration.start(state), state)
  const first = page.locator('#writer-0 .cm-content')
  const second = page.locator('#writer-1 .cm-content')
  await first.click()
  await page.keyboard.press('Control+Home')
  await page.keyboard.type('Alice ')
  await second.click()
  await page.keyboard.press('Control+End')
  await page.keyboard.insertText(' Bob 中文')
  await expect(first).toHaveText('Alice **Original** <literal>.')
  await expect(second).toHaveText('**Original** <literal>. Bob 中文')
  await page.evaluate(() => window.markdownCollaboration.exchange())
  await expect(first).toHaveText('Alice **Original** <literal>. Bob 中文')
  await expect(second).toHaveText('Alice **Original** <literal>. Bob 中文')
  await first.click()
  await page.keyboard.press('Control+z')
  await page.evaluate(() => window.markdownCollaboration.exchange())
  await expect(first).toHaveText('**Original** <literal>. Bob 中文')
  await expect(second).toHaveText('**Original** <literal>. Bob 中文')
})
