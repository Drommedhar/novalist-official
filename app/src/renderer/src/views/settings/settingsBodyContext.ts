import { type TFunction } from 'i18next'
import { type ReactNode } from 'react'
import { type SettingsSection, type SettingsView } from '../../stores/settingsStore'
import { type CatalogTheme } from '../../stores/themeCatalog'
import { assetDirectories } from '../../stores/userAssets'
import { type DisplayDiagnostics, type Scope } from './settingsViewTypes'
export interface SettingsBodyContext {
  t: TFunction
  view: SettingsView
  eff: SettingsView['effective']
  project: SettingsView['project']
  projectName: string | null
  closeProjectFromSettings(): Promise<void>
  scopeToggle(section: SettingsSection): ReactNode
  scopeFor(section: SettingsSection): Scope
  update(scope: Scope, patch: Record<string, unknown>): Promise<void>
  updateProjectMeta(patch: Record<string, unknown>): Promise<void>
  themes: CatalogTheme[]
  uiScale: number
  setUiScale(percent: number): void
  resetUiScale(): void
  assetDirs: ReturnType<typeof assetDirectories>
  fontDatalist: ReactNode
  systemVoices: { id: string; name: string; language: string }[]
  voices: SpeechSynthesisVoice[]
  writingLanguage: string
  writingLanguages: string[]
  isMobile: boolean
  tipsEnabled: boolean
  setTipsEnabled(enabled: boolean): void
  displayInfoBusy: boolean
  refreshDisplayInfo(): Promise<void>
  displayInfo: DisplayDiagnostics | null
}
