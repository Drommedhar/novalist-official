import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

function render({ layout, width, mobile = true }) {
  return loadRendererSource('src/shell/useIsPhone.ts', {
    react: { useState: initial => [initial(), () => {}], useEffect() {} },
    '../stores/shellStore': { useShellStore: select => select({ mobileLayout: layout }) }
  }, { window: { novalist: { isMobile: mobile }, matchMedia: () => ({ matches: width <= 700 }) } }).useIsPhone()
}

test('native tablet layout stays a tablet when its sidebar narrows the web content', () => {
  assert.equal(render({ layout: 'tablet', width: 560 }), false)
})

test('native phone layout stays compact in a resized window', () => {
  assert.equal(render({ layout: 'phone', width: 800 }), true)
})

test('desktop does not acquire phone controls in a narrow window', () => {
  assert.equal(render({ layout: 'phone', width: 400, mobile: false }), false)
})
