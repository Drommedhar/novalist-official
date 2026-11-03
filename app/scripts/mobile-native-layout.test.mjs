import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

test('native tablet layout applies before opening a project and follows window resize', () => {
  let layout
  const window = { novalist: { requestLayout() {} } }
  const document = { documentElement: { dataset: {} } }
  const module = loadRendererSource('src/mobile/nativeLayout.ts', {
    '../stores/shellStore': { useShellStore: { getState: () => ({ setMobileLayout(value) { layout = value } }) } }
  }, { window, document })
  module.installNativeLayout('tablet')
  assert.equal(layout, 'tablet')
  assert.equal(document.documentElement.dataset.mobileLayout, 'tablet')
  window.__novalistLayout('phone')
  assert.equal(layout, 'phone')
  assert.equal(document.documentElement.dataset.mobileLayout, 'phone')
})
