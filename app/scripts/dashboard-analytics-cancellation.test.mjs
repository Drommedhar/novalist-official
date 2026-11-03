import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))

function fixture(method) {
  let cleanup, call
  const values = []
  const window = { novalist: { isMobile: true } }
  const background = loadRendererSource('src/rpc/backgroundReads.ts', {}, { window })
  const rpc = { request(_method, _params, options) {
    assert.equal(_method, method)
    return new Promise((resolve, reject) => {
      call = { resolve, signal: options?.signal }
      options?.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true })
    })
  } }
  const hook = loadRendererSource('src/views/dashboard/useDashboardRead.ts', {
    react: { useState: () => [null, value => values.push(value)], useEffect: effect => { cleanup = effect() } },
    '../../rpc/client': { rpc }, '../../rpc/backgroundReads': background
  }, { AbortController })
  hook.useDashboardRead(method)
  return { call, values, cleanup: () => cleanup?.(), background }
}

for (const method of ['analytics/book', 'analytics/castAbsence', 'dashboard/echoPhrases']) {
  for (const trigger of ['unmount', 'foreground']) {
    test(`${method} cancels on ${trigger} without publishing an obsolete result`, async () => {
      const f = fixture(method)
      assert.ok(f.call.signal)
      if (trigger === 'unmount') f.cleanup()
      else f.background.cancelBackgroundReads()
      assert.equal(f.call.signal.aborted, true)
      f.call.resolve({ stale: true })
      await turn()
      assert.equal(f.values.length, 0)
    })
  }
  test(`${method} unregisters completed work`, async () => {
    const f = fixture(method)
    assert.ok(f.call.signal)
    f.call.resolve({ ready: true })
    await turn()
    f.background.cancelBackgroundReads()
    assert.equal(f.call.signal.aborted, false)
    assert.deepEqual(f.values, [{ ready: true }])
  })
}

for (const mobile of [false, true]) {
  test(`dashboard initial statistics ${mobile ? 'defer' : 'include'} echo phrases`, () => {
    const calls = []
    const rpc = { request(method, params) { calls.push({ method, params }); return new Promise(() => {}) } }
    const hooks = loadRendererSource('src/views/dashboard/dashboardModel.ts', {
      react: { useState: initial => [initial, () => {}], useEffect: effect => effect() },
      'react-i18next': { useTranslation: () => ({ t: value => value }) },
      '../../rpc/client': { rpc },
      '../../stores/projectStore': { useProjectStore: select => select({ chapters: [] }) }
    }, { window: { novalist: { isMobile: mobile } }, AbortController })

    hooks.useDashboard()

    assert.deepEqual(Array.from(calls.find(call => call.method === 'dashboard/get').params), [30, !mobile])
    assert.equal(calls.some(call => call.method === 'dashboard/echoPhrases'), false)
  })
}
