import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { StreamingAudio } from '../audio/StreamingAudio'

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
  /** Stable identity inside the scene — one per utterance. What the prose
   *  frame marks up, what a clip comes back under, and what the follow-along
   *  highlight is matched on. */
  key: string
  /** The dialogue line this utterance belongs to. A speech of three sentences
   *  is three segments sharing one of these, and a speaker or a direction the
   *  writer sets belongs to the line the writer wrote rather than to the breath
   *  the model takes through it — so corrections are addressed to this. */
  lineKey: string
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

/**
 * A voice that only applies over part of the book.
 *
 * A character is not one voice for four hundred pages. They age, they are
 * injured, they are disguised, they are remembered as a child in a chapter set
 * thirty years earlier. Blank fields widen the stretch: an act alone is the
 * whole act, an act and a chapter the whole chapter, all three one scene.
 */
export interface VoiceScope {
  /** Empty for the narrator, who changes between a book's parts too. */
  characterId: string
  act: string | null
  /** The chapter's guid where the app wrote it, its title where a writer did. */
  chapter: string | null
  scene: string | null
  voiceId: string
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
  /** VoiceEngineFeatures as a bit field; see the feature constants below. */
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

/** VoiceEngineFeatures.EmotionInferred. Such an engine reads delivery from the
 * prose, so offering sliders or an emotion picker would promise controls that
 * are deliberately not sent to it. */
export const FEATURE_EMOTION_INFERRED = 1 << 4

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

/** One audition clip. Explicitly directed engines may return a range; inferred
 * engines return one fresh performance of the words. */
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
  /** The utterance — what is highlighted and what a clip is keyed by. */
  key: string
  /** The line it belongs to — what a speaker or a direction is written against.
   *  The same as `key` on narration and on any quoted line short enough not to
   *  have been split. */
  lineKey: string
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
  /**
   * Lines an engine is working on now, and lines already made.
   *
   * Shown on the page: a reading is built ahead of where it is being played,
   * and without seeing that a writer cannot tell a model thinking from a
   * feature that has stopped.
   */
  rendering: string[]
  ready: Record<string, boolean>
  /** True for the next window only: make it again rather than reusing what is
   *  on disk. Cleared as soon as it has been honoured. */
  rebuild: boolean
  /** Why the reading stopped early, or null when it did not. A reading that
   *  ends without a word about it looks exactly like one still thinking. */
  readingError: string | null
  /** The dimensions an engine takes direction in, from the backend rather than
   *  a second copy of the list here that could drift from it. */
  dimensions: string[]
  /** Standing registers by character id; the narrator's is under "". */
  registers: Record<string, Record<string, number>>
  /** Voices that apply over part of the book only. */
  scopes: VoiceScope[]
  engines: VoiceEngine[]
  designed: DesignedVoice[]
  /** The brief being reviewed, or null when the design dialog is closed. */
  brief: VoiceBrief | null
  /**
   * The stretch of book the brief is being designed for, or null for the
   * character's standing voice.
   *
   * Carried rather than passed, because it has to survive the round trip
   * through the dialog: the writer opens the brief from the cast rail, edits
   * it, and presses Design a minute later. Dropped here, an "older Mira in Act
   * Three" would come back as Mira everywhere.
   */
  briefScope: { act: string | null; chapter: string | null; scene: string | null } | null
  /**
   * A voice that has been designed and not yet kept, as a clip to listen to.
   *
   * Design is not reliable per attempt: the same description asked for twice
   * gives two voices, and one of them may not be what was asked for at all.
   * Keeping the first result outright made a miss into the character's voice
   * until somebody noticed.
   */
  candidate: string | null
  /**
   * The number the offered voice was drawn with.
   *
   * Shown so a writer who likes what they hear can ask for it again. Without it
   * a voice they heard once and did not keep is gone: design is not
   * reproducible, and nothing anywhere else remembers the draw.
   */
  candidateSeed: number | null
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
  openBrief(
    characterId: string,
    consent?: boolean,
    scope?: { act: string | null; chapter: string | null; scene: string | null } | null
  ): Promise<void>
  openNarratorBrief(): Promise<void>
  closeBrief(): void
  design(
    engineId: string,
    characterId: string,
    description: string,
    consent?: boolean,
    seed?: number | null
  ): Promise<void>
  designNarrator(engineId: string, description: string, seed?: number | null): Promise<void>
  /** Keeps the voice that was offered, storing it and casting whoever it is for. */
  keepVoice(): Promise<void>
  /** Throws the offered voice away. Nothing was stored, so this only forgets. */
  discardVoice(): Promise<void>
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
  loadScopes(): Promise<void>
  /** Casts somebody over one stretch of the book. A null voice clears it, which
   *  sends those lines back to their standing voice rather than silencing
   *  them. */
  setVoiceScope(
    characterId: string | null,
    where: { act: string | null; chapter: string | null; scene: string | null },
    voiceId: string | null
  ): Promise<void>
  setSpeaker(ref: SegmentRef, characterId: string | null): Promise<void>
  select(ref: SegmentRef | null): void
  setRate(rate: number): void
  play(from?: number): Promise<void>
  stop(): void
  /** Stops, and does not come back until the backend has actually stopped. */
  stopAsync(): Promise<void>
  /**
   * Throws the rendered reading away and reads it again from nothing.
   *
   * Delivery is not reproducible - the same line asked for twice comes back
   * differently - so this is the only way to get a second answer out of an
   * engine. Without it, the reuse that makes a reading fast would also make it
   * fixed.
   */
  readAgain(): Promise<void>
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
  rendering: [],
  ready: {},
  rebuild: false,
  readingError: null,
  dimensions: [],
  registers: {},
  scopes: [],
  engines: [],
  designed: [],
  brief: null,
  briefScope: null,
  candidate: null,
  candidateSeed: null,
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
      // A writer directs what they wrote, not the breaths a model takes
      // through it, so a direction is written against the line.
      ref.lineKey,
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
        // Lines rather than utterances, and each only once: three sentences
        // of one speech are one thing to direct, not three.
        [...new Set(group.map((r) => r.lineKey))],
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

  loadScopes: async () => {
    const scopes = await rpc.request<VoiceScope[]>('narration/voiceScopes').catch(() => [])
    set({ scopes })
  },

  setVoiceScope: async (characterId, where, voiceId) => {
    await rpc.request<boolean>('narration/setVoiceScope', [
      characterId ?? '',
      where.act,
      where.chapter,
      where.scene,
      voiceId
    ])
    // The book too, not only the scopes: which voice reads a line is resolved
    // per segment on the way out, so the reading is what actually changed.
    await Promise.all([get().loadScopes(), get().loadBook()])
  },

  // Reassigning a speaker goes through the Dialogue view's own store of
  // overrides, so a correction made while listening is the same correction
  // seen from the other view. One store, two views.
  setSpeaker: async (ref, characterId) => {
    await rpc.request<boolean>('dialogue/setSpeaker', [
      ref.chapterGuid,
      ref.sceneId,
      // The line, not the utterance. A speech cut into three sentences is still
      // one line of dialogue, and the Dialogue view knows it by that key.
      ref.lineKey,
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
  openBrief: async (characterId, consent = false, scope = null) => {
    set({ designError: null, audition: [], briefScope: scope })
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
    set({ designError: null, audition: [], briefScope: null })
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

  // Closing the dialog throws away whatever was offered and not kept, on the
  // backend as well as here - a voice nobody chose should not sit waiting to be
  // committed by the next Keep.
  closeBrief: () => {
    if (get().candidate !== null) void get().discardVoice()
    set({
      brief: null,
      briefScope: null,
      designError: null,
      audition: [],
      candidate: null,
      candidateSeed: null
    })
  },

  design: async (engineId, characterId, description, consent = false, seed = null) => {
    set({ busy: true, designError: null, candidate: null, candidateSeed: null })
    try {
      const result = await rpc.request<{
        voiceId: string | null
        error: string | null
        clip: string | null
        seed: number | null
      }>('voiceEngines/design', [
        engineId,
        characterId,
        description,
        consent,
        // Where in the book, when this voice is only for part of it. A scoped
        // design gets an id of its own so it cannot overwrite the standing one.
        get().briefScope?.act ?? null,
        get().briefScope?.chapter ?? null,
        get().briefScope?.scene ?? null,
        seed
      ])
      if (result.error !== null) {
        set({ designError: result.error })
        return
      }
      // Offered rather than kept: the dialog plays it and asks.
      set({ candidate: result.clip, candidateSeed: result.seed })
    } finally {
      set({ busy: false })
    }
  },

  keepVoice: async () => {
    await rpc.request<boolean>('voiceEngines/keepVoice')
    set({ brief: null, briefScope: null, candidate: null, candidateSeed: null })
    // Keeping casts them in it, so the cast and the book both change - and the
    // scopes, when the voice was designed for one stretch of it.
    await Promise.all([get().loadEngines(), get().loadCast(), get().loadScopes()])
    await get().loadBook()
  },

  discardVoice: async () => {
    set({ candidate: null, candidateSeed: null })
    await rpc.request<boolean>('voiceEngines/discardVoice').catch(() => {})
  },

  designNarrator: async (engineId, description, seed = null) => {
    set({ busy: true, designError: null, candidate: null, candidateSeed: null })
    try {
      const result = await rpc.request<{
        voiceId: string | null
        error: string | null
        clip: string | null
        seed: number | null
      }>('narration/designNarrator', [engineId, description, seed])
      if (result.error !== null) {
        set({ designError: result.error })
        return
      }
      set({ candidate: result.clip, candidateSeed: result.seed })
    } finally {
      set({ busy: false })
    }
  },

  forgetVoice: async (voiceId) => {
    await rpc.request<boolean>('voiceEngines/forget', [voiceId])
    // Scopes too: an override pointing at a voice that no longer exists beats
    // the character's real one, so those chapters would fall silently back to
    // the narrator while the rest of the book stayed right.
    await Promise.all([get().loadEngines(), get().loadCast(), get().loadScopes()])
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

    // Awaited. narration/renderStop deliberately skips the request queue so
    // that Stop is immediate, and a Play that fires it and moved on would be
    // racing its own stop.
    await get().stopAsync()
    const token = ++run
    const performed = get().engines.some((e) => e.isReady)

    set({ preparing: true, readingError: null })
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
    cancelClip?.()
    current = null
    set({ preparing: false })
    // The render is a separate thing to interrupt: an engine part way through a
    // window would otherwise finish it into a cache nobody is listening to.
    await Promise.allSettled([
      rpc.request('voices/stop'),
      rpc.request('narration/renderStop')
    ])
    // What was made stays made and stays marked. Stopping to fix a word and
    // pressing Play again is the commonest thing there is to do in this view,
    // and it should not cost the scene twice.
    set({ rendering: [] })
  },

  readAgain: async () => {
    await get().stopAsync()
    await rpc.request<boolean>('narration/renderAgain').catch(() => false)
    set({ heard: {}, ready: {}, rendering: [], rebuild: true, readingError: null })
    await get().play(0)
  },

  reset: () => {
    run++
    current?.pause()
    cancelClip?.()
    set({
      narratorVoiceId: null,
      members: [],
      unassignedCount: 0,
      voices: [],
      emotions: [],
      heard: {},
      rendering: [],
      ready: {},
      rebuild: false,
      readingError: null,
      dimensions: [],
      registers: {},
      scopes: [],
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
 * The line a speech engine is on right now, pushed as it happens.
 *
 * A render window is one request and one answer, so without this the page would
 * learn nothing for the whole of it - and marking the entire batch as "being
 * made" says a dozen sentences are being worked on when eleven of them have not
 * been started. What a writer wants to see is the line the model is on.
 */
rpc.onNotification('narration/making', (params) => {
  const said = Array.isArray(params) ? params[0] : params
  const key = (said as { key?: string | null } | undefined)?.key ?? null
  useNarrationStore.setState({ rendering: key === null ? [] : [key] })
})

interface AudioChunk {
  streamId: string
  key: string
  sequence: number
  sampleRate: number
  audio: string
}

let liveReading: { token: number; receive: (chunk: AudioChunk) => void } | null = null
rpc.onNotification('narration/audioChunk', (params) => {
  const chunk = (Array.isArray(params) ? params[0] : params) as AudioChunk
  if (chunk && liveReading?.token === run) liveReading.receive(chunk)
})

/**
 * The reading, performed by an engine.
 *
 * Two loops rather than one. Making the speech runs flat out, ahead of the
 * writer, into a queue; playing it drains that queue. They only meet at the
 * queue, which is what lets a fast stretch bank time against a slow one.
 *
 * They used to be a single loop that rendered one window while playing the
 * previous one, and each window was **twice** the last. That quietly demanded
 * an engine twice as fast as speech itself - while six lines played, twelve
 * were being made - so anything slower stalled at every boundary. Which is
 * exactly what it sounded like: a few sentences, a pause, a few more.
 */
async function performedReading(
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
