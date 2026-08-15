import { create } from 'zustand'

/**
 * Onboarding is installation-local UI state. It must never travel with a book
 * or contain anything taken from one, so the persisted shape is deliberately
 * limited to stable feature identifiers and booleans.
 */
export const ONBOARDING_STORAGE_KEY = 'nl.onboarding'
export const LEGACY_TOUR_SEEN_KEY = 'nl.tour.seen'
export const ONBOARDING_SCHEMA_VERSION = 1

export type TourStatus = 'unseen' | 'completed' | 'skipped'
export type OnboardingTipStatus = 'completed' | 'dismissed'

/** Stable identifiers let a new tip ship without replaying the whole tour. */
export type OnboardingTipId =
  | 'editor-autosave'
  | 'focus-peek'
  | 'focus-peek-actions'
  | 'focus-mode-exit'

export interface OnboardingProgress {
  version: typeof ONBOARDING_SCHEMA_VERSION
  tour: TourStatus
  tipsEnabled: boolean
  tips: Partial<Record<OnboardingTipId, OnboardingTipStatus>>
  /**
   * Which views have introduced themselves.
   *
   * The walkthrough visits seven views and the app has twenty-two, so most of
   * them a writer arrives at cold - and a screen you have arrived at cold is
   * one you have to work out from its controls. Each says what it is for the
   * first time you open it, once, and then never again.
   *
   * Keyed by view name only. Like the rest of this record it is installation
   * state and must never carry anything taken from a book.
   */
  viewIntros: Record<string, OnboardingTipStatus>
}

export interface StorageLike {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

interface OnboardingState extends OnboardingProgress {
  completeTour(): void
  skipTour(): void
  completeTip(id: OnboardingTipId): void
  dismissTip(id: OnboardingTipId): void
  /** Marks a view as having introduced itself, however the card was closed. */
  closeViewIntro(view: string): void
  /** Whether this view still owes the writer an introduction. */
  shouldIntroduceView(view: string): boolean
  setTipsEnabled(enabled: boolean): void
  shouldShowTip(id: OnboardingTipId): boolean
  reset(): void
}

const DEFAULT_PROGRESS: OnboardingProgress = {
  version: ONBOARDING_SCHEMA_VERSION,
  tour: 'unseen',
  tipsEnabled: true,
  tips: {},
  viewIntros: {}
}

const TIP_IDS: ReadonlySet<string> = new Set<OnboardingTipId>([
  'editor-autosave',
  'focus-peek',
  'focus-peek-actions',
  'focus-mode-exit'
])

function browserStorage(): StorageLike | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function tourStatus(value: unknown): TourStatus {
  return value === 'completed' || value === 'skipped' ? value : 'unseen'
}

function tipStatus(value: unknown): OnboardingTipStatus | undefined {
  return value === 'completed' || value === 'dismissed' ? value : undefined
}

/**
 * Normalizes persisted data rather than trusting it. Older/future builds can
 * leave a partial object behind; malformed state should cost a tip, never boot.
 */
export function migrateOnboardingProgress(
  raw: unknown,
  legacyTourSeen = false
): OnboardingProgress {
  if (!isRecord(raw)) {
    return {
      ...DEFAULT_PROGRESS,
      tour: legacyTourSeen ? 'completed' : 'unseen'
    }
  }

  const tips: OnboardingProgress['tips'] = {}
  if (isRecord(raw.tips)) {
    for (const candidate of Object.keys(raw.tips)) {
      if (!TIP_IDS.has(candidate)) continue
      const id = candidate as OnboardingTipId
      const status = tipStatus(raw.tips[id])
      if (status) tips[id] = status
    }
  }

  // A record written before view introductions existed simply has none, which
  // is the same as a fresh install as far as they are concerned: somebody who
  // has been using Novalist for months has still never been told what the
  // Planning board is for.
  const viewIntros: OnboardingProgress['viewIntros'] = {}
  if (isRecord(raw.viewIntros)) {
    for (const view of Object.keys(raw.viewIntros)) {
      const status = tipStatus(raw.viewIntros[view])
      if (status) viewIntros[view] = status
    }
  }

  return {
    version: ONBOARDING_SCHEMA_VERSION,
    tour: legacyTourSeen && tourStatus(raw.tour) === 'unseen' ? 'completed' : tourStatus(raw.tour),
    tipsEnabled: typeof raw.tipsEnabled === 'boolean' ? raw.tipsEnabled : true,
    tips,
    viewIntros
  }
}

export function readOnboardingProgress(storage: StorageLike | null = browserStorage()): OnboardingProgress {
  if (!storage) return { ...DEFAULT_PROGRESS, tips: {}, viewIntros: {} }
  let legacyTourSeen = false
  try {
    legacyTourSeen = storage.getItem(LEGACY_TOUR_SEEN_KEY) === '1'
    const value = storage.getItem(ONBOARDING_STORAGE_KEY)
    const parsed: unknown = value ? JSON.parse(value) : null
    return migrateOnboardingProgress(parsed, legacyTourSeen)
  } catch {
    // A truncated newer record must not make somebody who completed the legacy
    // tour take it again. Normalization will replace it on the next write.
    return migrateOnboardingProgress(null, legacyTourSeen)
  }
}

function persist(progress: OnboardingProgress, storage: StorageLike | null = browserStorage()): void {
  if (!storage) return
  try {
    storage.setItem(ONBOARDING_STORAGE_KEY, JSON.stringify(progress))
    // Once the richer shape is safely written there is one source of truth.
    storage.removeItem(LEGACY_TOUR_SEEN_KEY)
  } catch {
    // Blocked/full storage makes tips repeat; it must never block the interface.
  }
}

const initial = readOnboardingProgress()

function progressOf(state: OnboardingState): OnboardingProgress {
  return {
    version: ONBOARDING_SCHEMA_VERSION,
    tour: state.tour,
    tipsEnabled: state.tipsEnabled,
    tips: state.tips,
    viewIntros: state.viewIntros
  }
}

export const useOnboardingStore = create<OnboardingState>((set, get) => ({
  ...initial,
  completeTour: () =>
    set((state) => {
      const next = { ...progressOf(state), tour: 'completed' as const }
      persist(next)
      return next
    }),
  skipTour: () =>
    set((state) => {
      // Replaying a completed tour and closing it is not a regression. Keeping
      // completion also makes future migrations free to distinguish people who
      // finished the walkthrough from those who opted out on first run.
      const next = {
        ...progressOf(state),
        tour: state.tour === 'completed' ? ('completed' as const) : ('skipped' as const)
      }
      persist(next)
      return next
    }),
  completeTip: (id) =>
    set((state) => {
      const next = {
        ...progressOf(state),
        tips: { ...state.tips, [id]: 'completed' as const }
      }
      persist(next)
      return next
    }),
  dismissTip: (id) =>
    set((state) => {
      const next = {
        ...progressOf(state),
        tips: { ...state.tips, [id]: 'dismissed' as const }
      }
      persist(next)
      return next
    }),
  setTipsEnabled: (tipsEnabled) =>
    set((state) => {
      const next = { ...progressOf(state), tipsEnabled }
      persist(next)
      return next
    }),
  closeViewIntro: (view) =>
    set((state) => {
      const next = {
        ...progressOf(state),
        viewIntros: { ...state.viewIntros, [view]: 'completed' as const }
      }
      persist(next)
      return next
    }),
  shouldShowTip: (id) => get().tipsEnabled && get().tips[id] == null,
  shouldIntroduceView: (view) => get().tipsEnabled && get().viewIntros[view] == null,
  reset: () => {
    const next = { ...DEFAULT_PROGRESS, tips: {}, viewIntros: {} }
    persist(next)
    set(next)
  }
}))

/** Compatibility for callers that only need the old yes/no tour question. */
export function hasSeenOnboardingTour(): boolean {
  return useOnboardingStore.getState().tour !== 'unseen'
}
