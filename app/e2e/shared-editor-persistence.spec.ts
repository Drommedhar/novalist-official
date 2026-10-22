import { expect, test } from '@playwright/test'
import * as Y from 'yjs'
import { dismissTour, launchApp, seedBook } from './harness'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'
import type { ManuscriptSectionDto } from '../src/renderer/src/stores/manuscriptStore'

declare global {
  interface Window {
    sharedPersistenceTest: {
      read(): Promise<string>
      write(update: string): Promise<void>
      ordinaryWrites: number
      annotationScene?: string
    }
  }
}

test('failed Manuscript edits survive reopening and switching to the scene editor without ordinary writes', async () => {
  const h = await launchApp('nl-shared-manuscript-retention-')
  const document = new Y.Doc()
  const prose = document.getText('prose')
  prose.insert(0, 'The story.')
  prose.insert(prose.length, '\n', { block: JSON.stringify([{ id: 'paragraph', tag: 'p', attributes: {} }]) })
  let refuse = true
  let attempts = 0
  try {
    const book = await seedBook(h, { Chapter: ['Shared'] })
    const chapter = book.chapters[0]
    await dismissTour(h.page)
    await h.page.exposeFunction('readSharedTestScene', () => Buffer.from(Y.encodeStateAsUpdate(document)).toString('base64'))
    await h.page.exposeFunction('writeSharedTestScene', (update: string) => {
      attempts++
      if (refuse) throw new Error('Temporary storage failure.')
      Y.applyUpdate(document, new Uint8Array(Buffer.from(update, 'base64')))
    })
    await h.page.evaluate(({ chapter, initial }) => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      const read = Reflect.get(window, 'readSharedTestScene') as () => Promise<string>
      const write = Reflect.get(window, 'writeSharedTestScene') as (update: string) => Promise<void>
      window.sharedPersistenceTest = { read, write, ordinaryWrites: 0 }
      const shared = { sessionId: 'session-one', itemId: 'item-one', state: initial }
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        const args = params as unknown[] | undefined
        if (method === 'collaboration/readScene') return await read() as T
        if (method === 'collaboration/writeScene') return await write(String(args?.[2])) as T
        if (method === 'scenes/write') window.sharedPersistenceTest.ordinaryWrites++
        const result = await original<T>(method, params)
        if (method === 'scenes/read' && args?.[1] === chapter.scenes[0].id) return { ...result, html: '<p>The story.</p>', shared }
        if (method === 'manuscript/get') return (result as ManuscriptSectionDto[]).map(section => ({ ...section,
          scenes: section.scenes.map(scene => ({ ...scene, html: '<p>The story.</p>', shared })) })) as T
        return result
      }
      window.novalistStores.shell.getState().setMainView('manuscript')
    }, { chapter, initial: Buffer.from(Y.encodeStateAsUpdate(document)).toString('base64') })
    const editor = h.page.frameLocator('iframe[title="manuscript"]').locator('.scene-editor')
    await expect(editor).toHaveText('The story.')
    await editor.click()
    await h.page.keyboard.press('Control+End')
    await h.page.keyboard.type(' More.')
    await expect.poll(() => attempts).toBeGreaterThan(0)
    await expect(h.page.getByRole('alert')).toContainText('Shared writing needs attention.')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    await expect(editor).toHaveText('The story. More.')
    await editor.click()
    await h.page.keyboard.press('Control+End')
    await h.page.keyboard.type(' Again.')
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
    const sceneEditor = h.page.frameLocator('iframe[title="editor"]').locator('#editor')
    await expect(sceneEditor).toHaveText('The story. More. Again.')
    refuse = false
    await h.page.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
    await expect.poll(() => prose.toString()).toBe('The story. More. Again.\n')
    await expect.poll(() => h.page.evaluate(() => Object.keys(window.novalistStores.manuscript.getState().sharedEdits))).toEqual([])
    expect(await h.page.evaluate(() => window.sharedPersistenceTest.ordinaryWrites)).toBe(0)
  } finally { await h.close(); document.destroy() }
})

test('the normal editor retains failed shared edits and saves CRDT updates without HTML overwrites', async () => {
  const h = await launchApp('nl-shared-editor-')
  const document = new Y.Doc()
  const prose = document.getText('prose')
  prose.insert(0, 'The story.')
  prose.insert(prose.length, '\n', { block: JSON.stringify([{ id: 'paragraph', tag: 'p', attributes: {} }]) })
  let refuse = true
  let attempts = 0
  try {
    const book = await seedBook(h, { Chapter: ['Shared', 'Ordinary'] })
    const chapter = book.chapters[0]
    await dismissTour(h.page)
    await h.page.exposeFunction('readSharedTestScene', () => Buffer.from(Y.encodeStateAsUpdate(document)).toString('base64'))
    await h.page.exposeFunction('writeSharedTestScene', (update: string) => {
      attempts++
      if (refuse) throw new Error('Temporary storage failure.')
      Y.applyUpdate(document, new Uint8Array(Buffer.from(update, 'base64')))
    })
    await h.page.evaluate(({ chapter, initial }) => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      const read = Reflect.get(window, 'readSharedTestScene') as () => Promise<string>
      const write = Reflect.get(window, 'writeSharedTestScene') as (update: string) => Promise<void>
      window.sharedPersistenceTest = { read, write, ordinaryWrites: 0 }
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        const args = params as unknown[] | undefined
        if (method === 'collaboration/readScene') return await read() as T
        if (method === 'collaboration/writeScene') return await write(String(args?.[2])) as T
        if (method === 'scenes/write' && args?.[1] === chapter.scenes[0].id) window.sharedPersistenceTest.ordinaryWrites++
        const result = await original<T>(method, params)
        if (method === 'scenes/read' && args?.[1] === chapter.scenes[0].id) {
          return { ...result, html: '<p>The story.</p>', shared: { sessionId: 'session-one', itemId: 'item-one', state: initial } }
        }
        return result
      }
    }, { chapter, initial: Buffer.from(Y.encodeStateAsUpdate(document)).toString('base64') })
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('The story.')
    await editor.click()
    await h.page.keyboard.press('Control+End')
    await h.page.keyboard.type(' More.')
    await expect.poll(() => attempts).toBeGreaterThan(0)
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().isDirty)).toBe(true)
    const refused = await h.page.evaluate(async () => {
      try { await window.novalistStores.project.getState().flushPendingSave(); return false }
      catch { return true }
    })
    expect(refused).toBe(true)
    expect(prose.toString()).toBe('The story.\n')
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { language: 'de' }))
    await expect(editor).toHaveText('The story. More.')
    await editor.click()
    await h.page.keyboard.press('Control+End')
    await h.page.keyboard.type(' Again.')
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { language: 'en' }))
    await expect(editor).toHaveText('The story. More. Again.')
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().isDirty)).toBe(true)
    const attemptsBeforeRetry = attempts
    refuse = false
    await h.page.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
    expect(prose.toString()).toBe('The story. More. Again.\n')
    expect(attempts - attemptsBeforeRetry).toBeLessThanOrEqual(4)
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().isDirty)).toBe(false)
    expect(await h.page.evaluate(() => window.sharedPersistenceTest.ordinaryWrites)).toBe(0)
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[1].id), chapter)
    await expect(editor).not.toHaveText('The story. More. Again.')
    await editor.fill('An ordinary scene.')
    await h.page.evaluate(() => window.novalistStores.project.getState().flushPendingSave())
  } finally {
    await h.close()
    document.destroy()
  }
})

test('switching into a shared scene loads its own annotations before saving a new comment', async () => {
  const h = await launchApp('nl-shared-annotations-')
  const document = new Y.Doc()
  document.getText('prose').insert(0, 'Shared prose.')
  document.getText('prose').insert(13, '\n', { block: JSON.stringify([{ id: 'paragraph', tag: 'p', attributes: {} }]) })
  try {
    const book = await seedBook(h, { Chapter: ['Ordinary', 'Shared'] })
    const chapter = book.chapters[0]
    for (const [index, scene] of chapter.scenes.entries()) await h.rpc('scenes/setAnnotations', [chapter.guid, scene.id,
      [{ id: `existing-${index}`, anchorText: '', text: `Comment ${index}`, resolved: false }], []])
    await dismissTour(h.page)
    await h.page.evaluate(({ chapter, initial }) => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.sharedPersistenceTest = { read: async () => initial, write: async () => {}, ordinaryWrites: 0 }
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        if (method === 'collaboration/readScene') return initial as T
        if (method === 'collaboration/writeScene') return undefined as T
        const result = await original<T>(method, params)
        if (method === 'scenes/getAnnotations') window.sharedPersistenceTest.annotationScene = String((params as unknown[])[1])
        if (method === 'scenes/read' && (params as unknown[])[1] === chapter.scenes[1].id)
          return { ...result, html: '<p>Shared prose.</p>', shared: { sessionId: 'session', itemId: 'item', state: initial } }
        return result
      }
    }, { chapter, initial: Buffer.from(Y.encodeStateAsUpdate(document)).toString('base64') })
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
    await expect.poll(() => h.page.evaluate(() => window.sharedPersistenceTest.annotationScene)).toBe(chapter.scenes[0].id)
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[1].id), chapter)
    await expect.poll(() => h.page.evaluate(() => window.sharedPersistenceTest.annotationScene)).toBe(chapter.scenes[1].id)
    await h.page.evaluate(() => {
      const editor = window.document.querySelector<HTMLIFrameElement>('.editor-frame')!.contentWindow as EditorWindow
      editor.getSelection()!.selectAllChildren(editor.document.querySelector('#editor p')!)
      editor.addCommentToSelection('new-shared-comment')
    })
    await expect.poll(async () => (await h.rpc<{ comments: { id: string }[] }>('scenes/getAnnotations', [chapter.guid, chapter.scenes[1].id])).comments.map(comment => comment.id))
      .toEqual(['existing-1', 'new-shared-comment'])
  } finally {
    await h.close()
    document.destroy()
  }
})
