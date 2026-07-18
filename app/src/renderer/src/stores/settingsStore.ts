import { create } from 'zustand'
import i18next from '../i18n'
import { rpc } from '../rpc/client'

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

export interface SettingsView {
  hasProject: boolean
  global: Record<string, unknown>
  overrides: Record<string, unknown> | null
  effective: EffectiveSettings
}

interface SettingsState {
  view: SettingsView | null
  load(): Promise<void>
  update(scope: 'global' | 'project', patch: Record<string, unknown>): Promise<void>
  clearSection(section: 'appearance' | 'editor' | 'writing'): Promise<void>
}

const THEME_SLUGS: Record<string, string> = {
  Discord: 'discord',
  'Catppuccin Mocha': 'catppuccin-mocha'
}

/** Applies the selected theme: named themes pin their palette; Default/system
 * follows the OS light/dark preference. Accent overrides the token directly. */
export function applyThemeTokens(theme: string, accentColor: string | null): void {
  const root = document.documentElement
  const slug = THEME_SLUGS[theme]
  if (slug) {
    root.dataset.theme = slug
  } else {
    root.dataset.theme = window.matchMedia('(prefers-color-scheme: light)').matches
      ? 'light'
      : 'dark'
  }
  if (accentColor) {
    root.style.setProperty('--nl-accent', accentColor)
    root.style.setProperty('--nl-accent-hover', accentColor)
  } else {
    root.style.removeProperty('--nl-accent')
    root.style.removeProperty('--nl-accent-hover')
  }
}

function applySideEffects(view: SettingsView): void {
  if (i18next.language !== view.effective.language) {
    void i18next.changeLanguage(view.effective.language)
  }
  applyThemeTokens(view.effective.theme, view.effective.accentColor)
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
  }
}))
