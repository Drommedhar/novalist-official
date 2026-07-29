import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useProjectStore } from './projectStore'

export interface Sprint {
  startedAt: string
  seconds: number
  targetMinutes: number
  words: number
  /** Zero for a sprint too short to divide by. */
  wordsPerMinute: number
}

export interface SprintSummary {
  count: number
  totalWords: number
  totalSeconds: number
  bestWords: number
  averageWordsPerMinute: number
}

interface SprintState {
  /** Null when no sprint is running. */
  startedAt: number | null
  /** Words in the project when the sprint began. */
  baselineWords: number
  /** Seconds already banked from before the last pause. */
  bankedSeconds: number
  running: boolean
  targetMinutes: number
  /** Ticks once a second while running, purely to force a re-render. */
  tick: number

  history: Sprint[]
  summary: SprintSummary | null

  start: () => void
  pause: () => void
  resume: () => void
  stop: () => Promise<void>
  setTarget: (minutes: number) => void
  loadHistory: () => Promise<void>
  clearHistory: () => Promise<void>
}

/** Total words in the open book. The live figure the sprint measures against. */
function projectWords(): number {
  return useProjectStore
    .getState()
    .chapters.reduce(
      (total, chapter) => total + chapter.scenes.reduce((n, scene) => n + scene.wordCount, 0),
      0
    )
}

let ticker: number | null = null

function startTicker(): void {
  if (ticker !== null) return
  ticker = window.setInterval(() => {
    useSprintStore.setState((s) => ({ tick: s.tick + 1 }))
  }, 1000)
}

function stopTicker(): void {
  if (ticker === null) return
  window.clearInterval(ticker)
  ticker = null
}

/**
 * A writing sprint: a clock, the words added since it began, and the pace.
 *
 * Novalist's smallest unit was a calendar day, so it could not report words
 * written this sitting - the only figure that means anything while the writer
 * is still in the chair.
 */
export const useSprintStore = create<SprintState>((set, get) => ({
  startedAt: null,
  baselineWords: 0,
  bankedSeconds: 0,
  running: false,
  targetMinutes: 25,
  tick: 0,
  history: [],
  summary: null,

  start: () => {
    startTicker()
    set({
      startedAt: Date.now(),
      baselineWords: projectWords(),
      bankedSeconds: 0,
      running: true
    })
  },

  pause: () => {
    const { startedAt, bankedSeconds, running } = get()
    if (!running || startedAt === null) return
    stopTicker()
    // Bank what has elapsed so resuming does not restart the clock.
    set({
      running: false,
      bankedSeconds: bankedSeconds + Math.floor((Date.now() - startedAt) / 1000),
      startedAt: null
    })
  },

  resume: () => {
    if (get().running) return
    startTicker()
    set({ startedAt: Date.now(), running: true })
  },

  stop: async () => {
    const { startedAt, bankedSeconds, baselineWords, targetMinutes } = get()
    stopTicker()
    const seconds =
      bankedSeconds + (startedAt === null ? 0 : Math.floor((Date.now() - startedAt) / 1000))
    const words = projectWords() - baselineWords
    const startedIso = new Date(Date.now() - seconds * 1000).toISOString()

    set({ startedAt: null, running: false, bankedSeconds: 0, baselineWords: 0 })

    const result = await rpc.request<{ sprints: Sprint[]; summary: SprintSummary }>(
      'sprints/record',
      [seconds, targetMinutes, words, startedIso]
    )
    set({ history: result.sprints, summary: result.summary })
  },

  setTarget: (minutes) => set({ targetMinutes: Math.max(0, minutes) }),

  loadHistory: async () => {
    const result = await rpc.request<{ sprints: Sprint[]; summary: SprintSummary }>(
      'sprints/history'
    )
    set({ history: result.sprints, summary: result.summary })
  },

  clearHistory: async () => {
    const result = await rpc.request<{ sprints: Sprint[]; summary: SprintSummary }>('sprints/clear')
    set({ history: result.sprints, summary: result.summary })
  }
}))

/** Seconds the current sprint has run, banked plus live. */
export function elapsedSeconds(): number {
  const { startedAt, bankedSeconds } = useSprintStore.getState()
  return bankedSeconds + (startedAt === null ? 0 : Math.floor((Date.now() - startedAt) / 1000))
}

/** Words added since the sprint began. Never negative: a deletion pass reads as
 *  zero rather than as going backwards. */
export function sprintWords(): number {
  const { baselineWords, startedAt, bankedSeconds } = useSprintStore.getState()
  if (startedAt === null && bankedSeconds === 0) return 0
  return Math.max(0, projectWords() - baselineWords)
}

/** Whole minutes, m:ss. */
export function formatDuration(seconds: number): string {
  const mins = Math.floor(seconds / 60)
  const secs = seconds % 60
  return `${mins}:${String(secs).padStart(2, '0')}`
}
