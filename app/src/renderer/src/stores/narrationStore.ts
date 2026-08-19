import { create } from 'zustand'
import { rpc } from '../rpc/client'

// Mirrors the NarrationRpc DTOs (camelCase over the wire).

/** Whether a segment is somebody speaking or the prose around it. */
export type NarrationSegmentKind = 'Narration' | 'Dialogue'

/** How a segment's direction was arrived at. Shown so a guess never reads as a
 *  decision — "Writer" is the writer's own, "None" means nothing was said. */
export type DirectionSource = 'Writer' | 'Verb' | 'Scene' | 'None'

/** How firmly a line is tied to its speaker. Same vocabulary as the Dialogue
 *  view, because it is the same attribution. */
export type NarrationConfidence = 'Manual' | 'High' | 'Inferred' | 'Medium' | 'Low' | 'None'

export interface NarrationCandidate {
  characterId: string
  name: string
  percent: number
}

export interface NarrationSegment {
  index: number
  kind: NarrationSegmentKind
  /** Stable identity inside the scene — what a direction or a speaker
   *  reassignment is keyed by, and what the prose frame marks up. */
  key: string
  text: string
  /** Null for narration, and for a line nobody could be found for. Either way
   *  the narrator reads it. */
  speakerId: string | null
  speakerName: string | null
  confidence: NarrationConfidence
  candidates: NarrationCandidate[]
  directionKey: string
  directionSource: DirectionSource
  /** The speech verb behind a "Verb" direction, so the chip can say why. */
  directionEvidence: string | null
  /** The eight dimensions this line will actually be performed at, standing
   *  register included — so the sliders open on what is set. */
  directionVector: Record<string, number>
  /** The clip this line was told to sound like, when one was pointed at. */
  directionClip: string | null
  /** The voice this resolves to, narrator fallback already applied. Null means
   *  nothing is cast at all yet. */
  voiceId: string | null
}

export interface NarrationProseScene {
  chapterGuid: string
  sceneId: string
  sceneTitle: string
  sceneEmotion: string | null
  sceneIntensity: number | null
  /** The scene's own HTML with a marker round every segment. */
  html: string
  segments: NarrationSegment[]
}

export interface NarrationChapter {
  guid: string
  title: string
  act: string
  scenes: NarrationProseScene[]
}

export interface NarrationBook {
  chapters: NarrationChapter[]
  spokenCount: number
}

export interface NarrationCastMember {
  characterId: string
  name: string
  lineCount: number
  voiceId: string | null
}

interface NarrationCast {
  narratorVoiceId: string | null
  members: NarrationCastMember[]
  unassignedCount: number
}

export interface SystemVoice {
  id: string
  name: string
  language: string
}


/** An installed speech engine, and whether it can be used yet. */
export interface VoiceEngine {
  engineId: string
  engineName: string
  /** VoiceEngineFeatures as a bit field; see FEATURE_DESIGN below. */
  features: number
  isReady: boolean
  isPreparing: boolean
  error: string | null
  detail: string
  downloadBytes: number | null
}

/** VoiceEngineFeatures.DesignFromDescription. The only flag the view branches
 *  on so far: an engine that cannot design a voice is offered no design
 *  button rather than one that fails when pressed. */
export const FEATURE_DESIGN = 1 << 0

/** What a character's voice would be designed from, for the writer to read and
 *  edit before anything is sent. */
export interface VoiceBrief {
  characterId: string
  name: string
  description: string
  sampleLines: string[]
  /** "None", or "WithheldFromAi" when the writer set the entry never to reach a
   *  model. A local model is still a model. */
  refusal: string
}

/** A voice this book has been given. */
export interface DesignedVoice {
  voiceId: string
  displayName: string
  description: string
  engineId: string
  designedAt: string
}

/** One rendered segment of the reading. */
export interface NarrationClip {
  key: string
  /** The name to fetch the audio by, or null when this segment could not be
   *  spoken. */
  clip: string | null
  durationMs: number
  error: string | null
}

/** What one render window produced. */
export interface NarrationRender {
  /** Null when no engine is ready — the signal to read with system voices. */
  engineId: string | null
  clips: NarrationClip[]
  total: number
}

/** One audition clip: the same line read at one point on the emotional range. */
export interface AuditionClip {
  key: string
  /** base64 audio, played straight from a data URI. */
  audio: string
  audioFormat: string
  sampleRate: number
  durationMs: number
  error: string | null
}

/** One segment, with the scene it belongs to — the reading as one flat run, in
 *  book order, which is what playback walks and what "the next line" means. */
export interface ReadingStep {
  chapterGuid: string
  sceneId: string
  sceneTitle: string
  chapterTitle: string
  segment: NarrationSegment
}

/** The brief's character id when the brief is the narrator's. The narrator is
 *  not a character and has no id of their own, and an empty string is what the
 *  cast sheet already uses to mean them. */
export const NARRATOR = ''

/** Which segment the writer is looking at. */
export interface SegmentRef {
  chapterGuid: string
  sceneId: string
  key: string
}

/**
 * Lines rendered this session, by segment key, as clip names.
 *
 * "Point at a line already rendered the way you wanted" only means anything
 * about deliveries the writer has actually heard, so this is the reading so far
 * rather than the whole book. Cleared with the cache, which is what the backend
 * does when a reading stops.
 */
export type HeardClips = Record<string, string>

interface NarrationState {
  narratorVoiceId: string | null
  members: NarrationCastMember[]
  unassignedCount: number
  voices: SystemVoice[]
  emotions: string[]
  /** Lines performed this session, as clip names, for "like that". */
  heard: HeardClips
  /** The dimensions an engine takes direction in, from the backend rather than
   *  a second copy of the list here that could drift from it. */
  dimensions: string[]
  /** Standing registers by character id; the narrator's is under "". */
  registers: Record<string, Record<string, number>>
  engines: VoiceEngine[]
  designed: DesignedVoice[]
  /** The brief being reviewed, or null when the design dialog is closed. */
  brief: VoiceBrief | null
  /** True while an engine is designing or preparing - both are slow enough to
   *  need saying so. */
  busy: boolean
  designError: string | null
  audition: AuditionClip[]
  book: NarrationBook | null
  reading: ReadingStep[]
  loading: boolean
  /** The segment being spoken, or null when nothing is. */
  speaking: SegmentRef | null
  /** True between pressing Play and the first sound. Rendering speech takes
   *  seconds, and a transport that says nothing for that long reads as a
   *  button that did not work. */
  preparing: boolean
  /** The segment the writer has picked, whose controls the panel shows. */
  selected: SegmentRef | null
  rate: number

  loadCast(): Promise<void>
  loadBook(): Promise<void>
  loadEngines(): Promise<void>
  prepareEngine(engineId: string): Promise<void>
  openBrief(characterId: string, consent?: boolean): Promise<void>
  openNarratorBrief(): Promise<void>
  closeBrief(): void
  design(engineId: string, characterId: string, description: string, consent?: boolean): Promise<void>
  designNarrator(engineId: string, description: string): Promise<void>
  forgetVoice(voiceId: string): Promise<void>
  auditionVoice(voiceId: string, text: string): Promise<void>
  setVoice(characterId: string | null, voiceId: string | null): Promise<void>
  setDirection(
    ref: SegmentRef,
    emotionKey: string | null,
    vector?: Record<string, number> | null,
    referenceClip?: string | null
  ): Promise<void>
  /** The same direction across a run of lines — a whole argument, a whole
   *  eulogy — so one performance is set once. */
  setDirections(
    refs: SegmentRef[],
    emotionKey: string | null,
    vector?: Record<string, number> | null,
    referenceClip?: string | null
  ): Promise<void>
  /** A character's standing register. A blank id is the narrator. */
  setRegister(characterId: string | null, vector: Record<string, number> | null): Promise<void>
  loadRegisters(): Promise<void>
  setSpeaker(ref: SegmentRef, characterId: string | null): Promise<void>
  select(ref: SegmentRef | null): void
  setRate(rate: number): void
  play(from?: number): Promise<void>
  stop(): void
  /** Stops, and does not come back until the backend has actually stopped. */
  stopAsync(): Promise<void>
  reset(): void
}

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

/** How many segments to render at a time. Small enough that stopping is quick
 *  and that pressing Play does not wait for the whole chapter. */
const RENDER_WINDOW = 12

/**
 * How many lines the first request asks for.
 *
 * A window is rendered before any of it is played, so asking for twelve up
 * front means the writer presses Play and waits for all twelve - ten or twenty
 * seconds of nothing, on the engine that most needs a reading to feel live.
 * The first request is small so a voice starts almost at once; the ones after
 * it are full size and are rendered while that first stretch is still playing.
 */
const FIRST_WINDOW = 2

/** Plays one rendered clip, resolving when it finishes, is stopped, or fails.
 *  Never rejects: a clip that will not play ends the reading through the same
 *  path as one that finished. */
function playClip(name: string): Promise<void> {
  return new Promise((resolve) => {
    const audio = new Audio(`novalist-audio://clip/${encodeURIComponent(name)}`)
    current = audio
    const done = (): void => {
      audio.onended = null
      audio.onerror = null
      if (current === audio) current = null
      resolve()
    }
    audio.onended = done
    audio.onerror = done
    void audio.play().catch(done)
  })
}

/** The reading as one run, in book order. */
function flatten(book: NarrationBook | null): ReadingStep[] {
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

export const useNarrationStore = create<NarrationState>((set, get) => ({
  narratorVoiceId: null,
  members: [],
  unassignedCount: 0,
  voices: [],
  emotions: [],
  heard: {},
  dimensions: [],
  registers: {},
  engines: [],
  designed: [],
  brief: null,
  busy: false,
  designError: null,
  audition: [],
  book: null,
  reading: [],
  loading: false,
  speaking: null,
  preparing: false,
  selected: null,
  rate: 1,

  loadCast: async () => {
    const [cast, voices, emotions, dimensions, registers] = await Promise.all([
      rpc.request<NarrationCast>('narration/cast'),
      rpc.request<SystemVoice[]>('voices/list'),
      rpc.request<string[]>('narration/emotions'),
      rpc.request<string[]>('narration/dimensions').catch(() => []),
      rpc
        .request<Record<string, Record<string, number>>>('narration/registers')
        .catch(() => ({}))
    ])
    set({
      narratorVoiceId: cast.narratorVoiceId,
      members: cast.members,
      unassignedCount: cast.unassignedCount,
      voices,
      emotions,
      dimensions,
      registers
    })
  },

  loadBook: async () => {
    // Rebuilding the book ends whatever was being read: the segment the loop is
    // holding may not exist in what comes back.
    get().stop()
    set({ loading: true })
    const book = await rpc.request<NarrationBook>('narration/book')
    const reading = flatten(book)
    // A selection that survived the reload stays; one whose segment is gone -
    // because its words were edited - is dropped rather than left pointing at
    // nothing.
    const selected = get().selected
    const stillThere =
      selected !== null &&
      reading.some((step) => step.sceneId === selected.sceneId && step.segment.key === selected.key)
    set({ book, reading, loading: false, selected: stillThere ? selected : null })
  },

  // Casting changes which voice a segment resolves to, so the book is rebuilt
  // rather than patched — the narrator fallback means one character's voice can
  // change what half the book sounds like.
  setVoice: async (characterId, voiceId) => {
    await rpc.request<boolean>('narration/setVoice', [characterId, voiceId])
    await get().loadCast()
    await get().loadBook()
  },

  setDirection: async (ref, emotionKey, vector = null, referenceClip = null) => {
    await rpc.request<boolean>('narration/setDirection', [
      ref.chapterGuid,
      ref.sceneId,
      ref.key,
      emotionKey,
      vector,
      referenceClip
    ])
    await get().loadBook()
  },

  setDirections: async (refs, emotionKey, vector = null, referenceClip = null) => {
    if (refs.length === 0) return
    // A run has to be one scene to be one call, and directing across a scene
    // break is not a thing anybody means by "this whole argument".
    const byScene = new Map<string, SegmentRef[]>()
    for (const ref of refs) {
      const scene = `${ref.chapterGuid}\u001f${ref.sceneId}`
      byScene.set(scene, [...(byScene.get(scene) ?? []), ref])
    }
    for (const group of byScene.values()) {
      await rpc.request<boolean>('narration/setDirections', [
        group[0].chapterGuid,
        group[0].sceneId,
        group.map((r) => r.key),
        emotionKey,
        vector,
        referenceClip
      ])
    }
    await get().loadBook()
  },

  setRegister: async (characterId, vector) => {
    await rpc.request<boolean>('narration/setRegister', [characterId ?? '', vector])
    await Promise.all([get().loadRegisters(), get().loadBook()])
  },

  loadRegisters: async () => {
    const registers = await rpc
      .request<Record<string, Record<string, number>>>('narration/registers')
      .catch(() => ({}))
    set({ registers })
  },

  // Reassigning a speaker goes through the Dialogue view's own store of
  // overrides, so a correction made while listening is the same correction
  // seen from the other view. One store, two views.
  setSpeaker: async (ref, characterId) => {
    await rpc.request<boolean>('dialogue/setSpeaker', [
      ref.chapterGuid,
      ref.sceneId,
      ref.key,
      characterId
    ])
    await Promise.all([get().loadCast(), get().loadBook()])
  },


  loadEngines: async () => {
    const [engines, designed] = await Promise.all([
      rpc.request<VoiceEngine[]>('voiceEngines/list'),
      rpc.request<DesignedVoice[]>('voiceEngines/voices')
    ])
    set({ engines, designed })
  },

  prepareEngine: async (engineId) => {
    set({ busy: true })
    try {
      await rpc.request<VoiceEngine | null>('voiceEngines/prepare', [engineId])
    } catch {
      // Swallowed on purpose: whatever went wrong, the engine's own status is
      // where the reason lives, and it is fetched below either way. Letting this
      // propagate meant a failed prepare refreshed nothing at all - the dialog
      // closed, the rail said what it said before, and the writer was told
      // nothing.
    } finally {
      set({ busy: false })
      await get().loadEngines()
    }
  },

  // The brief is shown before anything is designed. A prompt assembled
  // invisibly is one the writer cannot correct, and this one is assembled from
  // fields they may never have thought of as describing a voice.
  openBrief: async (characterId, consent = false) => {
    set({ designError: null, audition: [] })
    const brief = await rpc.request<VoiceBrief | null>('voiceEngines/brief', [
      characterId,
      consent
    ])
    set({ brief })
  },

  // The narrator has no Codex entry to read: what decides how a book should be
  // narrated is what kind of book it is and who is telling it, which the writer
  // declared on the book itself.
  openNarratorBrief: async () => {
    set({ designError: null, audition: [] })
    const description = await rpc.request<string>('narration/narratorBrief')
    set({
      brief: {
        characterId: NARRATOR,
        name: '',
        description,
        sampleLines: [],
        refusal: 'None'
      }
    })
  },

  closeBrief: () => set({ brief: null, designError: null, audition: [] }),

  design: async (engineId, characterId, description, consent = false) => {
    set({ busy: true, designError: null })
    try {
      const result = await rpc.request<{ voiceId: string | null; error: string | null }>(
        'voiceEngines/design',
        [engineId, characterId, description, consent]
      )
      if (result.error !== null) {
        set({ designError: result.error })
        return
      }
      set({ brief: null })
      // Designing casts them in it, so the cast and the book both change.
      await Promise.all([get().loadEngines(), get().loadCast()])
      await get().loadBook()
    } finally {
      set({ busy: false })
    }
  },

  designNarrator: async (engineId, description) => {
    set({ busy: true, designError: null })
    try {
      const result = await rpc.request<{ voiceId: string | null; error: string | null }>(
        'narration/designNarrator',
        [engineId, description]
      )
      if (result.error !== null) {
        set({ designError: result.error })
        return
      }
      set({ brief: null })
      await Promise.all([get().loadEngines(), get().loadCast()])
      await get().loadBook()
    } finally {
      set({ busy: false })
    }
  },

  forgetVoice: async (voiceId) => {
    await rpc.request<boolean>('voiceEngines/forget', [voiceId])
    await Promise.all([get().loadEngines(), get().loadCast()])
    await get().loadBook()
  },

  // Three readings rather than one: a single neutral sample says nothing about
  // whether the casting holds when the character is actually feeling something.
  auditionVoice: async (voiceId, text) => {
    set({ busy: true, audition: [] })
    try {
      const clips = await rpc.request<AuditionClip[]>('voiceEngines/audition', [voiceId, text])
      set({ audition: clips })
    } finally {
      set({ busy: false })
    }
  },

  select: (ref) => set({ selected: ref }),

  setRate: (rate) => set({ rate: Math.min(2, Math.max(0.5, rate)) }),

  /**
   * Reads from `from` to the end of the book, one segment at a time.
   *
   * Sequential rather than queued, because the highlight has to follow the
   * voice: the platform engine reports only that a passage finished, so the
   * only way to know what is being spoken is to speak one thing at a time.
   *
   * A segment with no voice is skipped rather than allowed to stall the
   * reading — that only happens when nothing at all is cast, and the view says
   * so rather than sitting silent.
   */
  play: async (from = 0) => {
    const reading = get().reading
    if (reading.length === 0) return

    // Awaited. Stopping empties the clip cache on the backend, and
    // narration/renderStop deliberately skips the request queue so that Stop is
    // immediate - which means a Play that fires it and moves on has its own
    // first clips deleted out from under it a moment later. The log said so
    // exactly: "clips=1 ... cacheBytes=0", and no sound.
    await get().stopAsync()
    const token = ++run
    const performed = get().engines.some((e) => e.isReady)

    set({ preparing: true })
    try {
      if (performed) await performedReading(get, set, token, Math.max(0, from))
      else await spokenReading(get, set, token, Math.max(0, from))
    } finally {
      if (token === run) set({ speaking: null, preparing: false })
    }
  },

  // Nothing follows a press of Stop, so it need not be waited for.
  stop: () => {
    void get().stopAsync()
  },

  stopAsync: async () => {
    run++
    set({ speaking: null })
    current?.pause()
    current = null
    set({ preparing: false })
    // The render is a separate thing to interrupt: an engine part way through a
    // window would otherwise finish it into a cache nobody is listening to.
    await Promise.allSettled([
      rpc.request('voices/stop'),
      rpc.request('narration/renderStop')
    ])
  },

  reset: () => {
    run++
    set({
      narratorVoiceId: null,
      members: [],
      unassignedCount: 0,
      voices: [],
      emotions: [],
      heard: {},
      dimensions: [],
      registers: {},
      engines: [],
      designed: [],
      brief: null,
      busy: false,
      designError: null,
      audition: [],
      book: null,
      reading: [],
      speaking: null,
      preparing: false,
      selected: null
    })
  }
}))

/**
 * The reading, performed by an engine.
 *
 * Rendered a window at a time and played as each window arrives, so pressing
 * Play does not wait for the chapter and pressing Stop does not throw away a
 * chapter's worth of work. The window the interface asks for is the window the
 * backend renders, so advancing by what was asked for keeps the two in step
 * even where a segment had no voice and produced no clip.
 */
async function performedReading(
  get: () => NarrationState,
  set: (partial: Partial<NarrationState>) => void,
  token: number,
  from: number
): Promise<void> {
  const reading = get().reading
  let index = from
  // Small first, so a voice starts almost at once rather than after the whole
  // window has been rendered.
  let asked = FIRST_WINDOW

  while (index < reading.length) {
    if (token !== run) return
    const window = await rpc.request<NarrationRender>('narration/render', [
      index,
      asked,
      get().rate
    ])
    if (token !== run) return
    // No engine after all - it went away between the check and the call.
    if (window.engineId === null) return await spokenReading(get, set, token, index)

    for (const clip of window.clips) {
      if (token !== run) return
      const step = reading.find((s) => s.segment.key === clip.key)
      if (step) {
        set({
          speaking: { chapterGuid: step.chapterGuid, sceneId: step.sceneId, key: step.segment.key }
        })
      }
      // A segment the engine refused ends the reading rather than being skipped
      // past: something is wrong, and reading on would hide it.
      if (clip.clip === null) return
      // Remembered before it is played, so a delivery the writer liked can be
      // pointed at afterwards.
      set({ heard: { ...get().heard, [clip.key]: clip.clip }, preparing: false })
      await playClip(clip.clip)
    }

    index += asked
    asked = RENDER_WINDOW
  }
}

/**
 * The reading, spoken by the voices the machine already has.
 *
 * One passage at a time, because the platform engine reports only that a
 * passage finished - so speaking one thing at a time is the only way to know
 * what is being said.
 */
async function spokenReading(
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
      speaking: { chapterGuid: step.chapterGuid, sceneId: step.sceneId, key: step.segment.key }
    })
    try {
      await rpc.request<boolean>('voices/speak', [
        step.segment.text,
        step.segment.voiceId,
        get().rate
      ])
    } catch {
      // A passage the engine refused ends the reading rather than skipping
      // silently down the book.
      return
    }
  }
}
