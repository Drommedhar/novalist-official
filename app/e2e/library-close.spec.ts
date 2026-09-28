import { test, expect } from '@playwright/test'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

test('closing shows a complete library in the global language on its first render', async () => {
  const h = await launchApp('nl-library-close-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    const cover = join(h.workDir, 'cover.png')
    writeFileSync(cover, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=', 'base64'))
    await h.rpc('dashboard/setCover', [cover])
    await h.page.evaluate(async () => {
      const settings = window.novalistStores.settings.getState()
      await settings.update('global', { language: 'en' })
      await settings.pinSection('appearance')
      await settings.update('project', { language: 'de' })
      await window.novalistStores.project.getState().loadRecents()
    })
    expect(await h.page.evaluate(() => window.novalistStores.settings.getState().view!.effective.language)).toBe('de')

    // Hold the full library response while close runs. The old behavior
    // exposed a German bookshelf with no covers during this interval.
    await h.page.evaluate(() => {
      const client = window.novalistRpc
      const original = client.request.bind(client)
      const state = window as typeof window & { closeProbe: {
        release(): void; held: boolean; finished: boolean; first: { title: string; covers: number; language: string } | null
        releaseStale(): void; staleFinished: boolean
        error?: string
      } }
      let release!: () => void
      const gate = new Promise<void>((resolve) => { release = resolve })
      let releaseStale!: () => void
      let staleReady!: () => void
      const staleGate = new Promise<void>((resolve) => { releaseStale = resolve })
      const staleResponse = new Promise<void>((resolve) => { staleReady = resolve })
      let firstSettings = true
      state.closeProbe = { release, releaseStale, staleFinished: false, held: false, finished: false, first: null }
      const observer = new MutationObserver(() => {
        const library = document.querySelector('.project-library')
        if (!library || state.closeProbe.first) return
        state.closeProbe.first = {
          title: library.querySelector('h1')!.textContent!,
          covers: library.querySelectorAll('img.start-recent-cover-img').length,
          language: window.novalistStores.settings.getState().view!.effective.language
        }
      })
      observer.observe(document.body, { subtree: true, childList: true })
      client.request = async <T>(method: string, params?: unknown): Promise<T> => {
        const result = await original<T>(method, params)
        if (method === 'settings/get' && firstSettings) {
          firstSettings = false
          staleReady()
          await staleGate
        }
        if (method === 'project/recent' && (params as boolean[])?.[0]) {
          state.closeProbe.held = true
          await gate
        }
        return result
      }
      void window.novalistStores.settings.getState().load()
        .finally(() => { state.closeProbe.staleFinished = true })
      void staleResponse.then(() => window.novalistStores.project.getState().closeProject())
        .catch((error: Error) => { state.closeProbe.error = error.message })
        .finally(() => {
          state.closeProbe.finished = true
          client.request = original
          requestAnimationFrame(() => observer.disconnect())
        })
    })
    await expect.poll(() => h.page.evaluate(() => (window as unknown as { closeProbe: { held: boolean } }).closeProbe.held)).toBe(true)
    await expect(h.page.locator('.project-library')).toHaveCount(0)
    await expect(h.page.locator('.shell')).toHaveAttribute('inert', '')
    await h.page.evaluate(() => (window as unknown as { closeProbe: { release(): void } }).closeProbe.release())
    await expect(h.page.locator('.project-library h1')).toHaveText('Your bookshelf')
    await expect(h.page.locator('.project-library img.start-recent-cover-img')).toBeVisible()
    await expect(h.page.locator('.shell')).not.toHaveAttribute('inert')
    expect(await h.page.evaluate(() => (window as unknown as { closeProbe: { first: unknown; error?: string } }).closeProbe.first))
      .toEqual({ title: 'Your bookshelf', covers: 1, language: 'en' })
    expect(await h.page.evaluate(() => (window as unknown as { closeProbe: { error?: string } }).closeProbe.error)).toBeUndefined()

    // A delayed response from before closing must not restore project settings.
    await h.page.evaluate(() => (window as unknown as { closeProbe: { releaseStale(): void } }).closeProbe.releaseStale())
    await expect.poll(() => h.page.evaluate(() => (window as unknown as { closeProbe: { staleFinished: boolean } }).closeProbe.staleFinished)).toBe(true)
    const current = await h.page.evaluate(() => window.novalistStores.settings.getState().view!)
    expect(current.hasProject).toBe(false)
    expect(current.effective.language).toBe('en')
    await expect(h.page.locator('.project-library h1')).toHaveText('Your bookshelf')
  } finally { await h.close() }
})
