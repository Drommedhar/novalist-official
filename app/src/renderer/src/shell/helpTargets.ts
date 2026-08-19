import type { InspectorTab, MainView } from '../stores/shellStore'
import type { SettingsSectionKey } from '../views/settings/settingsRegistry'

/** A stable address inside the bundled Markdown manual. */
export interface ManualTarget {
  file: string
  /** GitHub-style heading slug, without the leading '#'. */
  anchor?: string
  /** Optional search seed when the page has no narrower documented section. */
  query?: string
}

/**
 * The default help page for every routed view.
 *
 * Keeping this exhaustive means a new MainView fails typecheck until its help
 * destination is chosen, instead of silently opening the manual's front page.
 */
export const VIEW_HELP_TARGETS: Record<MainView, ManualTarget> = {
  write: { file: '05-editor.md' },
  dashboard: { file: '11-dashboard.md' },
  manuscript: { file: '10-manuscript.md' },
  drafts: { file: '45-drafts.md' },
  timeline: { file: '12-timeline.md' },
  plotGrid: { file: '08-plot-grid.md' },
  calendar: { file: '13-calendar.md' },
  relationships: { file: '14-relationships.md' },
  dialogue: { file: '33-dialogue.md' },
  narration: { file: '46-narration.md' },
  style: { file: '36-style-report.md' },
  canvas: { file: '37-planning-board.md' },
  codex: { file: '06-codex.md' },
  wiki: { file: '30-wiki.md' },
  maps: { file: '29-maps.md' },
  languages: { file: '41-languages.md' },
  series: { file: '40-series.md' },
  research: { file: '15-research.md' },
  gallery: { file: '19-image-gallery.md' },
  expose: { file: '32-expose.md' },
  export: { file: '20-export.md' },
  git: { file: '18-git.md' },
  extensions: { file: '24-extensions.md' },
  settings: { file: '23-settings.md' },
  about: { file: '02-interface-overview.md', anchor: 'about' }
}

export const INSPECTOR_HELP_TARGETS: Record<InspectorTab, ManualTarget> = {
  context: { file: '22-context-sidebar.md', anchor: 'scene-context-and-analysis' },
  footnotes: {
    file: '22-context-sidebar.md',
    anchor: 'footnotes-and-comments-footnotes-tab'
  },
  inbox: { file: '22-context-sidebar.md', anchor: 'inbox-tab' }
}

export type WritingHelpFeature =
  | 'autosave'
  | 'formatting'
  | 'paragraphStyles'
  | 'focusPeek'
  | 'focusMode'
  | 'readAloud'
  | 'suggestions'
  | 'snapshots'
  | 'findReplace'

export const WRITING_HELP_TARGETS: Record<WritingHelpFeature, ManualTarget> = {
  autosave: { file: '05-editor.md', anchor: 'auto-save' },
  formatting: { file: '05-editor.md', anchor: 'the-writing-bar' },
  paragraphStyles: { file: '05-editor.md', anchor: 'paragraph-styles' },
  focusPeek: { file: '05-editor.md', anchor: 'entity-hover-cards' },
  focusMode: { file: '05-editor.md', anchor: 'focus-mode' },
  readAloud: { file: '05-editor.md', anchor: 'read-aloud' },
  suggestions: {
    file: '05-editor.md',
    anchor: 'suggesting-edits-instead-of-making-them'
  },
  snapshots: { file: '17-snapshots.md', anchor: 'taking-a-manual-snapshot' },
  findReplace: { file: '21-find-replace.md', anchor: 'opening-find--replace' }
}

/**
 * Settings sections share one registry key with contextual settings navigation.
 * Sections with their own manual use it; the rest land on the nearest section
 * in the Settings page and seed search with the stable section name.
 */
export const SETTINGS_HELP_TARGETS: Record<SettingsSectionKey, ManualTarget> = {
  appearance: { file: '23-settings.md', anchor: 'appearance' },
  accessibility: { file: '39-accessibility.md', anchor: 'settings--accessibility' },
  hotkeys: { file: '26-hotkeys.md' },
  editor: { file: '23-settings.md', anchor: 'editor' },
  writingAssistance: { file: '23-settings.md', anchor: 'writing-assistance' },
  writingGoals: { file: '23-settings.md', anchor: 'writing-goals' },
  completion: { file: '23-settings.md', anchor: 'word-completion' },
  backups: { file: '35-backups.md' },
  templates: { file: '23-settings.md', anchor: 'templates' },
  sceneStages: { file: '23-settings.md', query: 'scene stages' },
  sceneLabels: { file: '23-settings.md', anchor: 'scene-labels' },
  themeTokens: { file: '23-settings.md', anchor: 'theme-tokens' },
  groups: { file: '23-settings.md', query: 'groups and factions' },
  sceneTemplates: { file: '07-templates.md', query: 'scene templates' },
  tags: { file: '23-settings.md', anchor: 'tags' },
  manuscriptProperties: { file: '23-settings.md', anchor: 'your-own-fields' },
  updatesIntegrations: { file: '23-settings.md', anchor: 'updates--integrations' },
  languagePacks: { file: '34-custom-themes-and-languages.md' },
  narration: { file: '46-narration.md', anchor: 'installing-a-speech-engine' },
  diagnostics: { file: '23-settings.md', anchor: 'diagnostics' },
  extensions: { file: '24-extensions.md', anchor: 'the-extensions-view' }
}

export interface HelpContext {
  view: MainView
  inspectorTab?: InspectorTab
  settingsSection?: SettingsSectionKey
  writingFeature?: WritingHelpFeature
}

export function helpTargetForContext(context: HelpContext): ManualTarget {
  if (context.writingFeature) return WRITING_HELP_TARGETS[context.writingFeature]
  if (context.view === 'settings' && context.settingsSection)
    return SETTINGS_HELP_TARGETS[context.settingsSection]
  if (
    context.inspectorTab &&
    (context.view === 'write' || context.view === 'manuscript')
  )
    return INSPECTOR_HELP_TARGETS[context.inspectorTab]
  return VIEW_HELP_TARGETS[context.view]
}
