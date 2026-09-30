import { test, expect, type Page } from '@playwright/test'
import { resolve } from 'node:path'
import { launchApp, seedBook, type Harness } from './harness'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

type Fake = { gain: GainNode; context: AudioContext; calls: number; fail: boolean; formatFail?: boolean; hold: boolean;
  release?: () => void; stopped: boolean; languages: string[] }

async function setup(): Promise<Harness> {
  const h = await launchApp('nl-dictation-', {
    NOVALIST_BACKEND_PATH: resolve('../Novalist.Backend/bin/Debug/net8.0/Novalist.Backend.exe')
  })
  const book = await seedBook(h, { Chapter: ['First', 'Second'] })
  await h.page.evaluate(async (chapter) => {
    await window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id)
  }, book.chapters[0])
  await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toBeVisible()
  await h.page.evaluate(() => {
    const context = new AudioContext()
    const oscillator = context.createOscillator()
    const gain = context.createGain()
    gain.gain.value = 0
    const destination = context.createMediaStreamDestination()
    oscillator.connect(gain).connect(destination)
    oscillator.start()
    const fake: Fake = { context, gain, calls: 0, fail: false, hold: false, stopped: false, languages: [] }
    ;(window as unknown as { dictationFake: Fake }).dictationFake = fake
    // Generated audio only: these tests never ask for the machine's microphone.
    navigator.mediaDevices.getUserMedia = async () => {
      await context.resume()
      const stream = destination.stream.clone()
      const track = stream.getAudioTracks()[0]
      const stop = track.stop.bind(track)
      track.stop = () => { fake.stopped = true; stop() }
      return stream
    }
    const original = window.novalistRpc.request.bind(window.novalistRpc)
    window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
      if (method === 'dictation/providers') return [{ id: 'fake', name: 'Test speech', available: true,
        audioDestination: 'local test', formattingDestination: 'local test' }] as T
      if (method === 'dictation/transcribe') {
        const args = params as { audioBase64: string; language: string }
        if (!atob(args.audioBase64).startsWith('RIFF')) throw new Error('Missing standalone WAV header')
        fake.languages.push(args.language)
        if (fake.fail) { fake.fail = false; throw new Error('Speech server unavailable') }
        if (fake.hold) await new Promise<void>((resolve) => { fake.release = resolve })
        fake.calls++
        return (fake.calls === 1 ? 'Hello she said.' : 'Goodbye.') as T
      }
      if (method === 'dictation/format' && fake.formatFail) throw new Error('Bad model output')
      if (method === 'dictation/format') return (fake.calls === 1
        ? [{ text: 'Hello.', kind: 'dialogue', newParagraph: true }, { text: 'she said.', kind: 'attribution', newParagraph: false }]
        : [{ text: 'Goodbye.', kind: 'dialogue', newParagraph: true }]) as T
      return original<T>(method, params)
    }
  })
  return h
}
async function voice(page: Page, on: boolean): Promise<void> {
  await page.evaluate((enabled) => { (window as unknown as { dictationFake: Fake }).dictationFake.gain.gain.value = enabled ? 0.1 : 0 }, on)
}
async function start(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Dictate', exact: true }).click()
  await page.getByRole('button', { name: 'Start dictation', exact: true }).click()
  await expect(page.getByText('Listening…', { exact: true })).toBeVisible()
}

test('speech lands directly in the editor; stopping flushes the last clip without a preview', async () => {
  const h = await setup()
  try {
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await editor.click()
    await h.page.keyboard.type('Before.')
    await start(h.page)
    await voice(h.page, true)
    await h.page.waitForTimeout(900)
    await voice(h.page, false)
    await expect(editor).toContainText('“Hello,” she said.', { timeout: 10000 })
    expect(await editor.innerText()).toMatch(/Before\.\n+“Hello,” she said\./)
    await voice(h.page, true)
    await h.page.waitForTimeout(400)
    await h.page.getByRole('button', { name: 'Stop dictation', exact: true }).click()
    await expect(editor).toContainText('“Goodbye.”', { timeout: 10000 })
    await expect(h.page.getByRole('button', { name: 'Start dictation', exact: true })).toBeEnabled()
    expect(await h.page.evaluate(() => (window as unknown as { dictationFake: Fake }).dictationFake.stopped)).toBe(true)
    await expect(h.page.getByRole('dialog', { name: /dictation|preview/i })).toHaveCount(0)
  } finally { await h.close() }
})

test('dictation starts at the caret between existing paragraphs and continues there', async () => {
  const h = await setup()
  try {
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await editor.click()
    await h.page.keyboard.type('Before.')
    await h.page.keyboard.press('Enter')
    await h.page.keyboard.press('Enter')
    await h.page.keyboard.type('After.')
    await h.page.keyboard.press('Home')
    await h.page.keyboard.press('ArrowUp')
    await start(h.page)
    await voice(h.page, true)
    await h.page.waitForTimeout(900)
    await voice(h.page, false)
    await expect(editor).toContainText('“Hello,” she said.', { timeout: 10000 })
    expect(await editor.innerText()).toMatch(/Before\.\s+“Hello,” she said\.\s+After\./)
    await voice(h.page, true)
    await h.page.waitForTimeout(400)
    await h.page.getByRole('button', { name: 'Stop dictation', exact: true }).click()
    await expect(editor).toContainText('“Goodbye.”', { timeout: 10000 })
    expect(await editor.innerText()).toMatch(/Before\.\s+“Hello,” she said\.\s+“Goodbye\.”\s+After\./)
  } finally { await h.close() }
})

test('pending speech keeps its place through proofing, page layout changes, and a moved caret', async () => {
  const h = await setup()
  try {
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await h.page.evaluate(() => {
      const fake = (window as unknown as { dictationFake: Fake }).dictationFake
      fake.hold = true
      fake.formatFail = true
    })
    await editor.evaluate((element) => {
      const w = element.ownerDocument.defaultView as unknown as EditorWindow
      w.setContent('<p>Before the gap. After the gap.</p><p>Elsewhere.</p>')
      element.focus()
      w.getSelection()!.setPosition(element.querySelector('p')!.firstChild!, 15)
    })
    await start(h.page)
    await voice(h.page, true)
    await h.page.waitForTimeout(400)
    await h.page.getByRole('button', { name: 'Stop dictation', exact: true }).click()
    await h.page.waitForFunction(() => !!(window as unknown as { dictationFake: Fake }).dictationFake.release)
    await editor.evaluate((element) => {
      const w = element.ownerDocument.defaultView as unknown as EditorWindow
      w.setGrammarCheckEnabled(true)
      w.setGrammarIssues(JSON.stringify([{ offset: 0, length: 28, type: 'grammar',
        message: 'Fixture highlight', replacements: [] }]))
      w.setPageView(false)
      w.setGrammarIssues('[]')
      w.setPageView(true)
      // Editing elsewhere while a clip is processing must not redirect it.
      w.getSelection()!.setPosition(element.querySelectorAll('p')[1].firstChild!, 3)
    })
    await h.page.waitForTimeout(250)
    await h.page.evaluate(() => (window as unknown as { dictationFake: Fake }).dictationFake.release?.())
    await expect(editor.locator('p').first()).toHaveText('Before the gap. Hello she said. After the gap.')
    await expect(editor.locator('p').nth(1)).toHaveText('Elsewhere.')
    expect(await editor.evaluate((element) => {
      const selection = element.ownerDocument.defaultView!.getSelection()!
      return { text: selection.anchorNode!.textContent, offset: selection.anchorOffset }
    })).toEqual({ text: 'Elsewhere.', offset: 3 })
  } finally { await h.close() }
})

test('failed transcription keeps audio for retry and German quotation style reaches the editor', async () => {
  const h = await setup()
  try {
    await h.page.evaluate(async () => {
      await window.novalistStores.settings.getState().update('global', { autoReplacementLanguage: 'de-guillemet' })
      ;(window as unknown as { dictationFake: Fake }).dictationFake.fail = true
    })
    await h.page.frameLocator('.editor-frame').locator('#editor').click()
    await start(h.page)
    await voice(h.page, true)
    await h.page.waitForTimeout(400)
    await h.page.getByRole('button', { name: 'Stop dictation', exact: true }).click()
    await expect(h.page.getByRole('alert')).toContainText('Transcription failed', { timeout: 10000 })
    await h.page.getByRole('button', { name: 'Resume pending speech at the caret' }).click()
    await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toContainText('»Hello«, she said.', { timeout: 10000 })
    expect(await h.page.evaluate(() => (window as unknown as { dictationFake: Fake }).dictationFake.languages)).toEqual(['de', 'de'])
  } finally { await h.close() }
})

test('a late result cannot write into another scene; pending speech resumes explicitly', async () => {
  const h = await setup()
  try {
    await h.page.evaluate(() => { (window as unknown as { dictationFake: Fake }).dictationFake.hold = true })
    await h.page.frameLocator('.editor-frame').locator('#editor').click()
    await start(h.page)
    await voice(h.page, true)
    await h.page.waitForTimeout(400)
    await h.page.getByRole('button', { name: 'Stop dictation', exact: true }).click()
    await h.page.waitForFunction(() => !!(window as unknown as { dictationFake: Fake }).dictationFake.release)
    await h.page.evaluate(async () => {
      const project = window.novalistStores.project.getState()
      const chapter = project.chapters[0]
      await project.openScene(chapter.guid, chapter.scenes[1].id)
      ;(window as unknown as { dictationFake: Fake }).dictationFake.release?.()
    })
    await expect(h.page.frameLocator('.editor-frame').locator('#editor')).not.toContainText('Hello')
    await expect(h.page.getByText('Dictation paused because you left the scene.', { exact: false })).toBeVisible()
    await h.page.evaluate(async () => {
      const project = window.novalistStores.project.getState()
      await project.openScene(project.chapters[0].guid, project.chapters[0].scenes[0].id)
    })
    await h.page.frameLocator('.editor-frame').locator('#editor').click()
    await h.page.getByRole('button', { name: 'Resume pending speech at the caret' }).click()
    await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toContainText('“Hello,” she said.', { timeout: 10000 })
  } finally { await h.close() }
})

test('dictation preserves a selection, merges continued speech, supports undo, and rejects a replaced document', async () => {
  const h = await setup()
  try {
    const result = await h.page.frameLocator('.editor-frame').locator('#editor').evaluate((element) => {
      const w = element.ownerDocument.defaultView as unknown as EditorWindow
      w.setContent('<p>Keep these words.</p>')
      const range = element.ownerDocument.createRange()
      range.selectNodeContents(element.firstChild!)
      w.getSelection()!.removeAllRanges(); w.getSelection()!.addRange(range)
      w.captureDictationAnchor('test')
      w.insertDictationText('test', '“Hello.”', true, '', '“')
      w.insertDictationText('test', 'Again.”', false, '”', '“')
      const merged = element.textContent
      element.ownerDocument.execCommand('undo')
      const undone = element.textContent
      w.setContent('<p>Another scene.</p>')
      const stale = w.insertDictationText('test', 'Wrong!', false, '', '')
      return { merged, undone, stale, final: element.textContent }
    })
    expect(result.merged).toContain('Keep these words.')
    expect(result.merged).toContain('“Hello. Again.”')
    expect(result.undone).not.toContain('Again.')
    expect(result.stale).toBe(false)
    expect(result.final).toBe('Another scene.')
  } finally { await h.close() }
})

test('formatting failure inserts the original transcript without stopping for a preview', async () => {
  const h = await setup()
  try {
    await h.page.evaluate(() => { (window as unknown as { dictationFake: Fake }).dictationFake.formatFail = true })
    await start(h.page)
    await voice(h.page, true)
    await h.page.waitForTimeout(400)
    await h.page.getByRole('button', { name: 'Stop dictation', exact: true }).click()
    await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toContainText('Hello she said.')
    await expect(h.page.getByText('Dialogue formatting failed for a clip.', { exact: false })).toBeVisible()
  } finally { await h.close() }
})

test('a speech tag after a pause repairs only the generated closing punctuation', async () => {
  const h = await setup()
  try {
    const result = await h.page.frameLocator('.editor-frame').locator('#editor').evaluate((element) => {
      const w = element.ownerDocument.defaultView as unknown as EditorWindow
      const results: string[] = []
      for (const [open, close, language, tag] of [['“', '”', 'en', 'she said.'], ['„', '“', 'de', 'sagte sie.']]) {
        w.setContent('<p><br></p>')
        w.captureDictationAnchor('test')
        w.insertDictationText('test', open + 'Hallo.' + close, true, '', open)
        w.insertDictationText('test', tag, false, '', open, { close, language })
        results.push(element.textContent ?? '')
      }
      return results
    })
    expect(result).toEqual(['“Hallo,” she said.', '„Hallo“, sagte sie.'])
  } finally { await h.close() }
})

test('denied microphone access returns to an actionable idle state', async () => {
  const h = await setup()
  try {
    await h.page.evaluate(() => {
      navigator.mediaDevices.getUserMedia = async () => { throw new DOMException('Denied', 'NotAllowedError') }
    })
    await h.page.getByRole('button', { name: 'Dictate', exact: true }).click()
    await h.page.getByRole('button', { name: 'Start dictation', exact: true }).click()
    await expect(h.page.getByRole('alert')).toContainText('Microphone access was denied')
    await expect(h.page.getByRole('button', { name: 'Start dictation', exact: true })).toBeEnabled()
  } finally { await h.close() }
})
