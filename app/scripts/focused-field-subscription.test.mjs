import assert from 'node:assert/strict'
import test from 'node:test'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'

// Production React scheduling matters here: autofocus runs during commit,
// before this hook's passive effect subscribes to the real field controller.
const bundle = await build({
  stdin: { resolveDir: fileURLToPath(new URL('..', import.meta.url)), loader: 'tsx', contents: `
    import React, { useEffect } from 'react'
    import { createRoot } from 'react-dom/client'
    import { useFocusedField } from './src/renderer/src/collaboration/useFocusedField'
    import { flushPendingWrites } from './src/renderer/src/stores/pendingWrites'
    let root, controller
    function Field() {
      const field = useFocusedField({ sessionId: 'session', itemId: 'dateRange' }, '')
      controller = field.controller
      useEffect(() => { window.mounted() }, [])
      return <div onFocusCapture={() => void controller.focus()} onBlurCapture={field.blur}>
        <input type="date" autoFocus value={field.view.value} readOnly={field.view.phase !== 'editing'}
          onChange={event => controller.change(event.target.value)} />
      </div>
    }
    export function mount() {
      root = createRoot(document.getElementById('root'))
      root.render(<Field />)
    }
    export function snapshot() { return controller.snapshot }
    export const flush = flushPendingWrites
    export async function dispose() { root.unmount(); await flushPendingWrites() }
  ` },
  bundle: true, write: false, platform: 'browser', format: 'iife', globalName: 'fieldFixture', jsx: 'automatic',
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [{ name: 'field-services', setup(build) {
    build.onResolve({ filter: /\/rpc\/client$/ }, () => ({ path: 'rpc', namespace: 'fixture' }))
    build.onResolve({ filter: /\/SharedItemPresence$/ }, () => ({ path: 'presence', namespace: 'fixture' }))
    build.onLoad({ filter: /.*/, namespace: 'fixture' }, args => ({ contents: args.path === 'rpc'
      ? 'export const rpc = window.fieldRpc'
      : 'export class SharedItemPresence { close() {} }' }))
  } }]
})

const grant = { value: '2031-04-06', lease: { acquired: true, deviceId: 'owner', remainingMilliseconds: 30_000 } }

async function fixture(t, acquisition) {
  const dom = new JSDOM('<div id="root"></div>', { url: 'http://localhost/', runScripts: 'outside-only', pretendToBeVisual: true })
  const calls = []
  let mounted
  const ready = new Promise(resolve => { mounted = resolve })
  dom.window.mounted = mounted
  dom.window.fieldRpc = {
    request(method, params) {
      calls.push({ method, params })
      return method === 'collaboration/acquireField' ? acquisition : Promise.resolve()
    }
  }
  dom.window.eval(bundle.outputFiles[0].text)
  const app = dom.window.fieldFixture
  t.after(async () => { await app.dispose(); dom.window.close() })
  app.mount()
  await ready
  const input = dom.window.document.querySelector('input')
  const editable = () => new Promise((resolve, reject) => {
    const finish = (error) => { observer.disconnect(); dom.window.clearTimeout(timer); error ? reject(error) : resolve() }
    const check = () => { if (!input.readOnly) finish() }
    const observer = new dom.window.MutationObserver(check)
    const timer = dom.window.setTimeout(() => {
      try { assert.equal(input.readOnly, false, 'the autofocus grant must also reach the rendered input'); finish() }
      catch (error) { finish(error) }
    }, 1_000)
    observer.observe(input, { attributes: true })
    check()
  })
  return { app, calls, dom, editable, input }
}

test('autofocus shows a lease granted before the field subscription and saves subsequent edits', async t => {
  const f = await fixture(t, Promise.resolve(grant))
  assert.equal(f.app.snapshot().phase, 'editing', 'the real controller has acquired its lease')
  assert.equal(f.dom.window.document.activeElement, f.input)
  await f.editable()
  assert.equal(f.input.readOnly, false)
  assert.equal(f.input.value, grant.value)
  const setValue = Object.getOwnPropertyDescriptor(f.dom.window.HTMLInputElement.prototype, 'value').set
  setValue.call(f.input, '2031-04-07')
  f.input.dispatchEvent(new f.dom.window.Event('input', { bubbles: true }))
  assert.equal(f.app.snapshot().value, '2031-04-07')
  f.input.blur()
  await f.app.flush()
  assert.equal(f.calls.find(call => call.method === 'collaboration/writeField').params.at(-1), '2031-04-07')
  assert.ok(f.calls.some(call => call.method === 'collaboration/releaseField'))
})

test('a lease arriving after subscription keeps the input read-only until granted', async t => {
  let resolve
  const acquisition = new Promise(done => { resolve = done })
  t.after(() => resolve(grant))
  const f = await fixture(t, acquisition)
  assert.equal(f.input.readOnly, true)
  assert.equal(f.input.value, '')
  resolve(grant)
  await acquisition
  await f.editable()
  assert.equal(f.input.readOnly, false)
  assert.equal(f.input.value, grant.value)
})
