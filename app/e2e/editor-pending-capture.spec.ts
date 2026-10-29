import { expect, test, type Page } from '@playwright/test'
import { mobileHost } from './mobileHarness'
import { type Book } from './harness'

const prose = '<p>First paragraph.</p><p>Second paragraph.</p><ul><li>One item.</li><li>Another item.</li></ul>'

async function createScenes(page: Page, root: string, html: string) {
  return page.evaluate(async ({ root, html }) => {
    const rpc = window.novalistRpc
    await rpc.request('project/create', [root, 'Capture', 'Book'])
    const chapter = await rpc.request<Book>('project/createChapter', ['Chapter'])
    const chapterGuid = chapter.chapters[0].guid
    await rpc.request('project/createScene', [chapterGuid, 'First'])
    const book = await rpc.request<Book>('project/createScene', [chapterGuid, 'Second'])
    const [first, second] = book.chapters[0].scenes
    await rpc.request('scenes/write', [chapterGuid, first.id, html, 'First paragraph. Second paragraph. One item. Another item.'])
    await rpc.request('scenes/write', [chapterGuid, second.id, '<p>Second scene.</p>', 'Second scene.'])
    window.novalistStores.project.getState().applyState(await rpc.request('project/getState') as never)
    await window.novalistStores.project.getState().openScene(chapterGuid, first.id)
    return { chapterGuid, first: first.id, second: second.id }
  }, { root, html })
}

for (const [name, html] of [
  ['block separators', prose],
  ['HTML entity and attribute normalization', prose.replace('First paragraph.', 'First&#160;paragraph.').replace('<p>', "<p title='First'>")]
]) test(`reading and switching scenes does not save ${name}`, async ({ page }) => {
  const host = await mobileHost(page, 'tablet', 'android')
  try {
    const scenes = await createScenes(page, host.root, html)
    const editor = page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toContainText('Second paragraph.')
    const result = await page.evaluate(async ({ chapterGuid, first, second }) => {
      const writes: unknown[] = []
      const request = window.novalistRpc.request.bind(window.novalistRpc)
      window.novalistRpc.request = (method, params, options) => {
        if (method === 'scenes/write') writes.push(params)
        return request(method, params, options)
      }
      await window.novalistStores.project.getState().openScene(chapterGuid, second)
      await window.novalistStores.project.getState().openScene(chapterGuid, first)
      return writes
    }, scenes)
    expect(result).toEqual([])
  } finally { await host.close() }
})

test('switching scenes captures typing before the delayed editor message', async ({ page }) => {
  const host = await mobileHost(page, 'tablet', 'android')
  try {
    const scenes = await createScenes(page, host.root, prose)
    await expect(page.frameLocator('.editor-frame').locator('#editor')).toContainText('Second paragraph.')
    const result = await page.evaluate(async ({ chapterGuid, first, second }) => {
      const frame = document.querySelector<HTMLIFrameElement>('.editor-frame')!
      const doc = frame.contentDocument!
      const paragraph = doc.querySelector('#editor p')!
      const range = doc.createRange()
      range.selectNodeContents(paragraph)
      range.collapse(false)
      doc.getSelection()!.removeAllRanges()
      doc.getSelection()!.addRange(range)
      ;(doc.getElementById('editor') as HTMLElement).focus()
      doc.execCommand('insertText', false, ' Buffered typing.')
      const beforeNavigation = window.novalistStores.project.getState().isDirty
      await window.novalistStores.project.getState().openScene(chapterGuid, second)
      const saved = await window.novalistRpc.request<{ html: string }>('scenes/read', [chapterGuid, first])
      return { beforeNavigation, html: saved.html }
    }, scenes)
    expect(result.beforeNavigation).toBe(false)
    expect(result.html).toContain('Buffered typing.')
  } finally { await host.close() }
})
