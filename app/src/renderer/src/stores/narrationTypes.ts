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

export interface NarrationCast {
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

export interface NarrationState {
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
  /** A fresh request also lets clicking the same binder scene reveal it again. */
  sceneNavigation: { sceneId: string } | null
  navigateToScene(sceneId: string): void
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

export interface AudioChunk {
  streamId: string
  key: string
  sequence: number
  sampleRate: number
  audio: string
}
