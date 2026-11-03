import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { transformWithEsbuild } from 'vite'
import { loadRendererSource } from './renderer-source-loader.mjs'

const views = [
  { name: 'Codex', path: 'views/codex/CodexView.tsx', component: 'CodexView' },
  { name: 'Wiki', path: 'views/wiki/MobileWikiView.tsx', component: 'MobileWikiView' }
]
for (const view of views) {
  const source = readFileSync(new URL('../src/renderer/src/' + view.path, import.meta.url), 'utf8')
  view.code = (await transformWithEsbuild(source, view.path, {
    loader: 'tsx', format: 'cjs', jsxFactory: 'jsx', jsxFragment: 'fragment'
  })).code
}

function fixture(view, { platform = 'android', layout = 'phone' } = {}) {
  const window = new EventTarget()
  window.novalist = { platform, isMobile: true }
  const cleanups = []
  const detail = loadRendererSource('src/mobile/useMobileDetailBack.ts', {
    react: { useEffect: effect => { const cleanup = effect(); if (cleanup) cleanups.push(cleanup) } }
  }, { window })
  const codex = { entityType: 'character', entities: [], selectedId: 'character', selectedRecord: null }
  const wiki = { currentId: 'article', index: null, article: null, clear: () => { wiki.currentId = null } }
  const useCodexStore = select => select(codex)
  useCodexStore.setState = patch => Object.assign(codex, patch)
  const module = { exports: {} }
  runInNewContext(view.code, {
    module, exports: module.exports, window,
    jsx: (type, props, ...children) => ({ type, props, children }), fragment: 'fragment',
    require(name) {
      if (name.endsWith('/mobile/useMobileDetailBack')) return detail
      if (name.endsWith('/stores/codexStore')) return { useCodexStore }
      if (name.endsWith('/stores/wikiStore')) return { useWikiStore: select => select(wiki) }
      if (name.endsWith('/stores/shellStore')) return { useShellStore: select => select({ mobileLayout: layout }) }
      if (name.endsWith('/useIsPhone')) return { useIsPhone: () => layout === 'phone' }
      if (name === 'react') return {
        useEffect() {}, useMemo: fn => fn(), useRef: () => ({ current: null }), useState: initial => [initial, () => {}]
      }
      if (name === 'react-i18next') return { useTranslation: () => ({ t: key => key }) }
      return new Proxy({}, { get: (_target, key) => key.startsWith('use') ? () => {} : key })
    }
  })
  module.exports[view.component]()
  return {
    back: () => !window.dispatchEvent(new Event('novalist-mobile-detail-back', { cancelable: true })),
    selected: () => view.name === 'Codex' ? codex.selectedId : wiki.currentId,
    unmount: () => cleanups.forEach(cleanup => cleanup())
  }
}

for (const view of views) {
  test(`Android Back returns the real ${view.name} entry view to its list`, () => {
    const f = fixture(view)
    assert.equal(f.back(), true)
    assert.equal(f.selected(), null)
  })
  test(`${view.name} does not consume detail Back after unmounting`, () => {
    const f = fixture(view)
    f.unmount()
    assert.equal(f.back(), false)
    assert.notEqual(f.selected(), null)
  })
  test(`${view.name} detail handling leaves iOS navigation unchanged`, () => {
    const f = fixture(view, { platform: 'darwin' })
    assert.equal(f.back(), false)
    assert.notEqual(f.selected(), null)
  })
}

test('tablet Codex keeps its selection when leaving the list-and-detail view', () => {
  const f = fixture(views[0], { layout: 'tablet' })
  assert.equal(f.back(), false)
  assert.notEqual(f.selected(), null)
})
