import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

function fixture(dialog = null, nativeDialog = null) {
  const events = []
  const window = new EventTarget()
  const module = loadRendererSource('src/mobile/backEvents.ts', {}, {
    window, Event,
    KeyboardEvent: class extends Event { constructor(type, options) { super(type, options); this.key = options.key } },
    document: { querySelector: () => nativeDialog, querySelectorAll: () => ({ length: dialog ? 1 : 0, item: () => dialog }), activeElement: null }
  })
  window.addEventListener('novalist-android-back', event => events.push(event))
  return { back: module.dispatchAndroidBack, window, events }
}

test('Android Back dismisses a modal before navigating the project', () => {
  let key
  const f = fixture({ dispatchEvent(event) { key = event.key } })
  assert.equal(f.back(), true)
  assert.equal(key, 'Escape')
  assert.equal(f.events.length, 0)
})

test('Android Back allows the system to background the app when no view handles it', () => {
  const f = fixture()
  assert.equal(f.back(), false)
  f.window.addEventListener('novalist-android-back', event => event.preventDefault())
  assert.equal(f.back(), true)
})

test('Android Back cancels the native gallery dialog before navigating', () => {
  let cancelled = false
  const native = new EventTarget()
  native.addEventListener('cancel', event => { event.preventDefault(); cancelled = true })
  const f = fixture(null, native)
  assert.equal(f.back(), true)
  assert.equal(cancelled, true)
  assert.equal(f.events.length, 0)
})

test('Android Back closes a native dialog when cancellation is not intercepted', () => {
  const native = new EventTarget()
  let closed = false
  native.close = () => { closed = true }
  const f = fixture(null, native)
  assert.equal(f.back(), true)
  assert.equal(closed, true)
})
