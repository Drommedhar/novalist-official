import { test, expect } from '@playwright/test'
import { join } from 'node:path'
import { readFileSync, writeFileSync } from 'node:fs'
import { dismissTour, launchApp, seedBook } from './harness'

test('startup and closing do not retry manifests of unavailable library projects', async () => {
  const h = await launchApp('nl-library-offline-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    const real = await h.rpc<{ projectPath: string; projectName: string }>('project/getState')
    const settingsPath = join(h.workDir, 'settings', 'settings.json')
    const settings = JSON.parse(readFileSync(settingsPath, 'utf8'))
    settings.recentProjects = [
      { path: real.projectPath, name: real.projectName },
      ...Array.from({ length: 10 }, (_, i) => ({
        path: join(h.workDir, `disconnected-${i}`, 'Novel'), name: `Offline ${i}`
      }))
    ]
    writeFileSync(settingsPath, JSON.stringify(settings))
    const closeMs = await h.page.evaluate(async () => {
      const start = performance.now()
      await window.novalistStores.project.getState().closeProject()
      return Math.round(performance.now() - start)
    })
    await expect(h.page.locator('.start-recent-card')).toHaveCount(11)
    // Install before reload so initial hydration, not a later manual refresh,
    // is measured. A reconnect callback used to run it a second time on boot.
    await h.page.addInitScript(() => {
      const state = window as typeof window & { startupPings: number }
      state.startupPings = 0
      Object.defineProperty(window, 'novalistRpc', {
        configurable: true,
        set(client: typeof window.novalistRpc) {
          Object.defineProperty(window, 'novalistRpc', { configurable: true, writable: true, value: client })
          const original = client.request.bind(client)
          client.request = <T>(method: string, params?: unknown): Promise<T> => {
            if (method === 'system/ping') state.startupPings++
            return original<T>(method, params)
          }
        }
      })
    })
    const start = Date.now()
    await h.page.reload()
    await expect(h.page.locator('.start-recent-card')).toHaveCount(11)
    const startupMs = Date.now() - start
    console.log('Library with ten unavailable projects:', { closeMs, startupMs })
    expect(await h.page.evaluate(() => (window as unknown as { startupPings: number }).startupPings)).toBe(1)
    // A missing manifest formerly incurred five attempts with 400 ms of
    // backoff per project, on every full refresh. This rejects that behavior.
    expect(closeMs).toBeLessThan(2000)
    expect(startupMs).toBeLessThan(3000)
  } finally { await h.close() }
})
