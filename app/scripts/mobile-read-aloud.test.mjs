import assert from 'node:assert/strict'
import test from 'node:test'
import { readFileSync } from 'node:fs'
import { createContext, runInContext } from 'node:vm'

test('a WebView without browser voices reads through the native engine and stops on failure', () => {
  const messages = []
  const window = { NovalistEditorState: {}, sendMessage: message => messages.push(message) }
  const context = createContext({ window })
  runInContext(readFileSync(new URL('../src/renderer/public/editor/read-aloud.js', import.meta.url), 'utf8'), context)
  context.proseBlocks = () => [{}]
  context.sentenceRangesIn = () => [{ text: 'Hello.', range: {} }, { text: 'Second sentence.', range: {} }]
  window.startReadAloud(false, 1, null)
  assert.equal(messages.find(message => message.type === 'speakSentence')?.text, 'Hello.')
  assert.equal(messages.filter(message => message.type === 'speakSentence').length, 1)
  window.onSentenceSpoken(false)
  assert.equal(window.NovalistEditorState.readAloudActive, false)
  assert.equal(messages.filter(message => message.type === 'speakSentence').length, 1)
  assert.equal(messages.at(-1).speaking, false)
})
