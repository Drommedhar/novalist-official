import { create } from 'zustand'
import i18next from '../i18n'
import { rpc } from '../rpc/client'
import { applyCustomGestures } from '../shell/hotkeys'
import { applyTheme } from './themeCatalog'

export interface EffectiveSettings {
  language: string
  theme: string
  accentColor: string | null
  editorFontFamily: string
  editorFontSize: number
  editorLineHeight: number
  readabilityHighlighting: boolean
  readAloudRate: number
  readAloudVoiceUri: string | null
  editorLetterSpacing: number
  editorParagraphSpacing: number
  composeDimming: boolean
  typewriterScrollEnabled: boolean
  typewriterScrollAnchor: string
  pageViewEnabled: boolean
  enableBookParagraphSpacing: boolean
  enableBookWidth: boolean
  bookPageFormat: string
  bookTextBlockWidth: number | null
  bookFontFamily: string
  bookFontSize: number
  autoReplacementLanguage: string
  dialogueCorrectionEnabled: boolean
  grammarCheckEnabled: boolean
  spellCheckEnabled: boolean
  spellCheckLanguages: string[]
  grammarCheckApiUrl: string | null
  grammarCheckApiKey: string | null
  grammarCheckUsername: string | null
  grammarCheckPickyMode: boolean
  grammarCheckMotherTongue: string | null
}

/** Per-project metadata that lives outside the overridable settings model. */
export interface ProjectMeta {
  author: string
  watchFilesystem: boolean
  deadline: string | null
  dailyGoal: number
  projectGoal: number
}

/** The settings sections that carry a per-project override switch. */
export type SettingsSection = 'appearance' | 'editor' | 'writing'

export interface SettingsView {
  hasProject: boolean
  global: Record<string, unknown>
  overrides: Record<string, unknown> | null
  /** Which sections the open project has pinned; null with no project open.
   * The source of truth for each section's override switch. */
  overriddenSections: Record<SettingsSection, boolean> | null
  effective: EffectiveSettings
  project: ProjectMeta | null
}

interface SettingsState {
  view: SettingsView | null
  load(): Promise<void>
  update(scope: 'global' | 'project', patch: Record<string, unknown>): Promise<void>
  /** Pins a section to the open project, copying the values in effect now into
   * the project's overrides. What ticking the section's override switch does. */
  pinSection(section: SettingsSection): Promise<void>
  /** Drops a section's project overrides so it falls back to the global values.
   * What unticking the section's override switch does. */
  clearSection(section: SettingsSection): Promise<void>
  updateProjectMeta(patch: Record<string, unknown>): Promise<void>
  setHotkeyBinding(actionId: string, gesture: string): Promise<void>
  resetHotkeyBinding(actionId: string): Promise<void>
  resetAllHotkeys(): Promise<void>
}

function applySideEffects(view: SettingsView): void {
  if (i18next.language !== view.effective.language) {
    void i18next.changeLanguage(view.effective.language)
  }
  // Built-in, folder, and extension themes all resolve through the catalog; an
  // unknown name falls back to the default palette.
  applyTheme(view.effective.theme, view.effective.accentColor)
  applyCustomGestures((view.global.hotkeyBindings as Record<string, string>) ?? {})
}

export const useSettingsStore = create<SettingsState>((set) => ({
  view: null,

  load: async () => {
    const view = await rpc.request<SettingsView>('settings/get')
    applySideEffects(view)
    set({ view })
  },

  update: async (scope, patch) => {
    const method = scope === 'project' ? 'settings/updateProject' : 'settings/updateGlobal'
    const view = await rpc.request<SettingsView>(method, [patch])
    applySideEffects(view)
    set({ view })
  },

  pinSection: async (section) => {
    const view = await rpc.request<SettingsView>('settings/pinSection', [section])
    applySideEffects(view)
    set({ view })
  },

  clearSection: async (section) => {
    const view = await rpc.request<SettingsView>('settings/clearSection', [section])
    applySideEffects(view)
    set({ view })
  },

  updateProjectMeta: async (patch) => {
    const view = await rpc.request<SettingsView>('settings/updateProjectMeta', [patch])
    applySideEffects(view)
    set({ view })
  },

  setHotkeyBinding: async (actionId, gesture) => {
    const view = await rpc.request<SettingsView>('settings/setHotkeyBinding', [actionId, gesture])
    applySideEffects(view)
    set({ view })
  },

  resetHotkeyBinding: async (actionId) => {
    const view = await rpc.request<SettingsView>('settings/resetHotkeyBinding', [actionId])
    applySideEffects(view)
    set({ view })
  },

  resetAllHotkeys: async () => {
    const view = await rpc.request<SettingsView>('settings/resetAllHotkeys')
    applySideEffects(view)
    set({ view })
  }
}))
