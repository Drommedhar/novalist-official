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

function applySideEffects(view: SettingsView): void {
  if (i18next.language !== view.effective.language) {
    void i18next.changeLanguage(view.effective.language)
  }
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
