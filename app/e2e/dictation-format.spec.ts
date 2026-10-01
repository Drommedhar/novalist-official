import { test, expect } from '@playwright/test'
import { appendDictationContext, dictationQuotes, formatDictation, type DictationSegment } from '../src/renderer/src/dictation/formatDictation'
import { SpeechChunks, wav, openSystemMicrophone } from '../src/renderer/src/dictation/audio'

const speech = (text: string, newParagraph = true): DictationSegment => ({ text, kind: 'dialogue', newParagraph })
const narration = (text: string): DictationSegment => ({ text, kind: 'narration', newParagraph: false })
const tag = (text: string): DictationSegment => ({ text, kind: 'attribution', newParagraph: false })

test('native microphone stop drains an in-flight read before its final clip, exactly once', async () => {
  const emitted: number[] = []
  let release!: (value: { clips: string[]; ended: boolean }) => void
  let stops = 0
  const microphone = await openSystemMicrophone({
    start: async () => {},
    read: () => new Promise((resolve) => { release = resolve }),
    stop: async () => { stops++; return { clips: [btoa(String.fromCharCode(2))], ended: true } }
  }, (clip) => emitted.push(clip[0]), () => {}, new AbortController().signal)
  const stopping = microphone.stop()
  const repeated = microphone.stop()
  expect(stops).toBe(0)
  release({ clips: [btoa(String.fromCharCode(1))], ended: false })
  await Promise.all([stopping, repeated])
  expect(emitted).toEqual([1, 2])
  expect(stops).toBe(1)
})

test('cancellation while native microphone permission opens releases capture without polling', async () => {
  const abort = new AbortController()
  let stops = 0
  await expect(openSystemMicrophone({
    start: async () => { abort.abort() },
    read: async () => { throw new Error('Cancelled capture must not poll') },
    stop: async () => { stops++; return { clips: [], ended: true } }
  }, () => {}, () => {}, abort.signal)).rejects.toThrow('Aborted')
  expect(stops).toBe(1)
})

test('English dialogue starts a paragraph; its tag stays on that paragraph', () => {
  expect(formatDictation([narration('He waited.'), speech('Hello.'), tag('she said.'), speech('Goodbye.')],
    dictationQuotes('en', []), 'en').text).toBe('He waited.\n“Hello,” she said.\n“Goodbye.”')
})

for (const [style, open, close] of [['de-low', '„', '“'], ['de-guillemet', '»', '«']]) {
  test(`German ${style} puts the speech-tag comma outside the closing quote`, () => {
    expect(formatDictation([narration('Er wartete.'), speech('Komm zurück!'), tag('sagte sie.')],
      dictationQuotes(style, []), 'de').text).toBe(`Er wartete.\n${open}Komm zurück!${close}, sagte sie.`)
  })
}

test('custom quotation pairs override the preset, including single quotes', () => {
  const quotes = dictationQuotes('de-low', [{ kind: 'literal', start: "'", end: "'", startReplace: '‹', endReplace: '›' }])
  expect(formatDictation([speech('Hallo.')], quotes, 'de').text).toBe('‹Hallo.›')
})

test('speech continues after an attribution and across recording chunks', () => {
  expect(formatDictation([speech('Hello,'), tag('she said,'), speech('come in.', false)],
    dictationQuotes('en', []), 'en').text).toBe('“Hello,” she said, “come in.”')
  expect(formatDictation([speech('Come in.', false)], dictationQuotes('en', []), 'en', 'dialogue'))
    .toEqual({ text: 'Come in.”', mergeClose: '”', paragraph: false, lastKind: 'dialogue' })
})

test('indirect speech stays narration and a new speaker never merges with the last', () => {
  expect(formatDictation([narration('She said that she was tired.')], dictationQuotes('en', []), 'en').text)
    .toBe('She said that she was tired.')
  expect(formatDictation([speech('No.')], dictationQuotes('en', []), 'en', 'dialogue').mergeClose).toBe('')
})

test('same-speaker sentences and actions stay together; another actor starts a paragraph', () => {
  const result = formatDictation([speech('Wait.'), speech('I will get my coat.', false),
    narration('Anna opened the cupboard.'), speech('It is cold.', false),
    { ...narration('Ben walked to the door.'), newParagraph: true }, speech('Hurry up.', false), tag('he said.')],
  dictationQuotes('en', []), 'en')
  expect(result.text).toBe('“Wait. I will get my coat.” Anna opened the cupboard. “It is cold.”\nBen walked to the door. “Hurry up,” he said.')
})

test('paragraph decisions also apply at the start of a new recording chunk', () => {
  const quotes = dictationQuotes('en', [])
  expect(formatDictation([narration('She opened the cupboard.')], quotes, 'en', 'dialogue').paragraph).toBe(false)
  expect(formatDictation([{ ...narration('Ben left.'), newParagraph: true }], quotes, 'en', 'dialogue').paragraph).toBe(true)
  expect(formatDictation([speech('Come in.', false)], quotes, 'en', 'narration').paragraph).toBe(false)
  expect(formatDictation([speech('No.')], quotes, 'en', 'dialogue').paragraph).toBe(true)
})

test('model context preserves merged speech, speech tags and actual paragraph boundaries across chunks', () => {
  const quotes = dictationQuotes('en', [])
  let context = appendDictationContext('“Wait.”', formatDictation([speech('I will get my coat.', false)], quotes, 'en', 'dialogue'))
  expect(context).toBe('“Wait. I will get my coat.”')
  context = appendDictationContext(context, formatDictation([tag('Anna said.')], quotes, 'en', 'dialogue'))
  expect(context).toBe('“Wait. I will get my coat,” Anna said.')
  context = appendDictationContext(context, formatDictation([narration('She opened the cupboard.')], quotes, 'en', 'attribution'))
  context = appendDictationContext(context, formatDictation([speech('Hurry up.')], quotes, 'en', 'narration'))
  expect(context).toBe('“Wait. I will get my coat,” Anna said. She opened the cupboard.\n“Hurry up.”')
  expect(appendDictationContext('„Warte.“', formatDictation([tag('sagte sie.')], dictationQuotes('de-low', []), 'de', 'dialogue')))
    .toBe('„Warte“, sagte sie.')
})

test('WAV contains a valid header and clipped little-endian PCM', () => {
  const bytes = wav([new Float32Array([-2, 0, 2])], 16000)
  const view = new DataView(bytes.buffer)
  expect(new TextDecoder().decode(bytes.slice(0, 4))).toBe('RIFF')
  expect(view.getUint32(24, true)).toBe(16000)
  expect(view.getUint32(40, true)).toBe(6)
  expect(view.getInt16(44, true)).toBe(-32768)
  expect(view.getInt16(48, true)).toBe(32767)
})

test('continuous capture emits at pauses, ignores silence, and flushes final speech', () => {
  const clips: Uint8Array[] = []
  const chunks = new SpeechChunks(16000, (audio) => clips.push(audio))
  const quiet = new Float32Array(1600)
  const voice = new Float32Array(1600).fill(0.1)
  for (let i = 0; i < 50; i++) chunks.push(quiet)
  expect(clips).toHaveLength(0)
  for (let i = 0; i < 10; i++) chunks.push(voice)
  for (let i = 0; i < 10; i++) chunks.push(quiet)
  expect(clips).toHaveLength(1)
  for (let i = 0; i < 4; i++) chunks.push(voice)
  chunks.flush()
  expect(clips).toHaveLength(2)
  chunks.flush()
  expect(clips).toHaveLength(2)
})

test('long dictation has bounded independent clips and no session limit', () => {
  const clips: Uint8Array[] = []
  const chunks = new SpeechChunks(16000, (audio) => clips.push(audio))
  const voice = new Float32Array(1600).fill(0.1)
  for (let i = 0; i < 8000; i++) chunks.push(voice) // Over thirteen minutes.
  chunks.flush()
  expect(clips.length).toBeGreaterThan(30)
  expect(clips.every((clip) => clip.byteLength <= 44 + 25 * 16000 * 2)).toBe(true)
})
