import { expect, test } from '@playwright/test'
import { launchApp, seedBook } from './harness'

test('manuscript successors wait for and inherit the preceding save hash', async () => {
  const h = await launchApp('novalist-manuscript-save-order-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    const sceneId = book.chapters[0].scenes[0].id

    const observed = await h.page.evaluate(
      async ({ sceneId }) => {
        const manuscript = window.novalistStores.manuscript.getState()
        await manuscript.load()
        const rpc = window.novalistRpc
        const originalRequest = rpc.request.bind(rpc)
        const calls: unknown[][] = []
        let releaseFirst!: () => void
        let markFirstStarted!: () => void
        const firstGate = new Promise<void>((resolve) => {
          releaseFirst = resolve
        })
        const firstStarted = new Promise<void>((resolve) => {
          markFirstStarted = resolve
        })

        rpc.request = (async <T,>(method: string, params?: unknown): Promise<T> => {
          if (method !== 'scenes/write') return originalRequest<T>(method, params)
          const index = calls.length
          calls.push(params as unknown[])
          if (index === 0) {
            markFirstStarted()
            await firstGate
          }
          return {
            sceneId,
            wordCount: index + 1,
            hash: index === 0 ? 'hash-after-first' : 'hash-after-second',
            conflicted: false,
            diskHtml: null
          } as T
        }) as typeof rpc.request

        try {
          manuscript.onSceneContentChanged(sceneId, '<p>first</p>', 'first', 1)
          const first = manuscript.flushPendingSave()
          await firstStarted

          window.novalistStores.manuscript
            .getState()
            .onSceneContentChanged(sceneId, '<p>second</p>', 'second', 1)
          const second = window.novalistStores.manuscript.getState().flushPendingSave()
          await new Promise((resolve) => setTimeout(resolve, 0))
          const callsBeforeRelease = calls.length

          releaseFirst()
          await Promise.all([first, second])
          return { callsBeforeRelease, calls }
        } finally {
          releaseFirst()
          rpc.request = originalRequest
        }
      },
      { sceneId }
    )

    expect(observed.callsBeforeRelease).toBe(1)
    expect(observed.calls).toHaveLength(2)
    expect(observed.calls[1][4]).toBe('hash-after-first')
  } finally {
    await h.close()
  }
})

test('editor-first shutdown exposes a manuscript conflict instead of overwriting', async () => {
  const h = await launchApp('novalist-manuscript-overlap-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    const chapterGuid = book.chapters[0].guid
    const sceneId = book.chapters[0].scenes[0].id

    const result = await h.page.evaluate(
      async ({ chapterGuid, sceneId }) => {
        const project = window.novalistStores.project.getState()
        await project.openScene(chapterGuid, sceneId)
        await window.novalistStores.manuscript.getState().load()

        const current = window.novalistStores.project.getState()
        current.onEditorContentChanged(
          current.activeEditorPaneId!,
          '<p>editor version</p>',
          'editor version'
        )
        window.novalistStores.manuscript
          .getState()
          .onSceneContentChanged(sceneId, '<p>manuscript version</p>', 'manuscript version', 2)

        // This is the dangerous callback order: the checked EditorFrame save is
        // acknowledged first, then Manuscript tries to save its competing copy.
        await window.novalistStores.project.getState().flushPendingSave()
        let message = ''
        try {
          await window.novalistStores.manuscript.getState().flushPendingSave()
        } catch (error) {
          message = error instanceof Error ? error.message : String(error)
        }
        const conflict = window.novalistStores.project.getState().sceneConflict
        const beforeResolution = (
          await window.novalistRpc.request<{ html: string }>('scenes/read', [chapterGuid, sceneId])
        ).html

        await window.novalistStores.project
          .getState()
          .resolveSceneConflict('<p>chosen resolution</p>')
        // The rejected manuscript payload was retired by resolution; a later
        // updater flush must not submit it over the chosen text.
        await window.novalistStores.manuscript.getState().flushPendingSave()
        const afterResolution = (
          await window.novalistRpc.request<{ html: string }>('scenes/read', [chapterGuid, sceneId])
        ).html
        return { message, conflict, beforeResolution, afterResolution }
      },
      { chapterGuid, sceneId }
    )

    // The message is localized; the contract is that this flush cannot report
    // success while the conflict it exposed is still unresolved.
    expect(result.message).not.toBe('')
    expect(result.conflict?.mine).toBe('<p>manuscript version</p>')
    expect(result.conflict?.theirs).toBe('<p>editor version</p>')
    expect(result.beforeResolution).toBe('<p>editor version</p>')
    expect(result.afterResolution).toBe('<p>chosen resolution</p>')
  } finally {
    await h.close()
  }
})
