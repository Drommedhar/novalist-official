import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

declare global {
  interface Window {
    fieldTest: { grant: boolean; value: string; failSave: boolean; failRecovery: boolean; recovered: string | null; calls: string[]; finishAcquire(): void }
  }
}

test('Codex waits for its field lock, saves before releasing it, and refuses a competing focus', async () => {
  const h = await launchApp('nl-field-focus-')
  try {
    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(h.page)
    const entity = await h.rpc<{ id: string }>('entities/create', ['character', 'Writer'])
    await h.page.evaluate(async id => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.fieldTest = { grant: true, value: '42', failSave: false, failRecovery: false, recovered: null, calls: [], finishAcquire() {} }
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        const state = window.fieldTest
        if (method === 'collaboration/participants') return [{ deviceId: 'writer', viewId: 'other-focus', itemId: 'age-item', name: 'Morgan', selection: null, sceneId: null }] as T
        if (method === 'collaboration/presence') return undefined as T
        if (method === 'collaboration/acquireField') {
          state.calls.push('acquire')
          await new Promise<void>(resolve => { state.finishAcquire = resolve })
          return { value: state.value, lease: { acquired: state.grant, deviceId: 'writer', remainingMilliseconds: 30000, serverSeq: 0 } } as T
        }
        if (method === 'collaboration/writeField') {
          state.calls.push('save')
          if (state.failSave) throw new Error('Field save unavailable')
          state.value = (params as string[])[3]
          return undefined as T
        }
        if (method === 'collaboration/releaseField') { state.calls.push('release'); return true as T }
        if (method === 'collaboration/recoverField') {
          if (state.failRecovery) throw new Error('Recovery folder unavailable')
          state.recovered = (params as string[])[3]
          return 'C:/fixture/recovered.json' as T
        }
        if (method === 'collaboration/renewField') return { acquired: true, remainingMilliseconds: 30000 } as T
        const result = await original<T>(method, params)
        if (method === 'entities/get') return { ...result, age: state.value, sharedFields: { sessionId: 'session', fields: { age: 'age-item' } } }
        return result
      }
      window.novalistStores.shell.getState().setMainView('codex')
      await window.novalistStores.codex.getState().setType('character')
      await window.novalistStores.codex.getState().select(id)
    }, entity.id)
    const age = h.page.getByRole('textbox', { name: 'Age', exact: true })
    await age.focus()
    await expect(age).toHaveAttribute('readonly', '')
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.calls)).toEqual(['acquire'])
    await h.page.evaluate(() => window.fieldTest.finishAcquire())
    await expect(age).not.toHaveAttribute('readonly', '')
    await age.fill('43')
    await age.blur()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.calls)).toEqual(['acquire', 'save', 'release'])
    await h.page.evaluate(() => { window.fieldTest.grant = false })
    await age.focus()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.calls.length)).toBe(4)
    await h.page.evaluate(() => window.fieldTest.finishAcquire())
    await expect(age).toHaveAttribute('readonly', '')
    await expect(h.page.getByText('Morgan is editing this field.', { exact: true })).toBeVisible()
    await age.blur()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.value)).toBe('43')
    await h.page.evaluate(() => { window.fieldTest.grant = true; window.fieldTest.failSave = true })
    await age.focus()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.calls.filter(call => call === 'acquire').length)).toBe(3)
    await h.page.evaluate(() => window.fieldTest.finishAcquire())
    await expect(age).not.toHaveAttribute('readonly', '')
    await age.fill('Unsent age')
    await age.blur()
    await expect(h.page.getByRole('alert')).toContainText('Field save unavailable')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('write'))
    await expect(age).toHaveCount(0)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await expect(age).toHaveValue('Unsent age')
    await h.page.evaluate(() => { window.fieldTest.failSave = false })
    await age.focus()
    await age.blur()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.value)).toBe('Unsent age')
    await h.page.evaluate(() => { window.fieldTest.failSave = true; window.fieldTest.failRecovery = true })
    await age.focus()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.calls.filter(call => call === 'acquire').length)).toBe(4)
    await h.page.evaluate(() => window.fieldTest.finishAcquire())
    await expect(age).not.toHaveAttribute('readonly', '')
    await age.fill('Recovered age')
    await age.blur()
    await expect(h.page.getByRole('alert')).toContainText('Field save unavailable')
    await h.page.getByRole('button', { name: 'Save a recovery copy', exact: true }).click()
    await expect(h.page.getByRole('alert')).toContainText('Recovery folder unavailable')
    await expect(age).toHaveValue('Recovered age')
    await h.page.evaluate(() => { window.fieldTest.failRecovery = false })
    await h.page.getByRole('button', { name: 'Save a recovery copy', exact: true }).click()
    await expect(h.page.getByText('Your edit was saved in a local recovery copy.', { exact: true })).toBeVisible()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.recovered)).toBe('Recovered age')
    await expect(age).toHaveValue('Unsent age')
    await h.page.evaluate(() => { window.fieldTest.failSave = false })
    await age.focus()
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.calls.at(-1))).toBe('acquire')
    await h.page.evaluate(() => window.fieldTest.finishAcquire())
    await expect(age).not.toHaveAttribute('readonly', '')
    const acquisitions = await h.page.evaluate(() => window.fieldTest.calls.filter(call => call === 'acquire').length)
    await age.evaluate(input => { input.blur(); input.focus() })
    await expect.poll(() => h.page.evaluate(() => window.fieldTest.calls.filter(call => call === 'acquire').length)).toBe(acquisitions + 1)
    await h.page.evaluate(() => window.fieldTest.finishAcquire())
    await expect(age).not.toHaveAttribute('readonly', '')
  } finally { await h.close() }
})
