import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { transformWithEsbuild } from 'vite'

const source = readFileSync(new URL('../src/renderer/src/views/library/ResearchEditor.tsx', import.meta.url), 'utf8')
const { code } = await transformWithEsbuild(source, 'ResearchEditor.tsx', { loader: 'tsx', format: 'cjs', jsxFactory: 'jsx', jsxFragment: 'fragment' })
function fixture(mobile = true) {
  let error = null
  const module = { exports: {} }
  runInNewContext(code, { module, exports: module.exports, jsx: (type, props, ...children) => ({ type, props, children }),
    window: { novalist: { isMobile: mobile } },
    require: name => name === 'react' ? { useState: initial => [initial, next => { error = next }] }
      : name === './researchModel' ? { isFileType: () => true, TYPES: [], STATUSES: [] } : new Proxy({}, { get: () => () => null })
  })
  const selected = { id: 'image-A', type: 'Image', content: 'media/a.png', tags: [], entityRefs: [], relatedIds: [], fileSize: '', modified: '' }
  const tree = module.exports.ResearchEditor({ selected, t: key => key, isInbox: () => false, items: [], allEntities: [], entityNames: new Map() })
  const target = (src, reason, preview = true) => ({ tagName: 'IMG', matches: () => preview,
    getAttribute: name => name === 'src' ? src : name === 'aria-description' ? reason : null,
    removed: [], removeAttribute(name) { this.removed.push(name) } })
  return { handlers: tree.props, error: () => error, target }
}

test('mobile unresolved URL errors do not become false Research failures, but native size errors stay visible', () => {
  const f = fixture()
  f.handlers.onErrorCapture({ target: f.target('novalist-project://nl/media/a.png') })
  assert.equal(f.error(), null)
  f.handlers.onErrorCapture({ target: f.target('novalist-project://nl/media/a.png', '16 MiB limit') })
  assert.equal(f.error().message, '16 MiB limit')
})

test('successful preview decode clears a previous failure while unrelated content loads do not', () => {
  const f = fixture()
  f.handlers.onErrorCapture({ target: f.target('data:image/png;base64,broken') })
  assert.equal(f.error().message, 'research.previewFailed')
  f.handlers.onLoadCapture({ target: f.target('data:image/png;base64,other', null, false) })
  assert.equal(f.error().message, 'research.previewFailed')
  const loaded = f.target('data:image/png;base64,valid', '16 MiB limit')
  f.handlers.onLoadCapture({ target: loaded })
  assert.equal(f.error(), null)
  assert.deepEqual(loaded.removed, ['aria-description', 'title'])
  f.handlers.onErrorCapture({ target: f.target('data:audio/wav;base64,broken') })
  f.handlers.onLoadedDataCapture({ target: f.target('data:audio/wav;base64,valid') })
  assert.equal(f.error(), null)
})

test('desktop project protocol failures remain visible', () => {
  const f = fixture(false)
  f.handlers.onErrorCapture({ target: f.target('novalist-project://nl/media/missing.png') })
  assert.equal(f.error().message, 'research.previewFailed')
})

test('mobile CSP allows native media data URIs without allowing remote media origins', () => {
  const html = readFileSync(new URL('../src/renderer/index.mobile.html', import.meta.url), 'utf8')
  const policy = html.match(/content="(default-src[^\"]+)"/)[1]
  const directives = new Map(policy.split(';').map(value => value.trim().split(/\s+/)).filter(parts => parts[0]).map(([key, ...values]) => [key, values]))
  assert.deepEqual(directives.get('media-src'), ["'self'", 'data:'])
  assert.deepEqual(directives.get('default-src'), ["'self'"])
})
