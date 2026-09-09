import { test, expect } from '@playwright/test'
import { StreamingAudio } from '../src/renderer/src/audio/StreamingAudio'
import measured from './fixtures/narration-generation-timing.json'

function playback() {
  let clock = 0
  const starts: { at: number; duration: number; ended: boolean; node: AudioBufferSourceNode }[] = []
  const buffering: boolean[] = []
  let stopped = 0
  const context = {
    get currentTime() { return clock }, destination: {},
    resume: async () => {},
    decodeAudioData: async () => ({ duration: 0.32 }),
    createBufferSource: () => {
      const node = {
        buffer: null as AudioBuffer | null,
        onended: null as (() => void) | null,
        connect() {}, disconnect() {}, stop() { stopped++ },
        start(at: number) {
          starts.push({ at, duration: node.buffer!.duration, ended: false, node: node as unknown as AudioBufferSourceNode })
        }
      }
      return node
    }
  } as unknown as AudioContext
  const audio = new StreamingAudio(context, 0, () => clock * 1000)
  const playing = audio.play((value) => buffering.push(value))
  // Tests deliberately cancel in some scenarios; observe that rejection below.
  void playing.catch(() => {})
  function advance(seconds: number): void {
    clock = seconds
    for (const part of starts) {
      if (!part.ended && part.at + part.duration <= clock) {
        part.ended = true
        part.node.onended?.(new Event('ended'))
      }
    }
  }
  return {
    audio, starts, buffering, playing,
    get stopped() { return stopped },
    async chunk(at: number) { advance(at); await audio.append('AA==') },
    async finish(at?: number) {
      if (at !== undefined) advance(at)
      audio.finish()
      await Promise.resolve()
      advance(clock + 100)
      await playing
    }
  }
}

function expectContinuous(starts: { at: number; duration: number }[]): void {
  for (let i = 1; i < starts.length; i++)
    expect(starts[i].at).toBeCloseTo(starts[i - 1].at + starts[i - 1].duration, 8)
}

for (const secondsPerAudioSecond of [0.9, 1.05, 1.5625, 2.5]) {
  test(`generation taking ${secondsPerAudioSecond}s per audio second finishes buffering before playback`, async () => {
    const p = playback()
    for (let i = 1; i <= 30; i++) {
      await p.chunk(i * 0.32 * secondsPerAudioSecond)
      expect(p.starts).toHaveLength(0)
    }
    expect(p.audio.generationSecondsPerAudioSecond).toBeCloseTo(secondsPerAudioSecond, 8)
    expect(p.buffering).toEqual([true])
    await p.finish()
    expect(p.starts).toHaveLength(30)
    expectContinuous(p.starts)
    expect(p.buffering).toEqual([true, false])
  })
}

test('stable faster-than-playback generation starts early with sufficient reserve', async () => {
  const p = playback()
  for (let i = 1; i <= 8; i++) await p.chunk(i * 0.16)
  expect(p.starts).toHaveLength(8)
  expect(p.audio.generationSecondsPerAudioSecond).toBeCloseTo(0.5, 8)
  for (let i = 9; i <= 30; i++) await p.chunk(i * 0.16)
  await p.finish()
  expectContinuous(p.starts)
  expect(p.buffering).toEqual([true, false])
})

test('a burst of chunks is not mistaken for sustained fast generation', async () => {
  const p = playback()
  for (let i = 0; i < 12; i++) await p.chunk(1)
  expect(p.starts).toHaveLength(0)
  await p.finish()
  expectContinuous(p.starts)
})

test('fast average generation with an observed stall stays buffered', async () => {
  const p = playback()
  let at = 0
  for (let i = 0; i < 16; i++) {
    at += i === 3 ? 0.7 : 0.12
    await p.chunk(at)
  }
  expect(p.audio.generationSecondsPerAudioSecond).toBeLessThan(0.8)
  expect(p.starts).toHaveLength(0)
  await p.finish()
  expectContinuous(p.starts)
})

test('an unexpected slowdown buffers the remainder once instead of playing intermittent fragments', async () => {
  const p = playback()
  for (let i = 1; i <= 8; i++) await p.chunk(i * 0.16)
  expect(p.starts).toHaveLength(8)
  for (let i = 0; i < 6; i++) {
    await p.chunk(4.5 + i * 0.5)
    expect(p.starts).toHaveLength(8)
  }
  expect(p.buffering).toEqual([true, false, true])
  await p.finish()
  expect(p.starts).toHaveLength(14)
  expectContinuous(p.starts.slice(0, 8))
  expectContinuous(p.starts.slice(8))
  expect(p.buffering).toEqual([true, false, true, false])
})

test('stop settles both buffering and scheduled playback without releasing more audio', async () => {
  for (const chunks of [2, 8]) {
    const p = playback()
    for (let i = 1; i <= chunks; i++) await p.chunk(i * 0.16)
    const scheduled = p.starts.length
    p.audio.cancel(new Error('stopped'))
    await expect(p.playing).rejects.toThrow('stopped')
    await p.chunk(20)
    p.audio.finish()
    await Promise.resolve()
    expect(p.starts).toHaveLength(scheduled)
    expect(p.stopped).toBe(scheduled)
  }
})


test('the measured M5 generation trace is buffered into one continuous passage', async () => {
  const p = playback()
  for (const chunk of measured.chunks) {
    await p.chunk(chunk.seconds)
    expect(p.starts).toHaveLength(0)
  }
  await p.finish(measured.seconds)
  expect(p.starts).toHaveLength(measured.chunks.length)
  expect(p.starts[0].at).toBeGreaterThanOrEqual(measured.seconds)
  expectContinuous(p.starts)
})
