import { create } from 'zustand'

export const UI_SCALE_STEPS = [75, 80, 90, 100, 110, 125, 150] as const
export const DEFAULT_UI_SCALE = 100
const STORAGE_KEY = 'nl.ui.scale'

export function normalizeUiScale(value: number): number {
  if (!Number.isFinite(value)) return DEFAULT_UI_SCALE
  return Math.max(UI_SCALE_STEPS[0], Math.min(UI_SCALE_STEPS.at(-1) ?? 150, Math.round(value)))
}

function readScale(): number {
  try {
    return normalizeUiScale(Number(localStorage.getItem(STORAGE_KEY) ?? DEFAULT_UI_SCALE))
  } catch {
    return DEFAULT_UI_SCALE
  }
}

function adjacentScale(current: number, direction: -1 | 1): number {
  const ordered = [...UI_SCALE_STEPS]
  if (direction > 0) return ordered.find((step) => step > current) ?? ordered.at(-1) ?? current
  return [...ordered].reverse().find((step) => step < current) ?? ordered[0]
}

interface UiScaleState {
  percent: number
  apply(): void
  setPercent(percent: number): void
  increase(): void
  decrease(): void
  reset(): void
}

export const useUiScaleStore = create<UiScaleState>((set, get) => ({
  percent: readScale(),
  apply: () => {
    void window.novalist.setUiScale?.(get().percent / 100)
  },
  setPercent: (value) => {
    const percent = normalizeUiScale(value)
    try {
      localStorage.setItem(STORAGE_KEY, String(percent))
    } catch {
      // A blocked store costs persistence, not the ability to resize this run.
    }
    set({ percent })
    void window.novalist.setUiScale?.(percent / 100)
  },
  increase: () => get().setPercent(adjacentScale(get().percent, 1)),
  decrease: () => get().setPercent(adjacentScale(get().percent, -1)),
  reset: () => get().setPercent(DEFAULT_UI_SCALE)
}))
