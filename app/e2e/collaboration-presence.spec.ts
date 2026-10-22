import { expect, test } from '@playwright/test'
import * as Y from 'yjs'
import { dismissTour, launchApp, seedBook } from './harness'

declare global {
  interface Window {
    presenceTest: { visible: boolean; held: boolean; session: string; rejectSend(): void }
  }
}

test('cursor expiry and a new session remain independent of an old stalled publication', async () => {
  const h = await launchApp('nl-presence-lifecycle-')
  const document = new Y.Doc()
  const prose = document.getText('prose')
  prose.insert(0, 'The story.')
  prose.insert(prose.length, '\n', { block: JSON.stringify([{ id: 'paragraph', tag: 'p', attributes: {} }]) })
  const position = Buffer.from(Y.encodeRelativePosition(Y.createRelativePositionFromTypeIndex(prose, 4))).toString('base64')
  try {
    const book = await seedBook(h, { Chapter: ['Shared', 'Ordinary'] })
    const chapter = book.chapters[0]
    await dismissTour(h.page)
    await h.page.evaluate(({ chapter, initial, position }) => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      let sent = false
      window.presenceTest = { visible: true, held: false, session: 'one', rejectSend() {} }
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        const state = window.presenceTest
        if (method === 'collaboration/status') return { sessionId: state.session, connection: 'Connected', isOwner: true } as T
        if (method === 'collaboration/readScene') return initial as T
        if (method === 'collaboration/presence') {
          if (!sent) { sent = true; return undefined as T }
          if (!(params as unknown[])[4]) return undefined as T
          state.held = true
          return await new Promise<T>((_resolve, reject) => { state.rejectSend = () => reject(new Error('Old publication failed.')) })
        }
        if (method === 'collaboration/participants') return (state.visible ? [{
          deviceId: 'remote', viewId: 'view', itemId: 'item', name: 'Bea', selection: { anchor: position, focus: position }
        }] : []) as T
        const result = await original<T>(method, params)
        if (method === 'scenes/read' && (params as unknown[])[1] === chapter.scenes[0].id)
          return { ...result, html: '<p>The story.</p>', shared: { sessionId: 'one', itemId: 'item', state: initial, presence: true } }
        return result
      }
    }, { chapter, initial: Buffer.from(Y.encodeStateAsUpdate(document)).toString('base64'), position })
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('collaboration'))
    await expect(h.page.getByRole('button', { name: 'End this session', exact: true })).toBeVisible()
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
    const editor = h.page.frameLocator('.editor-frame')
    await expect(editor.locator('.nv-collaboration-label')).toHaveText('Bea')
    await editor.locator('#editor').click()
    await h.page.keyboard.press('ArrowRight')
    await expect.poll(() => h.page.evaluate(() => window.presenceTest.held)).toBe(true)
    await h.page.evaluate(() => { window.presenceTest.visible = false })
    await expect(editor.locator('.nv-collaboration-label')).toHaveCount(0)
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[1].id), chapter)
    await h.page.evaluate(() => {
      window.presenceTest.session = 'two'
      window.novalistStores.shell.getState().openSettings('collaboration')
    })
    await expect(h.page.getByRole('button', { name: 'End this session', exact: true })).toBeVisible()
    await h.page.evaluate(() => window.presenceTest.rejectSend())
    await expect(h.page.getByText('Participant cursors are temporarily unavailable.', { exact: true })).toHaveCount(0)
  } finally { await h.close(); document.destroy() }
})
