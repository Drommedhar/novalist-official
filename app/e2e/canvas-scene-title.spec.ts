import { test, expect } from '@playwright/test'
import { launchApp, seedBook, dismissTour } from './harness'

test('a promoted card and its manuscript scene share a live title', async () => {
  const h = await launchApp('nl-card-title-')
  try {
    await dismissTour(h.page)
    const book = await seedBook(h, { One: [] })
    await dismissTour(h.page)
    const chapterGuid = book.chapters[0].guid
    const board = await h.rpc<{ id: string }>('canvas/create', ['Ideas'])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('canvas'))
    await h.page.locator('.canvas-toolbar').getByRole('button', { name: 'Add card' }).click()
    const title = h.page.locator('.canvas-card-title')
    await title.fill('Original idea')
    await h.page.getByRole('button', { name: 'Make this a scene' }).click()
    await expect(h.page.locator('.canvas-card-badge')).toHaveText('In the manuscript')
    const linked = await h.rpc<{ cards: { id: string; sceneId: string }[] }>('canvas/load', [board.id])
    const sceneId = linked.cards[0].sceneId

    await title.fill('Renamed on the board')
    await expect.poll(async () => {
      const state = await h.rpc<{ chapters: { scenes: { title: string }[] }[] }>('project/getState')
      return state.chapters[0].scenes[0].title
    }).toBe('Renamed on the board')

    // The board stays mounted, as it can in a split pane beside the binder.
    await h.page.evaluate(async ({ chapterGuid, sceneId }) => {
      await window.novalistStores.project.getState().renameScene(chapterGuid, sceneId, 'Renamed in the manuscript')
    }, { chapterGuid, sceneId })
    await expect(title).toHaveValue('Renamed in the manuscript')
    await expect(h.page.getByRole('button', { name: 'Make this a scene' })).toBeDisabled()
    const reloaded = await h.rpc<{ cards: { title: string }[] }>('canvas/load', [board.id])
    expect(reloaded.cards[0].title).toBe('Renamed in the manuscript')

    const { projectPath } = await h.rpc<{ projectPath: string }>('project/getState')
    await h.page.evaluate(() => {
      const control = window as unknown as {
        boardRefreshStarted: boolean
        releaseBoardRefresh: () => void
      }
      const rpc = window.novalistRpc
      const original = rpc.request.bind(rpc)
      let saved = false
      rpc.request = (async <T,>(method: string, params?: unknown): Promise<T> => {
        const result = await original<T>(method, params)
        if (method === 'canvas/save') saved = true
        if (method === 'project/getState' && saved) {
          rpc.request = original
          await new Promise<void>((resolve) => {
            control.releaseBoardRefresh = resolve
            control.boardRefreshStarted = true
          })
        }
        return result
      }) as typeof rpc.request
    })
    await title.fill('Saved while closing')
    await h.page.waitForFunction(() =>
      (window as unknown as { boardRefreshStarted: boolean }).boardRefreshStarted)
    // Closing during the autosave's state refresh must wait for the entire
    // save, without spinning on an already-resolved board-write promise.
    await h.page.evaluate(async () => {
      const closing = window.novalistStores.project.getState().closeProject()
      setTimeout(() => (window as unknown as { releaseBoardRefresh: () => void }).releaseBoardRefresh(), 25)
      await closing
    })
    await h.page.evaluate((path) => window.novalistStores.project.getState().openProject(path), projectPath)
    const reopened = await h.rpc<{ cards: { title: string }[] }>('canvas/load', [board.id])
    expect(reopened.cards[0].title).toBe('Saved while closing')
  } finally {
    await h.close()
  }
})
