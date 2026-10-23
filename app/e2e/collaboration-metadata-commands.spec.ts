import { expect, test } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, seedBook } from './harness'

declare global {
  interface Window {
    metadataCommandTest: { grant: boolean; calls: string[]; value: string }
  }
}

for (const command of [
  { field: 'stage', label: '', value: '' },
  { field: 'label', label: 'Label: Evidence', value: 'evidence' },
  { field: 'isFavorite', label: 'Pin to top', value: true },
  { field: 'inactive', label: 'Take out of the book', value: true },
  { field: 'excludeFromExport', label: 'Hold back from exports', value: true },
  { field: 'wordTarget', label: 'Set word target...', value: 1200 }
]) test(`a scene ${command.field} command respects another writer and releases its temporary lease`, async () => {
  const h = await launchApp('nl-metadata-command-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    await h.rpc('labels/set', [[{ key: 'evidence', label: 'Evidence', color: '#5a80b0' }]])
    await dismissTour(h.page)
    await enterWriting(h.page)
    const [stage] = await h.rpc<{ key: string; label: string }[]>('stages/list')
    await h.page.evaluate(field => {
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
        if (['stages/setSceneStage', 'labels/setScene', 'binder/pinScene', 'scenes/setInactive', 'sceneBulk/setExportInclusion', 'targets/setScene'].includes(method)) state.calls.push('unguarded')
        return original<T>(method, params)
      }
      const project = window.novalistStores.project.getState()
      window.novalistStores.project.setState({ chapters: project.chapters.map(chapter => ({ ...chapter,
        scenes: chapter.scenes.map(scene => ({ ...scene, sharedFields: { sessionId: 'session', fields: { [field]: 'stage-item' } } })) })) })
    }, command.field)
    const chooseStage = async (): Promise<void> => {
      await h.page.locator('.binder-scene-row').first().click({ button: 'right' })
      await h.page.locator('.context-menu').getByRole('menuitem', { name: command.label || `Stage: ${stage.label}`, exact: true }).click()
      if (command.field === 'wordTarget') {
        await h.page.getByRole('dialog').getByRole('textbox').fill('1200')
        await h.page.getByRole('dialog').getByRole('button', { name: 'OK', exact: true }).click()
      }
    }
    await chooseStage()
    await expect(h.page.getByRole('status', { name: 'Morgan is editing this field.', exact: true })).toBeVisible()
    await expect.poll(() => h.page.evaluate(() => window.metadataCommandTest.calls)).toEqual(['acquire', 'release'])
    await h.page.evaluate(() => { window.metadataCommandTest.grant = true })
    await chooseStage()
    await expect.poll(() => h.page.evaluate(() => window.metadataCommandTest.calls)).toEqual(['acquire', 'release', 'acquire', 'write', 'release'])
    expect(await h.page.evaluate(() => window.metadataCommandTest.value)).toBe(JSON.stringify(command.field === 'stage' ? stage.key : command.value))
  } finally { await h.close() }
})
