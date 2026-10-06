import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { type NarrationBook, type NarrationCast, type VoiceScope, type SystemVoice, type VoiceEngine, type VoiceBrief, type DesignedVoice, type AuditionClip, NARRATOR, type SegmentRef, type NarrationState } from './narrationTypes'
import { flatten, performedReading, spokenReading, beginNarrationPlayback, isNarrationPlaybackCurrent, invalidateNarrationPlayback, stopNarrationAudio } from './narrationPlayback'

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
  sceneNavigation: null,
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

  navigateToScene: (sceneId) => {
    get().stop()
    set({ sceneNavigation: { sceneId }, selected: null })
  },

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
    const token = beginNarrationPlayback()
    const performed = get().engines.some((e) => e.isReady)

    set({ preparing: true, readingError: null })
    try {
      if (performed) await performedReading(get, set, token, Math.max(0, from))
      else await spokenReading(get, set, token, Math.max(0, from))
    } finally {
      if (isNarrationPlaybackCurrent(token)) set({ speaking: null, preparing: false })
    }
  },

  // Nothing follows a press of Stop, so it need not be waited for.
  stop: () => {
    void get().stopAsync()
  },

  stopAsync: async () => {
    invalidateNarrationPlayback()
    set({ speaking: null })
    stopNarrationAudio()
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
    invalidateNarrationPlayback()
    stopNarrationAudio()
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
      selected: null,
      sceneNavigation: null
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

export * from './narrationTypes'
