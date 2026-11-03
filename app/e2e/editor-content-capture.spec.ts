import { expect, test } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

test('capture ignores pagination, focus and footnote decoration but captures a real pending edit', async ({ page }) => {
  await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  const captured = await page.evaluate(() => {
    const w = window as unknown as EditorWindow
    w.setContent('<p>First paragraph.</p>\n<p>Another paragraph.<sup class="nv-fn" data-fn-id="note">1</sup></p>')
    w.setPageView(true)
    w.setFootnotesData([{ id: 'note', text: 'Footnote text' }])
    document.querySelector('#editor p')!.classList.add('nv-focus-block')
    const previous = document.querySelectorAll('#editor p')[1]
    previous.classList.add('nv-focus-block')
    previous.classList.remove('nv-focus-block')
    return w.capturePendingContentChange()
  })
  expect(captured).toBeNull()
  const edit = await page.evaluate(() => {
    const w = window as unknown as EditorWindow
    document.querySelector('#editor p')!.append(' Added.')
    return { first: w.capturePendingContentChange(), second: w.capturePendingContentChange() }
  })
  expect(edit.first?.html).toContain('First paragraph. Added.')
  expect(edit.first?.html).not.toContain('nv-focus-block')
  expect(edit.first?.html).not.toContain('title=')
  expect(edit.second).toBeNull()
})
