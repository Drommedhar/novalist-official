import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

declare global { interface Window { manuscriptLoad: { calls: number; release(): void } } }

test('Manuscript shows loading instead of empty statistics and requests its initial data once', async () => {
  const app = await launchApp('nl-manuscript-loading-')
  try {
    await seedBook(app, { Opening: ['Arrival'] })
    await dismissTour(app.page)
    await app.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.manuscriptLoad = { calls: 0, release() {} }
      const pending = new Promise<void>(resolve => { window.manuscriptLoad.release = resolve })
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        if (method === 'manuscript/get') { window.manuscriptLoad.calls++; await pending }
        return original<T>(method, params)
      }
      window.novalistStores.shell.getState().setMainView('manuscript')
    })
    await expect(app.page.locator('.manuscript').getByRole('status')).toHaveText('Loading...')
    await expect(app.page.locator('iframe[title="manuscript"]')).toBeHidden()
    expect(await app.page.evaluate(() => window.manuscriptLoad.calls)).toBe(1)
    await app.page.evaluate(() => window.manuscriptLoad.release())
    await expect(app.page.frameLocator('iframe[title="manuscript"]').locator('.scene-editor')).toHaveCount(1)
    await expect(app.page.locator('.manuscript').getByRole('status')).toHaveCount(0)
  } finally { await app.close() }
})
