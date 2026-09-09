import { test, expect } from '@playwright/test'
import { launchApp } from './harness'

test('narration plays chunks before completion, joins them continuously and reuses the complete cache', async () => {
  const h = await launchApp('nl-narration-stream-')
  try {
    await h.page.locator('body').click({ position: { x: 20, y: 20 } })
    const result = await h.page.evaluate(async () => {
      const store = window.novalistStores.narration
      const rpc = window.novalistRpc
      const originalRequest = rpc.request
      const OriginalContext = window.AudioContext
      const OriginalAudio = window.Audio
      const starts: number[] = []
      const decoded: number[] = []
      const states: string[] = []
      let completed = false
      let early = false
      let files = 0
      let renderCount = 0
      let began: () => void = () => {}
      class Context extends OriginalContext {
        override async decodeAudioData(bytes: ArrayBuffer): Promise<AudioBuffer> {
          const buffer = await super.decodeAudioData(bytes)
          decoded.push(buffer.duration)
          return buffer
        }
        override async resume(): Promise<void> {
          states.push(this.state)
          await super.resume()
          states.push(this.state)
        }
        override createBufferSource(): AudioBufferSourceNode {
          const source = super.createBufferSource()
          const start = source.start.bind(source)
          source.start = (when = 0) => {
            starts.push(when)
            early ||= !completed
            began()
            start(when)
          }
          return source
        }
      }
      class FileAudio {
        onended: (() => void) | null = null
        onerror: (() => void) | null = null
        pause(): void {}
        play(): Promise<void> { files++; queueMicrotask(() => this.onended?.()); return Promise.resolve() }
      }
      // 320 ms of PCM silence: actual browser decoding and scheduling, no model.
      const wav = new Uint8Array(44 + 7680 * 2)
      const view = new DataView(wav.buffer)
      const word = (at: number, text: string) => { for (let i = 0; i < text.length; i++) wav[at + i] = text.charCodeAt(i) }
      word(0, 'RIFF'); word(8, 'WAVE'); word(12, 'fmt '); word(36, 'data')
      view.setUint32(4, wav.length - 8, true); view.setUint32(16, 16, true)
      view.setUint16(20, 1, true); view.setUint16(22, 1, true)
      view.setUint32(24, 24000, true); view.setUint32(28, 48000, true)
      view.setUint16(32, 2, true); view.setUint16(34, 16, true); view.setUint32(40, wav.length - 44, true)
      const audio = btoa(String.fromCharCode(...wav))
      const notify = (streamId: string, sequence: number, key = 'line') => {
        ;(rpc as unknown as { dispatch: (message: unknown) => void }).dispatch({
          method: 'narration/audioChunk', params: [{ streamId, key, sequence, audio }]
        })
      }
      try {
        window.AudioContext = Context
        window.Audio = FileAudio as unknown as typeof Audio
        rpc.request = (async (method: string, params?: unknown) => {
          if (method === 'voices/stop' || method === 'narration/renderStop') return true
          if (method !== 'narration/render') return originalRequest.call(rpc, method, params)
          renderCount++
          if (renderCount === 1) {
            const streamId = (params as unknown[])[4] as string
            const started = new Promise<void>((resolve) => { began = resolve })
            notify('stale-reading', 99)
            notify(streamId, 99, 'later-line')
            for (let i = 0; i < 8; i++) {
              await new Promise((resolve) => setTimeout(resolve, 160))
              notify(streamId, i)
            }
            await Promise.race([started, new Promise((_, reject) => setTimeout(() => reject(new Error('stream did not start')), 5000))])
            notify(streamId, 8); notify(streamId, 9)
            completed = true
          }
          return { engineId: 'test', total: 1, clips: [{ key: 'line', clip: 'complete.wav', durationMs: 3200, error: null }] }
        }) as typeof rpc.request
        store.getState().reset()
        store.setState({ engines: [{ isReady: true } as never], reading: [{
          chapterGuid: 'chapter', sceneId: 'scene', chapterTitle: '', sceneTitle: '',
          segment: { key: 'line', lineKey: 'line', text: 'Hello.', voiceId: 'voice' } as never
        }] })
        await store.getState().play()
        const first = { early, chunks: starts.length, files, error: store.getState().readingError, heard: store.getState().heard.line }
        await store.getState().play()
        return { decoded, states, first, gaps: starts.slice(1).map((time, i) => time - starts[i]), replayFiles: files, idle: !store.getState().preparing && store.getState().speaking === null }
      } finally {
        await store.getState().stopAsync()
        store.getState().reset()
        rpc.request = originalRequest
        window.AudioContext = OriginalContext
        window.Audio = OriginalAudio
      }
    })
    expect(result.decoded).toEqual(Array(10).fill(0.32))
    expect(result.first).toEqual({ early: true, chunks: 10, files: 0, error: null, heard: 'complete.wav' })
    expect(result.replayFiles).toBe(1)
    expect(result.idle).toBe(true)
    for (const gap of result.gaps) expect(gap).toBeCloseTo(0.32, 6)
  } finally { await h.close() }
})

test('stopping while a passage buffers settles playback and ignores late chunks', async () => {
  const h = await launchApp('nl-narration-stream-stop-')
  try {
    await h.page.locator('body').click({ position: { x: 20, y: 20 } })
    const result = await h.page.evaluate(async () => {
      const store = window.novalistStores.narration
      const rpc = window.novalistRpc
      const originalRequest = rpc.request
      const OriginalContext = window.AudioContext
      let starts = 0
      let stopped = 0
      let began: () => void = () => {}
      let finishRender: () => void = () => {}
      let sendLate: () => void = () => {}
      // A controllable audio clock keeps a chunk playing until Stop.
      class Context {
        currentTime = 0
        destination = {}
        resume(): Promise<void> { return Promise.resolve() }
        close(): Promise<void> { return Promise.resolve() }
        decodeAudioData(): Promise<{ duration: number }> { return Promise.resolve({ duration: 0.32 }) }
        createBufferSource() {
          return { buffer: null, onended: null, connect() {}, disconnect() {},
            start() { starts++; began() }, stop() { stopped++ } }
        }
      }
      try {
        window.AudioContext = Context as unknown as typeof AudioContext
        rpc.request = (async (method: string, params?: unknown) => {
          if (method === 'voices/stop') return true
          if (method === 'narration/renderStop') { finishRender(); return true }
          if (method !== 'narration/render') return originalRequest.call(rpc, method, params)
          const streamId = (params as unknown[])[4]
          const emit = (sequence: number) => (rpc as unknown as { dispatch: (m: unknown) => void }).dispatch({
            method: 'narration/audioChunk', params: [{ streamId, key: 'line', sequence, audio: 'AA==' }]
          })
          emit(0); emit(1)
          began()
          sendLate = () => emit(2)
          await new Promise<void>((resolve) => { finishRender = resolve })
          sendLate()
          return { engineId: 'test', total: 1, clips: [] }
        }) as typeof rpc.request
        store.getState().reset()
        store.setState({ engines: [{ isReady: true } as never], reading: [{
          chapterGuid: 'chapter', sceneId: 'scene', chapterTitle: '', sceneTitle: '',
          segment: { key: 'line', lineKey: 'line', text: 'Hello.', voiceId: 'voice' } as never
        }] })
        const started = new Promise<void>((resolve) => { began = resolve })
        const playing = store.getState().play()
        await started
        await new Promise((resolve) => setTimeout(resolve, 350))
        const waiting = store.getState().preparing
        await store.getState().stopAsync()
        await playing
        sendLate()
        return { starts, stopped, waiting, error: store.getState().readingError, ready: store.getState().ready, idle: !store.getState().preparing && store.getState().speaking === null }
      } finally {
        await store.getState().stopAsync()
        store.getState().reset()
        rpc.request = originalRequest
        window.AudioContext = OriginalContext
      }
    })
    expect(result).toEqual({ starts: 0, stopped: 0, waiting: true, error: null, ready: {}, idle: true })
  } finally { await h.close() }
})

test('a corrupt live chunk reports an error instead of leaving playback waiting', async () => {
  const h = await launchApp('nl-narration-stream-error-')
  try {
    const result = await h.page.evaluate(async () => {
      const store = window.novalistStores.narration
      const rpc = window.novalistRpc
      const originalRequest = rpc.request
      const OriginalContext = window.AudioContext
      class Context {
        currentTime = 0
        destination = {}
        resume(): Promise<void> { return Promise.resolve() }
        close(): Promise<void> { return Promise.resolve() }
        decodeAudioData(): Promise<never> { return Promise.reject(new Error('invalid WAV')) }
      }
      try {
        window.AudioContext = Context as unknown as typeof AudioContext
        rpc.request = (async (method: string, params?: unknown) => {
          if (method === 'voices/stop' || method === 'narration/renderStop') return true
          if (method !== 'narration/render') return originalRequest.call(rpc, method, params)
          ;(rpc as unknown as { dispatch: (message: unknown) => void }).dispatch({
            method: 'narration/audioChunk', params: [{ streamId: (params as unknown[])[4], key: 'line', sequence: 0, audio: 'AA==' }]
          })
          await new Promise((resolve) => setTimeout(resolve, 50))
          return { engineId: 'test', total: 1, clips: [{ key: 'line', clip: 'complete.wav', durationMs: 320, error: null }] }
        }) as typeof rpc.request
        store.getState().reset()
        store.setState({ engines: [{ isReady: true } as never], reading: [{
          chapterGuid: 'chapter', sceneId: 'scene', chapterTitle: '', sceneTitle: '',
          segment: { key: 'line', lineKey: 'line', text: 'Hello.', voiceId: 'voice' } as never
        }] })
        await store.getState().play()
        return { error: store.getState().readingError, idle: !store.getState().preparing && store.getState().speaking === null }
      } finally {
        await store.getState().stopAsync()
        store.getState().reset()
        rpc.request = originalRequest
        window.AudioContext = OriginalContext
      }
    })
    expect(result).toEqual({ error: 'audio-stream-decode-failed', idle: true })
  } finally { await h.close() }
})
