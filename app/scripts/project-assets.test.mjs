import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise((resolve) => setImmediate(resolve))

function fixture() {
  const requests = []
  const observers = []
  class Element {
    constructor(tag, src) { this.tagName = tag; this.src = src; this.isConnected = true; this.nodeType = 1; this.attributes = {}; this.events = [] }
    getAttribute(name) { return name === 'src' || name === 'data' ? this.src : this.attributes[name] }
    setAttribute(name, value) { if (name === 'src' || name === 'data') this.src = value; else this.attributes[name] = value }
    querySelectorAll() { return [] }
    matches() { return true }
    dispatchEvent(event) { this.events.push(event) }
    addEventListener() {}
  }
  const doc = { nodeType: 9, body: {}, documentElement: {}, assets: [], querySelectorAll(selector) { return selector === 'iframe' ? [] : this.assets } }
  class Observer {
    constructor(callback) { this.callback = callback; observers.push(this) }
    observe() {}
    disconnect() {}
  }
  const loader = loadRendererSource('src/mobile/projectImages.ts', { '../i18n': { default: { t: (key) => key } } }, {
    document: doc, MutationObserver: Observer, Event, CustomEvent, Error, URL, Node: { ELEMENT_NODE: 1 },
    window: { novalist: { isMobile: true, readProjectAsset: (path) => new Promise((resolve, reject) => requests.push({ path, resolve, reject })) } }
  })
  loader.installProjectImageLoader()
  const add = (tag, path) => {
    const element = new Element(tag, 'novalist-project://nl/' + path)
    doc.assets.push(element)
    observers[0].callback([{ type: 'childList', addedNodes: [element] }])
    return element
  }
  const change = (element, path) => {
    element.src = 'novalist-project://nl/' + path
    observers[0].callback([{ type: 'attributes', target: element }])
  }
  return { ...loader, requests, add, change }
}

test('an old image response cannot replace a newer source', async () => {
  const f = fixture()
  const image = f.add('IMG', 'a.png')
  f.change(image, 'b.png')
  f.requests[0].resolve('data:image/png;base64,A')
  await turn()
  assert.equal(image.src, 'novalist-project://nl/b.png')
  f.requests[1].resolve('data:image/png;base64,B')
  await turn()
  assert.equal(image.src, 'data:image/png;base64,B')
})

test('project changes retire pending responses even when the relative path is identical', async () => {
  const f = fixture()
  const image = f.add('IMG', 'cover.png')
  f.clearProjectImageCache()
  f.change(image, 'cover.png')
  f.requests[0].resolve('data:image/png;base64,OLD')
  await turn()
  assert.equal(image.src, 'novalist-project://nl/cover.png')
  f.requests[1].resolve('data:image/png;base64,NEW')
  await turn()
  assert.equal(image.src, 'data:image/png;base64,NEW')
})

test('concurrent asset reads deduplicate and research audio/video/PDF use the host asset reader', async () => {
  const f = fixture()
  const first = f.add('IMG', 'same.png')
  const second = f.add('IMG', 'same.png')
  assert.equal(f.requests.length, 1)
  f.requests[0].resolve('data:image/png;base64,A')
  await turn()
  assert.equal(first.src, second.src)
  for (const [tag, path, mime] of [['AUDIO', 'clip.mp3', 'audio/mpeg'], ['VIDEO', 'clip.mp4', 'video/mp4'], ['IFRAME', 'document.pdf', 'application/pdf']]) {
    const asset = f.add(tag, path)
    f.requests.at(-1).resolve(`data:${mime};base64,A`)
    await turn()
    assert.equal(asset.src, `data:${mime};base64,A`)
  }
})

test('host media errors retain the explicit reason and remain retryable', async () => {
  const f = fixture()
  const image = f.add('IMG', 'large.png')
  f.requests[0].reject(new Error('This media file is too large for the mobile preview (16 MiB limit).'))
  await turn()
  assert.match(image.getAttribute('title'), /16 MiB limit/)
  assert.equal(image.getAttribute('aria-description'), image.getAttribute('title'))
  assert.deepEqual(image.events.map((event) => event.type), ['novalist-asset-error', 'error'])
  const retry = f.resolveProjectAssetUrl('novalist-project://nl/large.png')
  assert.equal(f.requests.length, 2)
  f.requests[1].resolve('data:image/png;base64,A')
  assert.equal(await retry, 'data:image/png;base64,A')
})

test('resolved assets use a bounded least-recently-used cache', async () => {
  const f = fixture()
  const uri = 'data:image/png;base64,' + 'A'.repeat(6 * 1024 * 1024)
  for (const path of ['a.png', 'b.png']) {
    const result = f.resolveProjectAssetUrl('novalist-project://nl/' + path)
    f.requests.at(-1).resolve(uri)
    await result
  }
  await f.resolveProjectAssetUrl('novalist-project://nl/a.png')
  const third = f.resolveProjectAssetUrl('novalist-project://nl/c.png')
  f.requests.at(-1).resolve(uri)
  await third
  await f.resolveProjectAssetUrl('novalist-project://nl/a.png')
  assert.equal(f.requests.length, 3)
  const evicted = f.resolveProjectAssetUrl('novalist-project://nl/b.png')
  assert.equal(f.requests.length, 4)
  f.requests.at(-1).resolve(uri)
  await evicted
})

test('preview reads enter the native host one at a time and queued duplicates share their result', async () => {
  const f = fixture()
  const first = f.resolveProjectAssetUrl('novalist-project://nl/a.png')
  const second = f.resolveProjectAssetUrl('novalist-project://nl/b.png')
  const duplicate = f.resolveProjectAssetUrl('novalist-project://nl/b.png')
  const third = f.resolveProjectAssetUrl('novalist-project://nl/c.png')
  assert.deepEqual(f.requests.map(request => request.path), ['a.png'])
  f.requests[0].resolve('data:A')
  await turn()
  assert.deepEqual(f.requests.map(request => request.path), ['a.png', 'b.png'])
  f.requests[1].resolve('data:B')
  await turn()
  assert.deepEqual(f.requests.map(request => request.path), ['a.png', 'b.png', 'c.png'])
  f.requests[2].resolve('data:C')
  assert.deepEqual(await Promise.all([first, second, duplicate, third]), ['data:A', 'data:B', 'data:B', 'data:C'])
})

test('a project change skips old queued paths before they reach the new project root', async () => {
  const f = fixture()
  const old = f.resolveProjectAssetUrl('novalist-project://nl/active.png')
  const queued = f.resolveProjectAssetUrl('novalist-project://nl/old-only.png')
  const oldCover = f.resolveProjectAssetUrl('novalist-project://nl/cover.png')
  f.clearProjectImageCache()
  const newCover = f.resolveProjectAssetUrl('novalist-project://nl/cover.png')
  assert.deepEqual(f.requests.map(request => request.path), ['active.png'])
  f.requests[0].resolve('data:OLD')
  await turn()
  assert.deepEqual(f.requests.map(request => request.path), ['active.png', 'cover.png'])
  f.requests[1].resolve('data:NEW')
  assert.deepEqual(await Promise.all([old, queued, oldCover, newCover]), [null, null, null, 'data:NEW'])
})

test('a failed preview rejects its caller and releases the next queued preview', async () => {
  const f = fixture()
  const failed = assert.rejects(f.resolveProjectAssetUrl('novalist-project://nl/broken.png'), /unavailable/)
  const next = f.resolveProjectAssetUrl('novalist-project://nl/good.png')
  assert.equal(f.requests.length, 1)
  f.requests[0].reject(new Error('unavailable'))
  await failed
  await turn()
  assert.equal(f.requests.length, 2)
  f.requests[1].resolve('data:GOOD')
  assert.equal(await next, 'data:GOOD')
})

test('queued DOM previews skip detached elements and superseded sources before entering the host', async () => {
  const f = fixture()
  f.add('IMG', 'active.png')
  const detached = f.add('IMG', 'old-codex.png')
  detached.isConnected = false
  const changed = f.add('IMG', 'old-source.png')
  f.change(changed, 'current.png')
  f.requests[0].resolve('data:ACTIVE')
  await turn()
  assert.deepEqual(f.requests.map(request => request.path), ['active.png', 'current.png'])
  f.requests[1].resolve('data:CURRENT')
  await turn()
  assert.equal(changed.src, 'data:CURRENT')
  assert.equal(detached.events.length, 0)
})

test('a live coalesced DOM consumer keeps its queued preview after the original element detaches', async () => {
  const f = fixture()
  f.add('IMG', 'active.png')
  const detached = f.add('IMG', 'shared.png')
  const live = f.add('IMG', 'shared.png')
  detached.isConnected = false
  f.requests[0].resolve('data:ACTIVE')
  await turn()
  assert.deepEqual(f.requests.map(request => request.path), ['active.png', 'shared.png'])
  f.requests[1].resolve('data:SHARED')
  await turn()
  assert.equal(live.src, 'data:SHARED')
  assert.equal(detached.src, 'novalist-project://nl/shared.png')
})

test('a direct asset consumer keeps a queued read even after every DOM consumer detaches', async () => {
  const f = fixture()
  f.add('IMG', 'active.png')
  const detached = f.add('IMG', 'shared.png')
  const direct = f.resolveProjectAssetUrl('novalist-project://nl/shared.png')
  detached.isConnected = false
  f.requests[0].resolve('data:ACTIVE')
  await turn()
  assert.deepEqual(f.requests.map(request => request.path), ['active.png', 'shared.png'])
  f.requests[1].resolve('data:SHARED')
  assert.equal(await direct, 'data:SHARED')
})
