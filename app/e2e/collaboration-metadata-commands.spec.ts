import { expect, test } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

declare global {
  interface Window {
    metadataCommandTest: { grant: boolean; calls: string[]; value: string }
  }
}

test('a scene stage command respects another writer and releases its own temporary lease', async () => {
  const h = await launchApp('nl-metadata-command-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    await dismissTour(h.page)
    await enterWriting(h.page)
    const [stage] = await h.rpc<{ key: string; label: string }[]>('stages/list')
    await h.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.metadataCommandTest = { grant: false, calls: [], value: 'null' }
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        const state = window.metadataCommandTest
        if (method === 'collaboration/participants') return [{ deviceId: 'other', viewId: 'focus', itemId: 'stage-item', name: 'Morgan', selection: null, sceneId: null }] as T
        if (method === 'collaboration/presence') return undefined as T
        if (method === 'collaboration/acquireField') {
          state.calls.push('acquire')
          return { value: state.value, lease: { acquired: state.grant, deviceId: 'other', remainingMilliseconds: 30000 } } as T
        }
        if (method === 'collaboration/writeField') { state.calls.push('write'); state.value = (params as string[])[3]; return undefined as T }
        if (method === 'collaboration/releaseField') { state.calls.push('release'); return true as T }
        if (method === 'stages/setSceneStage') state.calls.push('unguarded')
        return original<T>(method, params)
      }
      const project = window.novalistStores.project.getState()
      window.novalistStores.project.setState({ chapters: project.chapters.map(chapter => ({ ...chapter,
        scenes: chapter.scenes.map(scene => ({ ...scene, sharedFields: { sessionId: 'session', fields: { stage: 'stage-item' } } })) })) })
    })
    const chooseStage = async (): Promise<void> => {
      await h.page.locator('.binder-scene-row').first().click({ button: 'right' })
      await h.page.locator('.context-menu').getByRole('menuitem', { name: `Stage: ${stage.label}`, exact: true }).click()
    }
    await chooseStage()
    await expect(h.page.getByText('Morgan is editing this field.', { exact: true })).toBeVisible()
    await expect.poll(() => h.page.evaluate(() => window.metadataCommandTest.calls)).toEqual(['acquire', 'release'])
    await h.page.evaluate(() => { window.metadataCommandTest.grant = true })
    await chooseStage()
    await expect.poll(() => h.page.evaluate(() => window.metadataCommandTest.calls)).toEqual(['acquire', 'release', 'acquire', 'write', 'release'])
    expect(await h.page.evaluate(() => window.metadataCommandTest.value)).toBe(JSON.stringify(stage.key))
  } finally { await h.close() }
})
