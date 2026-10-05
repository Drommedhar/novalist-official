import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('expose controls wait for initial values before accepting a limit edit', async () => {
  const h = await launchApp('novalist-expose-readiness-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    await h.page.evaluate(() => {
      const rpc = window.novalistRpc
      const original = rpc.request.bind(rpc)
      let release!: () => void
      const gate = new Promise<void>(resolve => { release = resolve })
      ;(window as unknown as { releaseExposeRead(): void }).releaseExposeRead = () => {
        release()
        rpc.request = original
      }
      rpc.request = async <T>(method: string, params?: unknown): Promise<T> => {
        const result = await original<T>(method, params)
        if (method === 'expose/get') await gate
        return result
      }
      window.novalistStores.shell.getState().setMainView('expose')
    })
    const view = h.page.locator('.expose-view')
    const limit = h.page.locator('#expose-char-limit')
    await expect(view).toHaveAttribute('aria-busy', 'true')
    await expect(limit).toBeDisabled()
    await expect(h.page.locator('#expose-page-limit')).toBeDisabled()
    await expect(h.page.locator('.expose-export')).toBeDisabled()
    for (const button of await h.page.locator('.expose-style-btn').all()) await expect(button).toBeDisabled()

    await h.page.evaluate(() => (window as unknown as { releaseExposeRead(): void }).releaseExposeRead())
    await expect(view).toHaveAttribute('aria-busy', 'false')
    await limit.fill('10')
    await limit.blur()
    await expect.poll(async () => (await h.rpc<{ charLimit: number }>('expose/get')).charLimit).toBe(10)
    const editor = h.page.frameLocator('.expose-editor .editor-frame').locator('#editor')
    await editor.fill('The loaded limit remains while writing.')
    await expect(h.page.locator('.expose-counter.is-over')).toBeVisible()
    await expect(limit).toHaveValue('10')
  } finally {
    await h.page.evaluate(() => (window as unknown as { releaseExposeRead?: () => void }).releaseExposeRead?.()).catch(() => {})
    await h.close()
  }
})
