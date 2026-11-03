import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { transformWithEsbuild } from 'vite'
import { loadRendererSource } from './renderer-source-loader.mjs'

const source = readFileSync(new URL('../src/renderer/src/shell/MobileNav.tsx', import.meta.url), 'utf8')
const { code } = await transformWithEsbuild(source, 'MobileNav.tsx', {
  loader: 'tsx', format: 'cjs', jsxFactory: 'jsx', jsxFragment: 'fragment'
})

function fixture({ platform = 'android', inspector = false, planning = false } = {}) {
  const window = new EventTarget()
  window.novalist = { platform }
  const state = []
  let cursor = 0
  let effects = []
  let cleanups = []
  const calls = []
  const react = {
    createContext: () => ({ Provider: 'provider' }),
    useCallback: fn => fn,
    useMemo: fn => fn(),
    useRef(initial) {
      const slot = cursor++
      state[slot] ??= { current: initial }
      return state[slot]
    },
    useState(initial) {
      const slot = cursor++
      state[slot] ??= initial
      return [state[slot], next => { state[slot] = typeof next === 'function' ? next(state[slot]) : next }]
    },
    useEffect: effect => effects.push(effect)
  }
  const module = { exports: {} }
  runInNewContext(code, {
    module, exports: module.exports, window, Event, fragment: 'fragment',
    jsx: (type, props, ...children) => ({ type, props, children }),
    requestAnimationFrame: fn => fn(),
    require(name) {
      if (name === 'react') return react
      if (name === 'lucide-react' || name === './mobile-nav.css') return {}
      throw new Error(`Unexpected dependency: ${name}`)
    }
  })
  const shell = loadRendererSource('src/mobile/useAndroidBack.ts', {
    react: { useEffect: effect => effect() },
    '../stores/hostBridgeStore': { useHostBridgeStore: { getState: () => ({ pushToast: message => calls.push(message) }) } },
    '../stores/shellStore': { useShellStore: { getState: () => ({ guardLeave: fn => fn() }) } },
    './backNavigation': { handleMobileBack: async () => { calls.push('leave view') } }
  }, { window, Event })
  shell.useAndroidBack(inspector, () => calls.push('close inspector'), planning, () => calls.push('close planning'))
  let dialog = null
  const bridge = loadRendererSource('src/mobile/backEvents.ts', {}, {
    window, Event,
    KeyboardEvent: class extends Event { constructor(type, options) { super(type, options); this.key = options.key } },
    document: { querySelector: () => null, querySelectorAll: () => ({ length: dialog ? 1 : 0, item: () => dialog }), activeElement: null }
  })
  const unmount = () => { cleanups.forEach(fn => fn()); cleanups = [] }
  const render = () => {
    unmount()
    cursor = 0
    effects = []
    const tree = module.exports.MobileNav({ title: 'Settings', renderPage: id => id, children: 'root' })
    cleanups = effects.map(effect => effect()).filter(fn => typeof fn === 'function')
    return tree.props.value
  }
  return { window, render, unmount, calls, back: bridge.dispatchAndroidBack, setDialog: value => { dialog = value } }
}

test('Android Back pops each drilldown page before leaving the current view', () => {
  const f = fixture()
  f.render().push({ id: 'editor', title: 'Editor' })
  f.render().push({ id: 'font', title: 'Font' })
  assert.equal(f.render().depth, 2)
  assert.equal(f.back(), true)
  assert.equal(f.render().depth, 1)
  assert.deepEqual(f.calls, [])
  f.back()
  assert.equal(f.render().depth, 0)
  assert.deepEqual(f.calls, [])
  f.back()
  assert.deepEqual(f.calls, ['leave view'])
})

for (const overlay of ['inspector', 'planning']) {
  test(`Android Back closes ${overlay} before the underlying drilldown page`, () => {
    const f = fixture({ [overlay]: true })
    f.render().push({ id: 'editor', title: 'Editor' })
    f.render()
    f.back()
    assert.equal(f.render().depth, 1)
    assert.deepEqual(f.calls, [`close ${overlay}`])
  })
}

test('Android Back dismisses a modal before its underlying drilldown page', () => {
  const f = fixture()
  f.render().push({ id: 'editor', title: 'Editor' })
  f.render()
  f.setDialog({ dispatchEvent: event => assert.equal(event.key, 'Escape') })
  assert.equal(f.back(), true)
  assert.equal(f.render().depth, 1)
  assert.deepEqual(f.calls, [])
})

test('unmounted drilldown does not consume Back in the next view', () => {
  const f = fixture()
  f.render().push({ id: 'editor', title: 'Editor' })
  f.render()
  f.unmount()
  assert.equal(f.back(), true)
  assert.deepEqual(f.calls, ['leave view'])
})

test('iOS keeps its existing on-screen drilldown navigation', () => {
  const f = fixture({ platform: 'darwin' })
  f.render().push({ id: 'editor', title: 'Editor' })
  f.render()
  assert.equal(f.back(), false)
  assert.equal(f.render().depth, 1)
  f.render().pop()
  assert.equal(f.render().depth, 0)
  assert.deepEqual(f.calls, [])
})

test('Android Back returns from an entry to its list after popping its nested pages', () => {
  const f = fixture()
  let entryOpen = true
  f.window.addEventListener('novalist-mobile-detail-back', event => {
    if (!entryOpen) return
    event.preventDefault()
    entryOpen = false
  })
  f.render().push({ id: 'relationships', title: 'Relationships' })
  f.render()
  f.back()
  assert.equal(f.render().depth, 0)
  assert.equal(entryOpen, true)
  f.back()
  assert.equal(entryOpen, false)
  assert.deepEqual(f.calls, [])
  f.back()
  assert.deepEqual(f.calls, ['leave view'])
})
