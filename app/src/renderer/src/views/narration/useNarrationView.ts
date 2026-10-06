import { useEffect, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { FEATURE_DESIGN, FEATURE_EMOTION_INFERRED, useNarrationStore } from '../../stores/narrationStore'
import { useProjectStore } from '../../stores/projectStore'
import { autoColour } from '../manuscript/sceneColour'
import { useNarrationFrame } from './useNarrationFrame'

/**
 * The book as it will be read aloud.
 *
 * The prose is the view. An earlier cut listed the segments as rows and it was
 * unreadable for the thing it is for: a reading is something you follow, and a
 * column of extracted fragments has no paragraphs, no emphasis and no place in
 * the book — so the writer could not tell where they were in their own scene.
 * The whole book is on one strip, marked up where it stands, and scrolling is
 * how you move. The binder can also reveal a scene within this reading without
 * changing the scene open in the writing editor.
 */
export function useNarrationView() {
  const { t } = useTranslation()
  const openSceneId = useProjectStore((s) => s.openSceneId)

  const book = useNarrationStore((s) => s.book)
  const reading = useNarrationStore((s) => s.reading)
  const members = useNarrationStore((s) => s.members)
  const voices = useNarrationStore((s) => s.voices)
  const narratorVoiceId = useNarrationStore((s) => s.narratorVoiceId)
  const unassignedCount = useNarrationStore((s) => s.unassignedCount)
  const loading = useNarrationStore((s) => s.loading)
  const speaking = useNarrationStore((s) => s.speaking)
  const selected = useNarrationStore((s) => s.selected)
  const sceneNavigation = useNarrationStore((s) => s.sceneNavigation)
  const rate = useNarrationStore((s) => s.rate)
  const setVoice = useNarrationStore((s) => s.setVoice)
  const setRate = useNarrationStore((s) => s.setRate)
  const play = useNarrationStore((s) => s.play)
  const stop = useNarrationStore((s) => s.stop)
  const preparing = useNarrationStore((s) => s.preparing)
  const busy = useNarrationStore((s) => s.busy)
  const prepareEngine = useNarrationStore((s) => s.prepareEngine)
  const engines = useNarrationStore((s) => s.engines)
  const designed = useNarrationStore((s) => s.designed)
  const brief = useNarrationStore((s) => s.brief)
  const ready = useNarrationStore((s) => s.ready)
  const rendering = useNarrationStore((s) => s.rendering)
  const readAgain = useNarrationStore((s) => s.readAgain)
  const readingError = useNarrationStore((s) => s.readingError)

  useNarrationLifecycle({engines, stop})

  /** A colour per speaker, so a page of prose says who is talking before you
   *  read a word of it. Hashed, like the corkboard's, so it needs no setup. */
  const colours = useMemo(() => {
    const map: Record<string, string> = {}
    for (const member of members) map[member.characterId] = autoColour(member.characterId)
    return map
  }, [members])

  /** Whether this voice deliberately takes its delivery from the prose instead
   *  of the host's detected/manual direction. */
  const voiceInfersDelivery = (voiceId: string | null): boolean => {
    const owner = designed.find((voice) => voice.voiceId === voiceId)?.engineId
    const features = engines.find((engine) => engine.engineId === owner)?.features ?? 0
    return (features & FEATURE_EMOTION_INFERRED) !== 0
  }

  const frameRef = useNarrationFrame({ book, colours, voiceInfersDelivery, t, sceneNavigation, speaking, ready, rendering, selected })

  const cast = members.filter((m) => m.voiceId !== null).length
  const canPlay = reading.some((step) => step.segment.voiceId)
  const noVoices = voices.length === 0 && designed.length === 0
  // The first engine that is installed, ready, and able to design. Everything
  // about designing stays hidden until there is one, rather than being offered
  // and then failing when pressed.
  const designer = engines.find((e) => e.isReady && (e.features & FEATURE_DESIGN) !== 0) ?? null
  const selectedStep = useMemo(
    () =>
      selected
        ? (reading.find(
            (step) => step.sceneId === selected.sceneId && step.segment.key === selected.key
          ) ?? null)
        : null,
    [reading, selected]
  )

  const playFrom = (): void => playNarrationSelection({selected, reading, sceneNavigation, play})

  return { t, narratorVoiceId, cast, members, voices, designed, setVoice, designer, colours, unassignedCount, noVoices, engines, busy, prepareEngine, frameRef, loading, book, selectedStep, brief, canPlay, speaking, preparing, playFrom, stop, selected, sceneNavigation, readAgain, openSceneId, rate, setRate, readingError, reading }
}

export type NarrationState = ReturnType<typeof useNarrationStore.getState>

function useNarrationLifecycle({engines, stop}: Pick<NarrationState, 'engines' | 'stop'>): void {
  const loadCast = useNarrationStore((s) => s.loadCast)
  const loadBook = useNarrationStore((s) => s.loadBook)
  const loadEngines = useNarrationStore((s) => s.loadEngines)
  const loadRegisters = useNarrationStore((s) => s.loadRegisters)
  const loadScopes = useNarrationStore((s) => s.loadScopes)

  useEffect(() => {
    void loadCast()
    void loadBook()
    void loadEngines()
    // Both were only ever loaded as a side effect of setting one, so a book
    // that already had standing registers or scoped voices opened showing
    // neither - the marks that say "this character has one" were blank until
    // the writer set another.
    void loadRegisters()
    void loadScopes()
  }, [loadCast, loadBook, loadEngines, loadRegisters, loadScopes])

  // An engine that is getting itself ready will be ready shortly, and nothing
  // will say so unless somebody asks again. Without this the rail read "not
  // ready" with no design buttons for the whole of a model load, and the only
  // way to see it finish was to leave the view and come back.
  const starting = engines.some((engine) => engine.isPreparing)
  useEffect(() => {
    if (!starting) return
    const timer = setInterval(() => void loadEngines(), 2000)
    return () => clearInterval(timer)
  }, [starting, loadEngines])

  // Stopping on the way out matters more than it looks: the platform engine
  // keeps speaking after the view is gone, and there would be no control left
  // on screen to stop it with.
  useEffect(() => () => stop(), [stop])

}

export type NarrationViewState = ReturnType<typeof useNarrationView>

function playNarrationSelection({selected, reading, sceneNavigation, play}: Pick<NarrationState, 'selected' | 'reading' | 'sceneNavigation' | 'play'>): void {
    if (!selected) {
      const at = reading.findIndex((step) => step.sceneId === sceneNavigation?.sceneId)
      void play(Math.max(0, at))
      return
    }
    const at = reading.findIndex(
      (step) => step.sceneId === selected.sceneId && step.segment.key === selected.key
    )
    void play(at < 0 ? 0 : at)
  }
