import { create } from 'zustand'
import i18next from '../i18n'
import { rpc } from '../rpc/client'
import { applyCustomGestures } from '../shell/hotkeys'
import { applyThemeTokens } from '../views/settings/ThemeTokensCard'
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
  editorFirstLineIndent: number
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
  /** Whether anything is substituted while typing. Off leaves the writer's own
   * quotes, hyphens and dots exactly as typed. */
  autoReplacementEnabled: boolean
  /** The name put on a suggested edit. */
  reviewerName: string
  dialogueCorrectionEnabled: boolean
  grammarCheckEnabled: boolean
  grammarCheckProvider: 'languagetool' | 'harper'
  /** Resolved from the writing language and selected spelling dictionaries. */
  grammarCheckLanguage: string
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
  /** Words for the calendar week; 0 means no weekly horizon. */
  weeklyGoal: number
  /** Words for the calendar month; 0 means no monthly horizon. */
  monthlyGoal: number
  /** Words to a printed page, for the page estimate. */
  wordsPerPage: number
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

let loadRevision = 0
let appearanceRevision = 0

async function applySideEffects(view: SettingsView): Promise<void> {
  const revision = ++appearanceRevision
  if (i18next.language !== view.effective.language) {
    await i18next.changeLanguage(view.effective.language)
  }
  if (revision !== appearanceRevision) return
  // Built-in, folder, and extension themes all resolve through the catalog; an
  // unknown name falls back to the default palette.
  applyTheme(view.effective.theme, view.effective.accentColor)
  // After the theme, because the overrides sit on top of whatever it set.
  await rpc
    .request<Record<string, string>>('appearance/tokens')
    .then((tokens) => tokens && revision === appearanceRevision && applyThemeTokens(tokens))
    .catch(() => {})
  if (revision !== appearanceRevision) return
  applyCustomGestures((view.global.hotkeyBindings as Record<string, string>) ?? {})
}

export const useSettingsStore = create<SettingsState>((set) => {
  // Reads and edits share one revision: no response from the project being
  // left may overwrite a newer global settings response.
  const applyResponse = async (response: Promise<SettingsView>): Promise<void> => {
    const revision = ++loadRevision
    const view = await response
    if (revision !== loadRevision) return
    await applySideEffects(view)
    if (revision === loadRevision) set({ view })
  }

  return {
    view: null,
    load: () => applyResponse(rpc.request<SettingsView>('settings/get')),

    update: (scope, patch) => {
      const method = scope === 'project' ? 'settings/updateProject' : 'settings/updateGlobal'
      return applyResponse(rpc.request<SettingsView>(method, [patch]))
    },

    pinSection: (section) => applyResponse(rpc.request<SettingsView>('settings/pinSection', [section])),
    clearSection: (section) => applyResponse(rpc.request<SettingsView>('settings/clearSection', [section])),
    updateProjectMeta: (patch) => applyResponse(rpc.request<SettingsView>('settings/updateProjectMeta', [patch])),
    setHotkeyBinding: (actionId, gesture) => applyResponse(rpc.request<SettingsView>('settings/setHotkeyBinding', [actionId, gesture])),
    resetHotkeyBinding: (actionId) => applyResponse(rpc.request<SettingsView>('settings/resetHotkeyBinding', [actionId])),
    resetAllHotkeys: () => applyResponse(rpc.request<SettingsView>('settings/resetAllHotkeys'))
  }
})
