import assert from 'node:assert/strict'
import test from 'node:test'
import { buildSync } from 'esbuild'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const output = fileURLToPath(new URL('../out/collaboration-document-tests/documents.cjs', import.meta.url))
buildSync({
  stdin: { contents: "export * from './src/main/collaboration/ProseDocumentHost'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, platform: 'node', format: 'cjs', packages: 'external', outfile: output
})
const require = createRequire(import.meta.url)
const Y = require('yjs')
const { ProseDocumentHost } = require(output)

for (const html of [
  '<p><strong class="novalist-entity" data-id="mira"><em>Imported</em> prose</strong>.</p>',
  '<p>Imported prose.</p>\n',
  '<p>First.</p>\n<p>Second.</p>\n',
  'Before <strong>bold</strong><p>A paragraph.</p>After'
]) test(`imported scene markup roundtrips exactly: ${html}`, () => {
  const owner = new ProseDocumentHost()
  const guest = new ProseDocumentHost()
  try {
    guest.load('imported', owner.seed('imported', html))
    assert.equal(owner.project('imported'), html)
    assert.equal(guest.project('imported'), html)
    const writer = new Y.Doc()
    try {
      Y.applyUpdate(writer, guest.state('imported'))
      const previous = Y.encodeStateVector(writer)
      writer.getText('prose').insert(0, 'New ')
      const update = Y.encodeStateAsUpdate(writer, previous)
      owner.receive('imported', update)
      guest.receive('imported', update)
      assert.equal(owner.project('imported'), guest.project('imported'))
      assert.ok(owner.project('imported').includes('New '))
    } finally { writer.destroy() }
  } finally { owner.dispose(); guest.dispose() }
})

test('unopened scenes merge edits and retain shared identities across restart', () => {
  const owner = new ProseDocumentHost()
  const guest = new ProseDocumentHost()
  try {
    const state = owner.seed('scene-a', '<p>Before <b>shared</b> after.</p>')
    guest.load('scene-a', state)
    const writer = new Y.Doc()
    Y.applyUpdate(writer, state)
    const edits = []
    writer.on('update', update => edits.push(update))
    writer.getText('prose').insert(7, 'new ')
    for (const update of edits) {
      owner.receive('scene-a', update)
      guest.receive('scene-a', update)
    }
    assert.equal(owner.project('scene-a'), '<p>Before new <b>shared</b> after.</p>')
    assert.equal(guest.project('scene-a'), owner.project('scene-a'))
    const restored = new ProseDocumentHost()
    try {
      restored.load('scene-a', owner.state('scene-a'))
      for (const update of edits) restored.receive('scene-a', update)
      assert.equal(restored.project('scene-a'), owner.project('scene-a'))
    } finally { restored.dispose() }
    writer.destroy()
  } finally { owner.dispose(); guest.dispose() }
})

test('malformed out-of-order formatting cannot poison later valid scene updates', () => {
  const host = new ProseDocumentHost()
  const writer = new Y.Doc()
  try {
    const state = host.seed('scene-a', '<p>Story.</p>')
    Y.applyUpdate(writer, state)
    const changes = []
    writer.on('update', update => changes.push(update))
    writer.getText('prose').insert(0, 'New ')
    writer.getText('prose').format(0, 1, { 'span::0': 'not-json' })
    assert.throws(() => host.receive('scene-a', changes[1]))
    assert.deepEqual(host.state('scene-a'), state)
    host.receive('scene-a', changes[0])
    assert.equal(host.project('scene-a'), '<p>New Story.</p>')
  } finally { writer.destroy(); host.dispose() }
})

test('dependency-incomplete snapshots and deletion baselines are rejected atomically', () => {
  const host = new ProseDocumentHost()
  const writer = new Y.Doc()
  try {
    const baseline = host.seed('scene-a', '<p>Story.</p>')
    Y.applyUpdate(writer, baseline)
    const updates = []
    writer.on('update', update => updates.push(update))
    writer.getText('prose').insert(0, 'New ')
    assert.throws(() => host.load('incomplete', updates[0]))
    assert.throws(() => host.remove('scene-a', updates[0]))
    host.load('incomplete', baseline)
    assert.equal(host.project('scene-a'), '<p>Story.</p>')
    assert.equal(host.project('incomplete'), '<p>Story.</p>')
  } finally { writer.destroy(); host.dispose() }
})

test('unrepresentable scene content cannot acquire a shared identity or silently disappear', () => {
  const host = new ProseDocumentHost()
  try {
    for (const html of ['<p><audio controls src="voice.mp3"></audio>Words</p>', '<table><tbody><tr><td>Cell</td></tr></tbody></table>']) {
      assert.throws(() => host.seed('scene-a', html))
    }
    host.seed('scene-a', '<p>Kept intact.</p>')
    assert.equal(host.project('scene-a'), '<p>Kept intact.</p>')
  } finally { host.dispose() }
})

test('pending semantic errors are reported without blocking the valid updates they depended on', () => {
  const host = new ProseDocumentHost()
  const writer = new Y.Doc()
  try {
    const baseline = host.seed('scene-a', '<p>Story.</p>')
    Y.applyUpdate(writer, baseline)
    const prose = writer.getText('prose')
    const block = prose.toDelta().at(-1).attributes.block
    const updates = []
    writer.on('update', update => updates.push(update))
    prose.insert(0, 'New ')
    prose.format(0, 1, { block })
    host.receive('scene-a', updates[1])
    assert.throws(() => host.state('scene-a'), /missing history/)
    assert.equal(host.project('scene-a'), '<p>Story.</p>')
    const rejected = host.receive('scene-a', updates[0])
    assert.equal(rejected.length, 1)
    assert.equal(host.project('scene-a'), '<p>New Story.</p>')
    assert.equal(host.receive('scene-a', updates[0]).length, 0)
    assert.equal(host.project('scene-a'), '<p>New Story.</p>')
  } finally { writer.destroy(); host.dispose() }
})

test('valid updates arriving before their history merge when a deletion baseline supplies it', () => {
  const host = new ProseDocumentHost()
  const writer = new Y.Doc()
  try {
    const baseline = host.seed('scene-a', '<p>Story.</p>')
    Y.applyUpdate(writer, baseline)
    const updates = []
    writer.on('update', update => updates.push(update))
    writer.getText('prose').insert(0, 'New ')
    const deletedAt = Y.encodeStateAsUpdate(writer)
    writer.getText('prose').insert(4, 'shared ')
    assert.equal(host.receive('scene-a', updates[1]).length, 0)
    assert.equal(host.project('scene-a'), '<p>Story.</p>')
    assert.throws(() => host.state('scene-a'), /missing history/)
    assert.equal(host.remove('scene-a', deletedAt).length, 0)
    assert.equal(host.project('scene-a'), null)
    assert.equal(host.recovered('scene-a'), '<p>New shared Story.</p>')
  } finally { writer.destroy(); host.dispose() }
})

test('a deleted scene stays deleted while concurrent and later edits survive in a recovered copy', () => {
  const owner = new ProseDocumentHost()
  const guest = new ProseDocumentHost()
  const restarted = new ProseDocumentHost()
  const writer = new Y.Doc()
  try {
    const baseline = owner.seed('scene-a', '<p>Story.</p>')
    guest.load('scene-a', baseline)
    Y.applyUpdate(writer, baseline)
    const edits = []
    writer.on('update', update => edits.push(update))
    writer.getText('prose').insert(0, 'New ')
    guest.receive('scene-a', edits[0])

    owner.remove('scene-a', baseline)
    assert.equal(owner.project('scene-a'), null)
    assert.equal(owner.recovered('scene-a'), null)
    owner.receive('scene-a', edits[0])
    guest.remove('scene-a', baseline)
    assert.equal(owner.project('scene-a'), null)
    assert.equal(guest.project('scene-a'), null)
    assert.equal(owner.recovered('scene-a'), '<p>New Story.</p>')
    assert.equal(guest.recovered('scene-a'), '<p>New Story.</p>')

    restarted.load('scene-a', owner.state('scene-a'), owner.deletions('scene-a'))
    writer.getText('prose').insert(4, 'shared ')
    restarted.receive('scene-a', edits[1])
    restarted.remove('scene-a', baseline)
    assert.equal(restarted.project('scene-a'), null)
    assert.equal(restarted.recovered('scene-a'), '<p>New shared Story.</p>')
    assert.equal(restarted.deletions('scene-a').length, 1)
  } finally { writer.destroy(); owner.dispose(); guest.dispose(); restarted.dispose() }
})
