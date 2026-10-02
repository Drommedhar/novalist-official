import { test, expect } from '@playwright/test'
import { codexTerms, splitDictationCommands } from '../src/renderer/src/dictation/dictationOptions'
import { SpeechChunks } from '../src/renderer/src/dictation/audio'

test('English and German standalone commands preserve all prose and its punctuation', () => {
  expect(splitDictationCommands('She waited. New speaker. Hello! Neuer Absatz. Er ging.', true)).toEqual([
    { kind: 'text', text: 'She waited.' }, { kind: 'speaker' }, { kind: 'text', text: 'Hello!' },
    { kind: 'paragraph' }, { kind: 'text', text: 'Er ging.' }
  ])
  expect(splitDictationCommands('New speaker. New paragraph.', true)).toEqual([{ kind: 'speaker' }, { kind: 'paragraph' }])
  for (const prose of ['He wanted a new paragraph.', 'New speaker please.', '“New speaker,” she said.'])
    expect(splitDictationCommands(prose, true)).toEqual([{ kind: 'text', text: prose }])
  expect(splitDictationCommands('New paragraph.', false)).toEqual([{ kind: 'text', text: 'New paragraph.' }])
  expect(splitDictationCommands('neue Sprecherin', true)).toEqual([{ kind: 'speaker' }])
})

test('automatic Codex vocabulary preserves spellings, deduplicates names, and respects recognition limits', () => {
  expect(codexTerms([' Großwald ', '', 'Aeloria', 'aeloria'])).toEqual(['Großwald', 'Aeloria'])
  const terms = codexTerms(['Aeloria', 'aeloria', 'Großwald', ...Array.from({ length: 200 }, (_, i) => `Name ${i}`)])
  expect(terms.slice(0, 2)).toEqual(['Aeloria', 'Großwald'])
  expect(terms).toHaveLength(128)
  expect(codexTerms(['a'.repeat(81), 'A\x00B', 'A\x80B'])).toEqual([])
  const longNames = codexTerms(Array.from({ length: 30 }, (_, i) => `${i}${'a'.repeat(76)}`))
  expect(longNames.reduce((size, term) => size + term.length + 2, 0)).toBeLessThanOrEqual(2000)
})

test('input level distinguishes silence and speech without waiting for a transcription clip', () => {
  const levels: number[] = []
  const clips: Uint8Array[] = []
  const chunks = new SpeechChunks(16000, clip => clips.push(clip), level => levels.push(level))
  chunks.push(new Float32Array(4096))
  chunks.push(new Float32Array(4096).fill(0.02))
  expect(levels[0]).toBe(0)
  expect(levels[1]).toBeGreaterThan(0)
  expect(clips).toHaveLength(0)
})
