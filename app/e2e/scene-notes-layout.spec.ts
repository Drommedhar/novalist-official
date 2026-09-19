import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook } from './harness'

// 1920x1080 at 125% display scaling, plus the narrower window in issue #14.
for (const [width, height] of [[1536, 864], [1153, 650]]) {
  test(`scene notes remain readable and scrollable at ${width}x${height}`, async () => {
    const h = await launchApp('nl-notes-layout-')
    try {
      const book = await seedBook(h, { Chapter: ['Scene'] })
      await dismissTour(h.page)
      await resizeWindow(h, width, height)
      await h.page.evaluate((chapter) =>
        window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id),
      book.chapters[0])
      await h.page.evaluate(() => window.novalistStores.shell.getState().toggleNotesDock())
      const dock = h.page.locator('.notes-dock')
      await expect(dock.locator('#dock-synopsis')).toBeVisible()

      // A visible textarea can still be only a few pixels wide. Measure the
      // actual controls and labels to catch the overlapping columns.
      const layout = await dock.evaluate((element) => {
        const columns = Array.from(element.querySelectorAll('.notes-dock-col'))
        return columns.map((column) => {
          const bounds = column.getBoundingClientRect()
          return {
            width: bounds.width,
            overflowing: Array.from(column.querySelectorAll('input, select, textarea, label'))
              .some((field) => field.getBoundingClientRect().right > bounds.right + 1)
          }
        })
      })
      expect(layout.length).toBeGreaterThanOrEqual(6)
      for (const column of layout) {
        expect(column.width).toBeGreaterThanOrEqual(200)
        expect(column.overflowing).toBe(false)
      }

      const body = dock.locator('.notes-dock-body')
      await body.evaluate((element) => { element.scrollTop = element.scrollHeight })
      expect(await body.evaluate((element) => element.scrollTop)).toBeGreaterThan(0)
      // The last group must be reachable within the dock, without scrolling
      // the entire writing workspace or losing the resize handle.
      await expect(dock.locator('.scene-cast input')).toBeInViewport()
      await expect(dock.locator('.notes-dock-resize')).toBeInViewport()
      await body.evaluate((element) => { element.scrollTop = 0 })
      await dock.locator('#dock-notes').fill('Notes still save after scrolling.')
      await dock.locator('#dock-synopsis').click()
      await expect.poll(() => h.page.evaluate(async () => {
        const { openChapterGuid, openSceneId } = window.novalistStores.project.getState()
        return (await window.novalistRpc.request('scenes/getMeta', [openChapterGuid, openSceneId]) as { notes: string }).notes
      })).toBe('Notes still save after scrolling.')
    } finally {
      await h.close()
    }
  })
}
