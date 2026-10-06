import { type TFunction } from 'i18next'
import { type Dispatch, type RefObject, type SetStateAction } from 'react'
import { useShellStore } from '../../stores/shellStore'
import { useSettingsNavigation } from './settingsNavigation'
import { searchSettings, type SettingsSectionKey, type SettingsSectionMetadata } from './settingsRegistry'
import { type ResolvedSection } from './settingsViewTypes'
export interface SettingsLayoutProps {
  t: TFunction; destination: ReturnType<typeof useSettingsNavigation.getState>['destination']; returnToOrigin(): void
  search: string; setSearch: Dispatch<SetStateAction<string>>; query: string
  visibleSections: ResolvedSection[]; activeSection: ResolvedSection | undefined
  goTo(section: SettingsSectionKey, control?: string): void
  sectionsRef: RefObject<HTMLDivElement | null>; sectionSurfaceRef: RefObject<HTMLDivElement | null>
  searchResults: ReturnType<typeof searchSettings>; overrideActive(section: SettingsSectionMetadata): boolean
  projectLoaded: boolean; projectName: string | null
  setMainView: ReturnType<typeof useShellStore.getState>['setMainView']
  closeProjectFromSettings(): Promise<void>
}
