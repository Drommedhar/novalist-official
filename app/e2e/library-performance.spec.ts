import { test, expect } from '@playwright/test'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

test('library refreshes keep cover payloads out of the writing workspace', async () => {
  const h = await launchApp('nl-library-performance-')
  try {
    const book = await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    // Valid PNG followed by padding, representing a typical full-size cover.
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=', 'base64')
    const cover = join(h.workDir, 'cover.png')
    writeFileSync(cover, Buffer.concat([png, Buffer.alloc(2 * 1024 * 1024)]))
    await h.rpc('dashboard/setCover', [cover])
    await h.page.evaluate((chapter) => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), book.chapters[0])
    await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toBeVisible()

    const measurement = await h.page.evaluate(async () => {
      const client = window.novalistRpc
      const original = client.request.bind(client)
      let calls = 0, bytes = 0
      client.request = async <T>(method: string, params?: unknown): Promise<T> => {
        const result = await original<T>(method, params)
        if (method === 'project/recent') {
          calls++
          bytes += JSON.stringify(result).length
        }
        return result
      }
      const started = performance.now()
      try {
        await Promise.all(Array.from({ length: 8 }, () => window.novalistStores.project.getState().loadRecents()))
        return { calls, bytes, elapsedMs: Math.round(performance.now() - started) }
      } finally { client.request = original }
    })
    console.log('Library refresh workload:', measurement)
    expect(measurement.calls).toBeLessThanOrEqual(2)
    expect(measurement.bytes).toBeLessThan(10_000)
    expect(await h.page.evaluate(async () => {
      const project = window.novalistStores.project
      const before = project.getState().recentProjects
      await project.getState().loadRecents()
      return project.getState().recentProjects === before
    })).toBe(true)
    // A menu refresh that completes after closing must not replace the library
    // with its deliberately lightweight response.
    await h.page.evaluate(async () => {
      const client = window.novalistRpc
      const original = client.request.bind(client)
      let release!: () => void
      let responseReady!: () => void
      const held = new Promise<void>((resolve) => { release = resolve })
      const ready = new Promise<void>((resolve) => { responseReady = resolve })
      let first = true
      client.request = async <T>(method: string, params?: unknown): Promise<T> => {
        const result = await original<T>(method, params)
        if (method === 'project/recent' && first) {
          first = false
          responseReady()
          await held
        }
        return result
      }
      try {
        const refresh = window.novalistStores.project.getState().loadRecents()
        await ready
        await window.novalistStores.project.getState().closeProject()
        release()
        await refresh
      } finally { release(); client.request = original }
    })
    await expect(h.page.locator('.project-library .start-recent-cover-img')).toBeVisible()
    await expect(h.page.getByText('1 book · World Bible')).toBeVisible()
  } finally { await h.close() }
})
