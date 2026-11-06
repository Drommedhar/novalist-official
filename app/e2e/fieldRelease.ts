import { expect, type Locator, type Page } from '@playwright/test'

export async function releaseFocusedField(page: Page, input: Locator): Promise<void> {
  const release = await page.evaluateHandle(() => {
    const original = window.novalistRpc.request
    const request = original.bind(window.novalistRpc)
    const observed = { completed: false, restore: () => { window.novalistRpc.request = original } }
    window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
      const result = await request<T>(method, params)
      if (method === 'collaboration/releaseField') observed.completed = result === true
      return result
    }
    return observed
  })
  try {
    await input.blur()
    // Readonly also covers the saving phase, before the server releases the lease.
    await expect.poll(() => release.evaluate(observed => observed.completed)).toBe(true)
  } finally {
    await release.evaluate(observed => observed.restore())
    await release.dispose()
  }
}
