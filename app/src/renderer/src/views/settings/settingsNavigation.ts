import { create } from 'zustand'
import type { MainView } from '../../stores/shellStore'
import { isSettingsSectionKey, type SettingsSectionKey } from './settingsRegistry'

export interface SettingsOrigin {
  /** The pane view to restore when the contextual visit is finished. */
  view: MainView
  /** Localized by Settings, so callers do not freeze the current UI language. */
  labelKey: string
}

export interface SettingsDestination {
  section?: SettingsSectionKey
  control?: string
  query?: string
  origin?: SettingsOrigin
}

interface SettingsNavigationState {
  destination: SettingsDestination
  revision: number
  setDestination(destination: SettingsDestination): void
  clearOrigin(): void
}

export const useSettingsNavigation = create<SettingsNavigationState>((set) => ({
  destination: { section: 'appearance' },
  revision: 0,
  setDestination: (destination) =>
    set((state) => ({ destination, revision: state.revision + 1 })),
  clearOrigin: () =>
    set((state) => ({
      destination: { ...state.destination, origin: undefined },
      revision: state.revision + 1
    }))
}))

/** Settings-owned adapter used by shell navigation and contextual controls. */
export function setSettingsDestination(destination: SettingsDestination): void {
  useSettingsNavigation.getState().setDestination(destination)
}

/** Stable textual form for command links, tests, and future URL routing. */
export function formatSettingsDestination(destination: SettingsDestination): string {
  const segments = ['settings']
  if (destination.section) segments.push(encodeURIComponent(destination.section))
  if (destination.control) segments.push(encodeURIComponent(destination.control))
  return segments.join('/')
}

export function parseSettingsDestination(value: string): SettingsDestination | null {
  const route = value.trim().replace(/^#?\/?/, '')
  const [prefix, encodedSection, encodedControl] = route.split('/')
  if (prefix !== 'settings' || !encodedSection) return null

  try {
    const section = decodeURIComponent(encodedSection)
    if (!isSettingsSectionKey(section)) return null
    return {
      section,
      control: encodedControl ? decodeURIComponent(encodedControl) : undefined
    }
  } catch {
    return null
  }
}
