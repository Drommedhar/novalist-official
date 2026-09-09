import { test, expect } from '@playwright/test'
import { launchApp } from './harness'

test('narration reports failures and settles stopped playback', async () => {
  const h = await launchApp('nl-narration-errors-')
  try {
    const results = await h.page.evaluate(async () => {
      const store = window.novalistStores.narration
      const rpc = window.novalistRpc
      const originalRequest = rpc.request
      const originalAudio = window.Audio
      type Scenario = 'ended' | 'rejected' | 'media' | 'render' | 'system' | 'system-request' | 'stop'
      let scenario: Scenario = 'ended'
      let played = 0
      let spoken = 0
      let stops = 0
      let started: () => void = () => {}

      class FakeAudio {
        onended: (() => void) | null = null
        onerror: (() => void) | null = null
        error = { code: 3 }
        pause(): void {}
        play(): Promise<void> {
          played++
          started()
          if (scenario === 'rejected') return Promise.reject(new DOMException('blocked', 'NotAllowedError'))
          if (scenario === 'media') queueMicrotask(() => this.onerror?.())
          if (scenario === 'ended') queueMicrotask(() => this.onended?.())
          return Promise.resolve()
        }
      }

      const results: { scenario: string; error: string | null; played: number; spoken: number; stops: number; idle: boolean }[] = []
      try {
        window.Audio = FakeAudio as unknown as typeof Audio
        rpc.request = (async (method: string, params?: unknown) => {
          if (method === 'narration/renderStop') { stops++; return true }
          if (method === 'voices/stop') return true
          if (method === 'voices/speak') {
            spoken++
            if (scenario === 'system-request') throw new Error('disconnected')
            return false
          }
          if (method === 'narration/render') {
            if (scenario === 'render') throw new Error('disconnected')
            const at = (params as number[])[0]
            return {
              engineId: 'test', total: 2,
              clips: [{ key: `line-${at}`, clip: 'abc.wav', durationMs: 1, error: null }]
            }
          }
          return originalRequest.call(rpc, method, params)
        }) as typeof rpc.request

        for (const next of ['ended', 'rejected', 'media', 'render', 'system', 'system-request', 'stop'] as const) {
          scenario = next
          played = spoken = stops = 0
          store.getState().reset()
          store.setState({
            engines: scenario.startsWith('system') ? [] : [{ isReady: true } as never],
            reading: [0, 1].map((i) => ({
              chapterGuid: 'chapter', sceneId: 'scene', chapterTitle: '', sceneTitle: '',
              segment: { key: `line-${i}`, lineKey: `line-${i}`, text: 'Hello.', voiceId: 'voice' } as never
            }))
          })
          const began = new Promise<void>((resolve) => { started = resolve })
          const playing = store.getState().play()
          if (scenario === 'stop') {
            await began
            await store.getState().stopAsync()
          }
          // Stop must settle play(), even though pausing an audio element never
          // fires ended. A broken implementation fails instead of hanging the test.
          let timer: ReturnType<typeof setTimeout> | undefined
          try {
            await Promise.race([
              playing,
              new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('play never settled')), 5000) })
            ])
          } finally {
            clearTimeout(timer)
          }
          const state = store.getState()
          results.push({ scenario, error: state.readingError, played, spoken, stops, idle: !state.preparing && state.speaking === null })
        }
      } finally {
        await store.getState().stopAsync()
        store.getState().reset()
        rpc.request = originalRequest
        window.Audio = originalAudio
      }
      return results
    })

    expect(results).toEqual([
      { scenario: 'ended', error: null, played: 2, spoken: 0, stops: 1, idle: true },
      { scenario: 'rejected', error: 'audio-playback-failed (NotAllowedError)', played: 1, spoken: 0, stops: 2, idle: true },
      { scenario: 'media', error: 'audio-playback-failed (media-3)', played: 1, spoken: 0, stops: 2, idle: true },
      { scenario: 'render', error: 'render-request-failed', played: 0, spoken: 0, stops: 2, idle: true },
      { scenario: 'system', error: 'system-speech-failed', played: 0, spoken: 1, stops: 1, idle: true },
      { scenario: 'system-request', error: 'system-speech-request-failed', played: 0, spoken: 1, stops: 1, idle: true },
      { scenario: 'stop', error: null, played: 1, spoken: 0, stops: 2, idle: true }
    ])
  } finally {
    await h.close()
  }
})
