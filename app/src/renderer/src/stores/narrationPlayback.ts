import { rpc } from '../rpc/client'
import { StreamingAudio } from '../audio/StreamingAudio'
import { type NarrationBook, type NarrationClip, type NarrationRender, type ReadingStep, type NarrationState, type AudioChunk } from './narrationTypes'

/**
 * A token that invalidates an in-flight reading.
 *
 * Playback is a loop of awaited `voices/speak` calls, and every one of them is
 * a point at which the writer may have pressed Stop, reloaded the book, or
 * reassigned a speaker. Bumping this is what makes the loop notice: it compares
 * on resume and gives up if it is no longer the current reading. Kept outside
 * the store because it is not state anything renders.
 */
let run = 0

/**
 * The clip being played, so stopping can silence it.
 *
 * An <audio> element rather than the platform voices: when an engine has
 * rendered the reading, what plays is a file, and the browser is the thing that
 * plays files.
 */
let current: HTMLAudioElement | null = null

let cancelClip: (() => void) | null = null

/** The most segments to ask for at once. Big enough that a fast engine is not
 *  held back by round trips, small enough that stopping is quick. */
const RENDER_WINDOW = 12

/**
 * How many lines to ask for, given how many are already waiting to be played.
 *
 * Never more than are already banked, and never fewer than one. That single
 * rule is what stops the reading pausing in the same place every time.
 *
 * The pause was arithmetic, not the engine. The first request asked for two
 * lines and the next for a full window - so after two sentences had played, the
 * reading waited for a batch six times the size, which no engine can make in
 * the time two sentences take to speak. It landed on the same sentence on every
 * run, because two is two.
 *
 * Sized off the lead instead, a window can only ever be as large as the audio
 * already queued to cover it. A reading starts on one line, and each line that
 * plays for longer than it took to make buys a little more room, so the windows
 * grow by themselves on a fast machine and stay at one on a slow one - where
 * asking for twelve was never going to help anybody.
 */
function windowFor(queued: number): number {
  return Math.max(1, Math.min(queued, RENDER_WINDOW))
}

/**
 * Silence between two clips of the same scene, and between two scenes.
 *
 * The same numbers the audiobook is muxed with, and here for the same reason:
 * an engine returns each clip trimmed to the words, so laid end to end with
 * nothing between them a dialogue tag runs into the next line as one breathless
 * sentence. Without these the preview was the worse of the two readings — what
 * the writer heard when they pressed Play was not what they would get in the
 * file, and it was the file that sounded right.
 */
const SEGMENT_GAP_MS = 140

const SCENE_GAP_MS = 900

/** Waits, unless the reading was stopped while waiting. */
function pause(ms: number, token: number): Promise<void> {
  return new Promise<void>((resolve) => {
    setTimeout(resolve, ms)
  }).then(() => {
    if (token !== run) throw new StoppedError()
  })
}

/** Thrown to unwind a reading the writer stopped mid-gap. Caught where the
 *  reading is driven; never surfaced. */
class StoppedError extends Error {}

/** A failed clip must stop the reading and explain why it was silent. */
function playClip(name: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const audio = new Audio(`novalist-audio://clip/${encodeURIComponent(name)}`)
    current = audio
    const done = (error?: Error): void => {
      audio.onended = null
      audio.onerror = null
      if (current === audio) {
        current = null
        cancelClip = null
      }
      if (error) {
        audio.pause()
        reject(error)
      } else resolve()
    }
    cancelClip = () => done(new StoppedError())
    audio.onended = () => done()
    audio.onerror = () => done(new Error(`audio-playback-failed (media-${audio.error?.code ?? 'unknown'})`))
    void audio.play().catch((error: unknown) => {
      done(new Error(`audio-playback-failed (${error instanceof Error ? error.name : 'unknown'})`))
    })
  })
}

/** The reading as one run, in book order. */
export function flatten(book: NarrationBook | null): ReadingStep[] {
  if (!book) return []
  const steps: ReadingStep[] = []
  for (const chapter of book.chapters) {
    for (const scene of chapter.scenes) {
      for (const segment of scene.segments) {
        steps.push({
          chapterGuid: scene.chapterGuid,
          sceneId: scene.sceneId,
          sceneTitle: scene.sceneTitle,
          chapterTitle: chapter.title,
          segment
        })
      }
    }
  }
  return steps
}

let liveReading: { token: number; receive: (chunk: AudioChunk) => void } | null = null

/**
 * The reading, performed by an engine.
 *
 * Two loops rather than one. Making the speech runs flat out, ahead of the
 * writer, into a queue; playing it drains that queue. They only meet at the
 * queue, which is what lets a fast stretch bank time against a slow one.
 *
 */
export async function performedReading(
  get: () => NarrationState,
  set: (partial: Partial<NarrationState>) => void,
  token: number,
  from: number
): Promise<void> {
  const reading = get().reading
  const queue: (NarrationClip & { stream?: StreamingAudio })[] = []
  const streams = new Set<StreamingAudio>()
  const audioContext: { current: AudioContext | null } = { current: null }
  try {
    // Set when there is no more to come: the book ran out, the engine went away,
    // or something failed. The consumer needs to tell "nothing yet" from
    // "nothing ever".
    let sealed = false
    // Set when the backend says no engine answered, so the reading falls back to
    // the operating system's voices from wherever it had got to.
    let systemFrom: number | null = null

    // Makes the speech, as fast as it can, without ever waiting for a word to be
    // played. This is the whole of the fix: time the engine gains on one stretch
    // is kept for the next one instead of being spent on a bigger window.
    const making = (async () => {
      let at = from
      while (at < reading.length && token === run) {
        // Live previews hold decoded audio in memory. Bound the look-ahead even
        // when the model can generate much faster than the current reading.
        if (queue.length >= RENDER_WINDOW) {
          try { await pause(250, token) } catch { return }
          continue
        }
        // Only as much as the queue can already cover. One line to begin with, so
        // a voice starts as soon as one line can be made rather than after a
        // batch; more once there is enough waiting to hide the making of it.
        const size = Math.min(windowFor(queue.length), RENDER_WINDOW - queue.length)
        let window: NarrationRender
        const streamId = crypto.randomUUID()
        const generationStartedAt = performance.now()
        let stream: StreamingAudio | undefined
        let sequence = 0
        const key = reading[at].segment.key
        liveReading = { token, receive: (chunk) => {
          if (chunk.streamId !== streamId || chunk.key !== key) return
          try {
            if (chunk.sequence !== sequence++) throw new Error('audio-stream-out-of-order')
            if (!stream) {
              audioContext.current ??= new AudioContext({ sampleRate: chunk.sampleRate || 24000 })
              stream = new StreamingAudio(audioContext.current, generationStartedAt)
              streams.add(stream)
              queue.push({ key, clip: '', durationMs: 0, error: null, stream })
            }
            stream.append(chunk.audio)
          } catch (error) {
            const failure = error instanceof Error ? error : new Error('audio-stream-playback-failed')
            stream?.cancel(failure)
            if (!stream) queue.push({ key, clip: null, durationMs: 0, error: failure.message })
          }
        } }
        try {
          window = await rpc.request<NarrationRender>(
            'narration/render', [at, size, get().rate, get().rebuild, streamId])
        } catch {
          if (token !== run) return
          stream?.cancel(new Error('render-request-failed'))
          queue.push({ key: '', clip: null, durationMs: 0, error: 'render-request-failed' })
          break
        } finally {
          if (liveReading?.token === token) liveReading = null
        }
        if (token !== run) return
        // Whatever the writer asked to be made again has been made again; the
        // rest of the reading comes off the cache as usual.
        if (get().rebuild) set({ rebuild: false })

        if (window.engineId === null) {
          stream?.cancel(new Error('speech-engine-unavailable'))
          systemFrom = at
          break
        }

        if (stream) {
          const complete = window.clips.find((clip) => clip.key === key)
          if (!complete?.clip || complete.error) {
            stream.cancel(new Error(complete?.error ?? 'incomplete-audio-stream'))
          } else {
            // Only the complete waveform becomes a reusable reference or cache
            // entry. Never replay the final clip after its chunks were heard.
            set({ heard: { ...get().heard, [key]: complete.clip } })
            stream.finish()
          }
        }
        queue.push(...window.clips.filter((clip) => !stream || clip.key !== key))
        set({
          ready: {
            ...get().ready,
            ...Object.fromEntries(
              window.clips.filter((c) => c.clip !== null).map((c) => [c.key, true]))
          }
        })
        at += size
      }
      sealed = true
      set({ rendering: [] })
    })()

    // Whether anything has been said yet, and where it was said, so the pause
    // before the next clip is a breath inside a scene or the longer one a scene
    // break needs.
    let spoken = false
    let lastScene = ''

    while (token === run) {
      if (queue.length === 0) {
        if (sealed) break
        // Polled rather than signalled. A signal is one missed wake-up away from
        // a reading that never resumes, and a quarter second is inaudible next to
        // the seconds a line takes to make.
        try {
          await pause(250, token)
        } catch {
          return
        }
        continue
      }

      const clip = queue.shift()!
      const step = reading.find((s) => s.segment.key === clip.key)
      if (step) {
        set({
          speaking: {
            chapterGuid: step.chapterGuid,
            sceneId: step.sceneId,
            key: step.segment.key,
            lineKey: step.segment.lineKey
          }
        })
      }
      // A segment the engine refused ends the reading rather than being skipped
      // past: something is wrong, and reading on would hide it. Saying so is the
      // difference between that and a reading that simply stops, which is
      // indistinguishable from one that is still thinking.
      if (clip.clip === null) {
        set({ speaking: null, preparing: false, readingError: clip.error ?? 'render' })
        await get().stopAsync()
        return
      }
      // Remembered before it is played, so a delivery the writer liked can be
      // pointed at afterwards.
      if (!clip.stream) set({ preparing: false })
      if (clip.clip) set({ heard: { ...get().heard, [clip.key]: clip.clip } })

      // The gap goes before the clip rather than after it, so a reading never
      // ends on silence and stopping is instant.
      if (spoken) {
        const scene = step ? `${step.chapterGuid}${step.sceneId}` : lastScene
        try {
          await pause(scene === lastScene ? SEGMENT_GAP_MS : SCENE_GAP_MS, token)
        } catch {
          return
        }
        lastScene = scene
      } else if (step) {
        lastScene = `${step.chapterGuid}${step.sceneId}`
      }
      spoken = true

      try {
        if (clip.stream) {
          const cancel = () => clip.stream!.cancel(new StoppedError())
          cancelClip = cancel
          try {
            await clip.stream.play((buffering) => {
              if (token === run) set({ preparing: buffering })
            })
          } finally {
            streams.delete(clip.stream)
            if (cancelClip === cancel) cancelClip = null
          }
        } else await playClip(clip.clip)
      } catch (error) {
        if (token !== run || error instanceof StoppedError) return
        set({ readingError: error instanceof Error ? error.message : 'audio-playback-failed' })
        await get().stopAsync()
        return
      }
    }

    await making
    // No engine after all - it went away between the check and the call. What was
    // already made has been played; the rest is read with the voices the machine
    // has.
    if (systemFrom !== null && token === run)
      await spokenReading(get, set, token, systemFrom)
  } finally {
    if (liveReading?.token === token) liveReading = null
    for (const stream of streams) stream.cancel(new StoppedError())
    if (audioContext.current) void audioContext.current.close().catch(() => {})
  }
}

export async function spokenReading(
  get: () => NarrationState,
  set: (partial: Partial<NarrationState>) => void,
  token: number,
  from: number
): Promise<void> {
  const reading = get().reading

  for (let i = from; i < reading.length; i++) {
    if (token !== run) return
    const step = reading[i]
    if (!step.segment.voiceId || step.segment.text.trim().length === 0) continue

    set({
      speaking: {
            chapterGuid: step.chapterGuid,
            sceneId: step.sceneId,
            key: step.segment.key,
            lineKey: step.segment.lineKey
          }
    })
    try {
      const spoke = await rpc.request<boolean>('voices/speak', [
        step.segment.text,
        step.segment.voiceId,
        get().rate
      ])
      // A passage the engine refused ends the reading. Carrying on was worse
      // than stopping: on a machine with no speech engine every call refuses
      // instantly, so the loop swept the whole book in a second with the
      // highlight racing prose nobody could hear.
      if (token !== run) return
      if (!spoke) {
        set({ readingError: 'system-speech-failed' })
        return
      }
    } catch {
      if (token === run) set({ readingError: 'system-speech-request-failed' })
      return
    }
  }
}

export function beginNarrationPlayback(): number { return ++run }

export function isNarrationPlaybackCurrent(token: number): boolean { return token === run }

export function invalidateNarrationPlayback(): void { run++ }

export function stopNarrationAudio(): void { current?.pause(); cancelClip?.(); current = null }

rpc.onNotification('narration/audioChunk', (params) => {
  const chunk = (Array.isArray(params) ? params[0] : params) as AudioChunk
  if (chunk && liveReading?.token === run) liveReading.receive(chunk)
})
