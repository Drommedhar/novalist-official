import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'

const context = { window: {} }
runInNewContext(readFileSync(new URL('../src/renderer/public/editor/dialogue-correction.js', import.meta.url), 'utf8'), context)
const { correctEnglishDialogue, correctGermanDialogue } = context.window.NovalistDialogue

test('English dialogue moves punctuation inside the quote and normalizes speech tags', () => {
  for (const text of ['"Hello." Said Noah', '"Hello", Said Noah', '"Hello" Said Noah']) {
    assert.equal(correctEnglishDialogue(text, '"', '"', ['said']), '"Hello," said Noah')
  }
  assert.equal(correctEnglishDialogue('"Why?", asked Noah', '"', '"', ['asked']), '"Why?" asked Noah')
})

test('interrupted dialogue retains sentence boundaries', () => {
  assert.equal(correctEnglishDialogue('"Hello," said Noah "again"', '"', '"', ['said']), '"Hello," said Noah, "again"')
  assert.equal(correctEnglishDialogue('"Hello," said Noah. "Again"', '"', '"', ['said']), null)
})

test('German dialogue retains its quote and punctuation conventions', () => {
  assert.equal(correctGermanDialogue('„Hallo.“ sagte Noah', '„', '“', ['sagte']), '„Hallo“, sagte Noah')
  assert.equal(correctGermanDialogue('„Hallo“, sagte Noah', '„', '“', ['sagte']), null)
})

test('unchanged prose and unsupported speech tags produce no correction', () => {
  assert.equal(correctEnglishDialogue('The sea was quiet.', '"', '"', ['said']), null)
  assert.equal(correctEnglishDialogue('"Hello" thought Noah', '"', '"', ['said']), null)
})
