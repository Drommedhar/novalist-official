import {
  type SettingsSectionKey,
  type SettingsSectionMetadata
} from './settingsRegistry'



export type Scope = 'global' | 'project'



export interface SectionBodyDef {
  key: SettingsSectionKey
  body: React.ReactNode
  /** True when the body is a self-contained card (its own title + styling). */
  standalone?: boolean
}



export type ResolvedSection = SettingsSectionMetadata & SectionBodyDef



export interface DisplayDiagnostics {
  zoomFactor: number
  scaleFactor: number
  windowBounds: { width: number; height: number }
  contentBounds: { width: number; height: number }
  workArea: { width: number; height: number }
}
