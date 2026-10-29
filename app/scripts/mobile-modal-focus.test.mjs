import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

test('modal focus starts on Android 13 WebViews without the modal selector', () => {
  const selectors = []
  const document = {
    activeElement: null, body: {},
    querySelectorAll(selector) {
      if (selector.includes(':modal')) throw new SyntaxError('Unsupported selector')
      selectors.push(selector)
      return []
    },
    addEventListener() {}, removeEventListener() {}
  }
  const module = loadRendererSource('src/shell/modalFocus.ts', {}, {
    document, CSS: { supports: () => false }, HTMLElement: class {},
    MutationObserver: class { observe() {} disconnect() {} }
  })
  const dispose = module.installModalFocus()
  assert.ok(selectors[0].includes('dialog[open]'))
  dispose()
})
