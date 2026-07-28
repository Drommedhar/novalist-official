import { create } from 'zustand'
import i18next from '../i18n'
import { rpc } from '../rpc/client'
import { applyCustomGestures } from '../shell/hotkeys'

export interface EffectiveSettings {
  language: string
  theme: string
  accentColor: string | null
  editorFontFamily: string
  editorFontSize: number
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

export interface SettingsView {
  hasProject: boolean
  global: Record<string, unknown>
  overrides: Record<string, unknown> | null
  effective: EffectiveSettings
  project: ProjectMeta | null
}

interface SettingsState {
  view: SettingsView | null
  load(): Promise<void>
  update(scope: 'global' | 'project', patch: Record<string, unknown>): Promise<void>
  clearSection(section: 'appearance' | 'editor' | 'writing'): Promise<void>
  updateProjectMeta(patch: Record<string, unknown>): Promise<void>
  setHotkeyBinding(actionId: string, gesture: string): Promise<void>
  resetHotkeyBinding(actionId: string): Promise<void>
  resetAllHotkeys(): Promise<void>
}

const THEME_SLUGS: Record<string, string> = {
  Discord: 'discord',
  'Catppuccin Mocha': 'catppuccin-mocha'
}

/** Applies the selected theme: named themes pin their palette; Default resolves
 * to dark (the light theme was removed). Accent overrides the token directly. */
export function applyThemeTokens(theme: string, accentColor: string | null): void {
  const root = document.documentElement
  const slug = THEME_SLUGS[theme]
  root.dataset.theme = slug ?? 'dark'
  if (accentColor) {
    root.style.setProperty('--nl-accent', accentColor)
    root.style.setProperty('--nl-accent-hover', accentColor)
  } else {
    root.style.removeProperty('--nl-accent')
    root.style.removeProperty('--nl-accent-hover')
  }
  syncTitleBarColors()
}

/**
 * Repaints the system-drawn window controls to match the theme. On Windows and
 * Linux the title bar is hidden and the app's own toolbar stands in for it, so
 * the minimise/maximise/close buttons have to be told the toolbar's colours or
 * they stay light grey against a dark strip. Reads the tokens back off the
 * document so a theme or custom accent needs no separate colour table.
 */
function syncTitleBarColors(): void {
  if (typeof window.novalist?.setTitleBarColors !== 'function') return
  const style = getComputedStyle(document.documentElement)
  const color = style.getPropertyValue('--nl-surface-toolbar').trim()
  const symbol = style.getPropertyValue('--nl-text').trim()
  // The overlay API takes only opaque colours; under the macOS material layer
  // the toolbar token resolves to a translucent value, and that path has native
  // traffic lights anyway, so there is nothing to repaint.
  if (!color.startsWith('#') || !symbol.startsWith('#')) return
  window.novalist.setTitleBarColors(color, symbol)
}

function applySideEffects(view: SettingsView): void {
  if (i18next.language !== view.effective.language) {
    void i18next.changeLanguage(view.effective.language)
  }
  applyThemeTokens(view.effective.theme, view.effective.accentColor)
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
