

export const SETTINGS_CATEGORIES = ['general', 'writing', 'project', 'system'] as const

export type SettingsCategory = (typeof SETTINGS_CATEGORIES)[number]

export const SETTINGS_SECTION_KEYS = [
  'project',
  'appearance',
  'accessibility',
  'hotkeys',
  'editor',
  'writingAssistance',
  'writingGoals',
  'completion',
  'backups',
  'templates',
  'sceneStages',
  'sceneLabels',
  'themeTokens',
  'groups',
  'sceneTemplates',
  'tags',
  'manuscriptProperties',
  'updatesIntegrations',
  'languagePacks',
  'narration',
  'diagnostics',
  'extensions'
] as const

export type SettingsSectionKey = (typeof SETTINGS_SECTION_KEYS)[number]

export type SettingsScopeKind = 'global' | 'project' | 'overridable' | 'mixed'

export interface SettingsControlMetadata {
  /** Stable route segment. It deliberately does not depend on translated text. */
  key: string
  labelKey: string
  descriptionKeys?: readonly string[]
  keywords?: readonly string[]
  /** Prefer a real form control when it has a stable DOM id. */
  targetId?: string
}

export interface SettingsSectionMetadata {
  key: SettingsSectionKey
  category: SettingsCategory
  titleKey: string
  scope: SettingsScopeKind
  requiresProject?: boolean
  desktopOnly?: boolean
  /**
   * The opposite of desktopOnly: shown only in the iOS build. Used for what the
   * desktop reaches through chrome a phone does not have - a menu bar, a
   * command palette, a window title - and which therefore has to live in
   * Settings there.
   */
  mobileOnly?: boolean
  standalone?: boolean
  keywords?: readonly string[]
  controls?: readonly SettingsControlMetadata[]
}

export const control = (
  key: string,
  labelKey: string,
  targetId?: string,
  descriptionKeys?: readonly string[],
  keywords?: readonly string[]
): SettingsControlMetadata => ({ key, labelKey, targetId, descriptionKeys, keywords })
