import { test, expect, type Page } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

type Surface = 'scene' | 'manuscript'
type ManuscriptWindow = Window & {
  setManuscript(json: string): void
  updateSceneContent(id: string, html: string): void
}

async function load(page: Page, surface: Surface, html: string): Promise<void> {
  await page.evaluate(({ surface, html }) => {
    if (surface === 'scene') (window as unknown as EditorWindow).setContent(html)
    else (window as unknown as ManuscriptWindow).setManuscript(JSON.stringify([{
      chapterGuid: 'chapter', chapterTitle: 'Chapter', status: 'Draft', scenes: [
        { sceneId: 'scene', title: 'Scene', wordCount: 2, html }
      ]
    }]))
  }, { surface, html })
}

for (const surface of ['scene', 'manuscript'] as const) {
  const selector = surface === 'scene' ? '#editor' : '.scene-editor'
  test(`${surface}: formatted scene files do not gain blank lines on reload`, async ({ page }) => {
    await page.goto(pathToFileURL(resolve(`src/renderer/public/editor/${surface === 'scene' ? 'editor' : 'manuscript-editor'}.html`)).href)
    // Saved scene revisions can have a source-formatting newline after each
    // paragraph. Those are not author-entered <p><br></p> blank paragraphs.
    await load(page, surface, '<p>First.</p>\r\n  <p>Second.</p>\n')
    const editor = page.locator(selector)
    const gap = await editor.evaluate(root => {
      const paragraphs = root.querySelectorAll('p')
      return paragraphs[1].getBoundingClientRect().top - paragraphs[0].getBoundingClientRect().bottom
    })
    expect(gap).toBe(0)
    for (let round = 0; round < 3; round++) {
      const saved = surface === 'scene'
        ? await page.evaluate(() => (window as unknown as EditorWindow).getContent())
        : await editor.innerHTML()
      expect(saved).toBe('<p>First.</p><p>Second.</p>')
      await load(page, surface, saved)
    }
    if (surface === 'scene') {
      await page.evaluate(() => (window as unknown as EditorWindow).setPageView(true))
      expect(await page.evaluate(() => (window as unknown as EditorWindow).getContent()))
        .toBe('<p>First.</p><p>Second.</p>')
    } else {
      await page.evaluate(() => (window as unknown as ManuscriptWindow).updateSceneContent('scene', '<p>First.</p>\n<p>Second.</p>\n'))
      expect(await editor.innerHTML()).toBe('<p>First.</p><p>Second.</p>')
    }
  })

  test(`${surface}: intentional blank paragraphs, verse and inline spaces survive`, async ({ page }) => {
    await page.goto(pathToFileURL(resolve(`src/renderer/public/editor/${surface === 'scene' ? 'editor' : 'manuscript-editor'}.html`)).href)
    const html = '<p>First.<br>Next line.</p><p><br></p>' +
      '<p class="nv-style-poetry">Verse one\n  Verse two</p>' +
      '<p><b>Two</b> <i>words</i></p><pre>  Code\n\n  here</pre>' +
      'Loose text\nwith a line break'
    await load(page, surface, html)
    expect(await page.locator(selector).innerHTML()).toBe(html)
    await load(page, surface, '<blockquote>\n<p>Quoted.</p>\n<p>Again.</p>\n</blockquote>')
    expect(await page.locator(selector).innerHTML())
      .toBe('<blockquote><p>Quoted.</p><p>Again.</p></blockquote>')
  })

  test(`${surface}: pasting formatted lists and tables keeps word separators`, async ({ page }) => {
    await page.goto(pathToFileURL(resolve(`src/renderer/public/editor/${surface === 'scene' ? 'editor' : 'manuscript-editor'}.html`)).href)
    await load(page, surface, '<p><br></p>')
    const editor = page.locator(selector)
    await editor.click()
    await editor.evaluate(root => {
      const clipboardData = new DataTransfer()
      clipboardData.setData('text/html', '<ul>\n<li>First item</li>\n<li>Second item</li>\n</ul>' +
        '<table><tbody><tr><td>First cell</td> <td>Second cell</td></tr></tbody></table>')
      root.dispatchEvent(new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true }))
    })
    await expect(editor).toContainText('First item\nSecond item')
    await expect(editor).toContainText('First cell Second cell')
  })
}
