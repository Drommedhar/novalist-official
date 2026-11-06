import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import test from 'node:test'
import { JSDOM } from 'jsdom'

const scripts = ['purify.min.js', 'scene-sanitizer.js', 'scene-html.js', 'manuscript-editor.js']
  .map(name => readFileSync(new URL(`../src/renderer/public/editor/${name}`, import.meta.url), 'utf8'))
const ornaments = ['***', '* * * *', '#', '_ _ _', '• • •', '~ ~ ~', '- * ~', '&nbsp;*&nbsp;*&nbsp;*&nbsp;']

function fixture(t) {
  const dom = new JSDOM('<div id="manuscript-content"></div>', { runScripts: 'outside-only' })
  t.after(() => dom.window.close())
  dom.window.invokeCSharpAction = () => {}
  scripts.forEach(source => dom.window.eval(source))
  return {
    document: dom.window.document,
    render(html, shared = false) {
      dom.window.setManuscript(JSON.stringify([{ chapterGuid: 'chapter', chapterTitle: 'Chapter', scenes: [
        { sceneId: 'scene', title: 'Scene', html, shared, wordCount: 2 }
      ] }]))
      return dom.window.document.querySelector('.scene-editor')
    }
  }
}

test('initial Manuscript rendering preserves scene-break classification and formatted HTML spacing', t => {
  const f = fixture(t)
  const html = ['<p id="ordinary">Ordinary prose.</p>',
    ...ornaments.map((text, index) => `<p id="break-${index}">${text}</p>`),
    '<p class="nv-style-poetry">Verse\n  continuation</p>'].join('\n  ')
  const editor = f.render(html)
  for (const [index] of ornaments.entries()) {
    assert.ok(editor.querySelector(`#break-${index}`).classList.contains('nv-scene-break'), `ornament ${index} stays unindented`)
  }
  assert.equal(editor.querySelector('#ordinary').classList.contains('nv-scene-break'), false)
  assert.equal([...editor.childNodes].some(node => node.nodeType === 3), false, 'source whitespace between blocks stays removed')
  assert.equal(editor.querySelector('.nv-style-poetry').textContent, 'Verse\n  continuation')
})

test('refreshing existing Manuscript prose classifies new breaks and clears obsolete break markers', t => {
  const f = fixture(t)
  const editor = f.render('<p id="changing">Original prose.</p>')
  const updated = f.render('<p id="changing">***</p><p id="ordinary">Next paragraph.</p>')
  assert.equal(updated, editor, 'the scene editor itself is retained')
  assert.ok(updated.querySelector('#changing').classList.contains('nv-scene-break'))
  f.render('<p id="changing" class="nv-scene-break">Ordinary prose again.</p>')
  assert.equal(editor.querySelector('#changing').classList.contains('nv-scene-break'), false)
})

test('a Manuscript snapshot never replaces live shared scene nodes', t => {
  const f = fixture(t)
  const editor = f.render('<p id="break">***</p><p id="live">Initial shared prose.</p>', true)
  const paragraph = editor.querySelector('#live')
  const sceneBreak = editor.querySelector('#break')
  paragraph.textContent = 'Unsaved shared prose.'
  f.render('<p>Older snapshot.</p>', true)
  assert.equal(editor.querySelector('#live'), paragraph)
  assert.equal(paragraph.textContent, 'Unsaved shared prose.')
  assert.equal(editor.querySelector('#break'), sceneBreak)
  assert.ok(sceneBreak.classList.contains('nv-scene-break'))
})
