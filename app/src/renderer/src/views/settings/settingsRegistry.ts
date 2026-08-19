export const SETTINGS_CATEGORIES = ['general', 'writing', 'project', 'system'] as const

export type SettingsCategory = (typeof SETTINGS_CATEGORIES)[number]

export const SETTINGS_SECTION_KEYS = [
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
  standalone?: boolean
  keywords?: readonly string[]
  controls?: readonly SettingsControlMetadata[]
}

const control = (
  key: string,
  labelKey: string,
  targetId?: string,
  descriptionKeys?: readonly string[],
  keywords?: readonly string[]
): SettingsControlMetadata => ({ key, labelKey, targetId, descriptionKeys, keywords })

/**
 * The Settings information architecture and search index.
 *
 * Bodies stay in SettingsView because they close over the live settings value
 * and its update functions. Everything that decides where a section lives,
 * whether it is available, what scope it edits, and how a caller links to it
 * has one source of truth here.
 */
export const SETTINGS_REGISTRY: readonly SettingsSectionMetadata[] = [
  {
    key: 'appearance',
    category: 'general',
    titleKey: 'settings.appearance',
    scope: 'overridable',
    keywords: ['appearance', 'language', 'theme', 'accent', 'color', 'colour', 'interface'],
    controls: [
      control('language', 'settings.uiLanguage', 'set-language', ['settings.uiLanguageDesc']),
      control('theme', 'settings.theme', 'set-theme', ['settings.themeDescription']),
      control('interface-scale', 'settings.uiScale', 'set-ui-scale', ['settings.uiScaleDesc'], [
        'zoom',
        'dpi',
        'display size'
      ]),
      control('accent-color', 'settings.accentColor', 'set-accent', ['settings.accentColorDesc'])
    ]
  },
  {
    key: 'accessibility',
    category: 'general',
    titleKey: 'settings.accessibility',
    scope: 'mixed',
    keywords: ['accessibility', 'readability', 'dyslexia', 'contrast', 'spacing'],
    controls: [
      control(
        'contextual-tips',
        'settings.contextualTips',
        'set-contextual-tips',
        ['settings.contextualTipsDesc'],
        ['onboarding', 'guidance', 'coachmark', 'focus peek']
      ),
      control('accessible-font', 'settings.accessibleFont', 'set-a11y-font', [
        'settings.accessibleFontHint'
      ]),
      control('font-size', 'settings.fontSize', 'set-a11y-size', ['settings.fontSizeDesc']),
      control('line-height', 'settings.lineHeight', 'set-a11y-spacing', [
        'settings.lineHeightDesc'
      ]),
      control('high-contrast', 'settings.useHighContrast', 'set-high-contrast', [
        'settings.highContrastHint'
      ])
    ]
  },
  {
    key: 'hotkeys',
    category: 'general',
    titleKey: 'settings.hotkeys',
    scope: 'global',
    desktopOnly: true,
    standalone: true,
    keywords: ['hotkey', 'keyboard', 'shortcut', 'key', 'binding', 'gesture'],
    controls: [
      control('filter', 'hotkeys.searchPlaceholder', undefined, undefined, ['find shortcut']),
      control('reset-all', 'hotkeys.resetAll', undefined, undefined, ['defaults'])
    ]
  },
  {
    key: 'editor',
    category: 'writing',
    titleKey: 'settings.editor',
    scope: 'overridable',
    keywords: ['editor', 'font', 'book', 'width', 'page', 'paragraph', 'spacing', 'speech'],
    controls: [
      control('font-family', 'settings.fontFamily', 'set-font', ['settings.fontFamilyDesc']),
      control('font-size', 'settings.fontSize', 'set-fontsize', ['settings.fontSizeDesc']),
      control('line-height', 'settings.lineHeight', 'set-lineheight', ['settings.lineHeightDesc']),
      control('letter-spacing', 'settings.letterSpacing', 'set-letterspacing', [
        'settings.letterSpacingDesc'
      ]),
      control('paragraph-spacing', 'settings.paragraphSpacing', 'set-paraspacing', [
        'settings.paragraphSpacingDesc'
      ]),
      control('read-aloud-speed', 'settings.readAloudRate', 'set-readaloud-rate'),
      control('read-aloud-voice', 'settings.readAloudVoice', 'set-readaloud-voice', [
        'settings.readAloudDesc',
        'settings.readAloudVoiceKinds'
      ]),
      control('compose-dimming', 'settings.composeDimming', 'set-compose-dimming', [
        'settings.composeDimmingDesc'
      ]),
      control('typewriter-scrolling', 'settings.typewriterScroll', 'set-typewriter-scroll', [
        'settings.typewriterScrollDesc'
      ]),
      control('page-view', 'settings.pageView', 'set-page-view', ['settings.pageViewDesc']),
      control('book-paragraph-spacing', 'settings.bookSpacing', 'set-book-spacing', [
        'settings.bookSpacingDesc'
      ]),
      control('book-page-width', 'settings.bookWidth', 'set-book-width', [
        'settings.bookWidthDesc'
      ]),
      control('page-format', 'settings.bookWidthPageFormat', 'set-pageformat'),
      control('text-block-width', 'settings.bookWidthCustom', 'set-customwidth', [
        'settings.bookWidthCustomDesc'
      ]),
      control('book-font', 'settings.bookWidthFont', 'set-bookfont'),
      control('book-font-size', 'settings.bookWidthFontSize', 'set-bookfontsize')
    ]
  },
  {
    key: 'writingAssistance',
    category: 'writing',
    titleKey: 'settings.writingAssistance',
    scope: 'overridable',
    keywords: ['writing', 'replacement', 'quote', 'dialogue', 'grammar', 'spelling'],
    controls: [
      control('automatic-replacements', 'settings.autoReplacement', 'set-auto-replacement', [
        'settings.autoReplacementDesc'
      ]),
      control('quote-style', 'settings.quoteStyle', 'set-quotes', ['settings.quoteStyleDesc']),
      control('reviewer-name', 'settings.reviewerName', 'set-reviewer', [
        'settings.reviewerNameHint'
      ]),
      control('dialogue-correction', 'settings.dialogueCorrection', 'set-dialogue-correction', [
        'settings.dialogueCorrectionDesc'
      ]),
      control('spell-check', 'settings.spellCheck', 'set-spell-check', [
        'settings.spellCheckHint'
      ]),
      control('grammar-check', 'settings.grammarCheck', 'set-grammar-check', [
        'settings.grammarCheckDesc'
      ]),
      control('grammar-server', 'settings.grammarCheckApiUrl', 'set-gc-url', [
        'settings.grammarCheckApiUrlDesc'
      ]),
      control('grammar-account', 'settings.grammarCheckUsername', 'set-gc-user', [
        'settings.grammarCheckUsernameDesc'
      ]),
      control('grammar-api-key', 'settings.grammarCheckApiKey', 'set-gc-key', [
        'settings.grammarCheckApiKeyDesc'
      ]),
      control('advanced-style-check', 'settings.grammarCheckPickyMode', 'set-gc-picky', [
        'settings.grammarCheckPickyModeDesc'
      ]),
      control('native-language', 'settings.grammarCheckMotherTongue', 'set-gc-mother', [
        'settings.grammarCheckMotherTongueDesc'
      ])
    ]
  },
  {
    key: 'writingGoals',
    category: 'project',
    titleKey: 'settings.writingGoals',
    scope: 'project',
    requiresProject: true,
    keywords: ['goal', 'deadline', 'author', 'target', 'words', 'chapter', 'scene', 'act'],
    controls: [
      control('deadline', 'settings.projectDeadline', 'set-deadline', [
        'settings.projectDeadlineDesc'
      ]),
      control('author', 'settings.projectAuthor', 'set-author', ['settings.projectAuthorDesc']),
      control('daily-word-goal', 'settings.dailyWordGoal', 'set-daily-goal', [
        'settings.dailyWordGoalDesc'
      ]),
      control('weekly-word-goal', 'settings.weeklyWordGoal', 'set-weekly-goal', [
        'settings.weeklyWordGoalDesc'
      ]),
      control('monthly-word-goal', 'settings.monthlyWordGoal', 'set-monthly-goal', [
        'settings.monthlyWordGoalDesc'
      ]),
      control('words-per-page', 'settings.wordsPerPage', 'set-words-per-page', [
        'settings.wordsPerPageDesc'
      ]),
      control('project-word-goal', 'settings.projectWordGoal', 'set-project-goal', [
        'settings.projectWordGoalDesc'
      ])
    ]
  },
  {
    key: 'completion',
    category: 'project',
    titleKey: 'completion.title',
    scope: 'project',
    requiresProject: true,
    keywords: ['completion', 'autocomplete', 'words', 'phrases', 'vocabulary', 'typing'],
    controls: [
      control('words', 'completion.words'),
      control('trigger-length', 'completion.trigger', 'set-completion-trigger', [
        'completion.triggerHint'
      ])
    ]
  },
  {
    key: 'backups',
    category: 'system',
    titleKey: 'backup.title',
    scope: 'mixed',
    keywords: ['backup', 'archive', 'restore', 'zip', 'recovery', 'safety'],
    controls: [
      control('enabled', 'backup.enabled', undefined, ['backup.enabledDesc']),
      control('folder', 'backup.folder', 'set-backup-folder', ['backup.folderDesc']),
      control('interval', 'backup.interval', 'set-backup-interval', ['backup.intervalDesc']),
      control('retention', 'backup.retention', 'set-backup-retention', ['backup.retentionDesc']),
      control('milestone', 'backup.milestone', 'set-backup-milestone', [
        'backup.milestoneDesc'
      ])
    ]
  },
  {
    key: 'templates',
    category: 'project',
    titleKey: 'settings.templates',
    scope: 'project',
    requiresProject: true,
    standalone: true,
    keywords: ['template', 'character', 'location', 'item', 'lore'],
    controls: [
      control('character-templates', 'settings.characterTemplates'),
      control('location-templates', 'settings.locationTemplates'),
      control('item-templates', 'settings.itemTemplates'),
      control('lore-templates', 'settings.loreTemplates')
    ]
  },
  {
    key: 'sceneStages',
    category: 'project',
    titleKey: 'stages.title',
    scope: 'project',
    requiresProject: true,
    keywords: ['stage', 'status', 'revision', 'draft', 'progress', 'scene'],
    controls: [
      control('add-stage', 'stages.add'),
      control('counts-as-written', 'stages.countsAsWritten', undefined, [
        'stages.countsAsWrittenHint'
      ])
    ]
  },
  {
    key: 'sceneLabels',
    category: 'project',
    titleKey: 'labels.title',
    scope: 'project',
    requiresProject: true,
    keywords: ['label', 'labels', 'colour', 'color', 'flag', 'scene', 'corkboard'],
    controls: [control('add-label', 'labels.add'), control('label-colour', 'labels.colour')]
  },
  {
    key: 'themeTokens',
    category: 'general',
    titleKey: 'themeTokens.title',
    scope: 'global',
    keywords: ['token', 'theme', 'colour', 'color', 'appearance', 'font', 'radius', 'spacing'],
    controls: [
      control('profile', 'themeTokens.profile'),
      control('reset-all', 'themeTokens.resetAll')
    ]
  },
  {
    key: 'groups',
    category: 'project',
    titleKey: 'groups.title',
    scope: 'project',
    requiresProject: true,
    keywords: ['group', 'faction', 'house', 'crew', 'family', 'colour', 'color'],
    controls: [control('add-group', 'groups.add'), control('harvest-groups', 'groups.harvest')]
  },
  {
    key: 'sceneTemplates',
    category: 'project',
    titleKey: 'sceneTemplates.title',
    scope: 'project',
    requiresProject: true,
    keywords: ['template', 'scene', 'preset', 'skeleton', 'start'],
    controls: [control('summary', 'sceneTemplates.summary')]
  },
  {
    key: 'tags',
    category: 'project',
    titleKey: 'tags.title',
    scope: 'project',
    requiresProject: true,
    keywords: ['tag', 'label', 'colour', 'color', 'merge', 'rename', 'vocabulary'],
    controls: [control('rename-tag', 'tags.rename'), control('tag-colour', 'tags.colour')]
  },
  {
    key: 'manuscriptProperties',
    category: 'project',
    titleKey: 'props.title',
    scope: 'project',
    requiresProject: true,
    keywords: ['property', 'field', 'custom', 'metadata', 'column', 'scene', 'chapter'],
    controls: [control('add-property', 'props.add'), control('show-in-outliner', 'props.showInOutliner')]
  },
  {
    key: 'updatesIntegrations',
    category: 'system',
    titleKey: 'settings.updatesIntegrations',
    scope: 'global',
    desktopOnly: true,
    keywords: ['update', 'extension', 'github', 'token', 'integration'],
    controls: [
      control('application-updates', 'update.checkForUpdates', 'set-check-updates', [
        'update.checkForUpdatesDesc'
      ]),
      control('extension-updates', 'settings.checkForExtensionUpdates', 'set-extension-updates', [
        'settings.checkForExtensionUpdatesDesc'
      ]),
      control('github-token', 'settings.githubToken', 'set-github-token', [
        'settings.githubTokenDesc'
      ])
    ]
  },
  {
    key: 'languagePacks',
    category: 'system',
    titleKey: 'languagePacks.title',
    scope: 'global',
    keywords: ['language', 'locale', 'translation', 'lexicon', 'analysis', 'pack'],
    controls: [
      control('rescan', 'languagePacks.rescan'),
      control('open-languages', 'languagePacks.openLocales'),
      control('open-analysis', 'languagePacks.openAnalysis')
    ]
  },
  {
    key: 'diagnostics',
    category: 'system',
    titleKey: 'settings.diagnostics',
    scope: 'global',
    keywords: ['log', 'logging', 'diagnostic', 'support'],
    controls: [
      control(
        'display-information',
        'settings.displayInfoRefresh',
        'set-display-diagnostics',
        ['settings.displayInfoDesc'],
        ['dpi', 'scale', 'zoom', 'window size', 'monitor']
      ),
      control('diagnostic-logging', 'settings.diagnosticLogging', 'set-diagnostic-logging', [
        'settings.diagnosticLoggingDesc'
      ]),
      control('open-log-folder', 'settings.openLogFolder'),
      control('clear-logs', 'settings.clearLogs')
    ]
  },
  {
    key: 'narration',
    category: 'system',
    titleKey: 'settings.narration',
    scope: 'global',
    // The engines are downloads onto this machine, and an installed model is
    // not a thing a project carries.
    desktopOnly: true,
    keywords: [
      'narration',
      'speech',
      'voice',
      'engine',
      'tts',
      'audiobook',
      'read aloud',
      'prepare',
      'download'
    ],
    controls: [
      control('engines', 'settings.narrationEngines', undefined, ['settings.narrationDesc'])
    ]
  },
  {
    key: 'extensions',
    category: 'system',
    titleKey: 'extensions.title',
    scope: 'global',
    desktopOnly: true,
    standalone: true,
    keywords: ['extension', 'plugin', 'addon']
  }
] as const

const sectionKeys = new Set<string>(SETTINGS_SECTION_KEYS)

export function isSettingsSectionKey(value: string): value is SettingsSectionKey {
  return sectionKeys.has(value)
}

export function settingsSectionsForContext(context: {
  hasProject: boolean
  isMobile: boolean
}): readonly SettingsSectionMetadata[] {
  return SETTINGS_REGISTRY.filter(
    (section) =>
      (!section.requiresProject || context.hasProject) &&
      (!section.desktopOnly || !context.isMobile)
  )
}

function normalized(value: string): string {
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase()
    .trim()
}

function translatedText(
  keys: readonly string[] | undefined,
  translate: (key: string) => string
): string[] {
  return keys?.map((key) => translate(key)) ?? []
}

export interface SettingsSearchResult {
  section: SettingsSectionMetadata
  control?: SettingsControlMetadata
}

/** Searches both translated visible copy and language-neutral fallback terms. */
export function searchSettings(
  sections: readonly SettingsSectionMetadata[],
  query: string,
  translate: (key: string) => string
): SettingsSearchResult[] {
  const needle = normalized(query)
  if (!needle) return []

  const results: SettingsSearchResult[] = []
  for (const section of sections) {
    const sectionText = [translate(section.titleKey), ...(section.keywords ?? [])]
      .map(normalized)
      .join(' ')
    if (sectionText.includes(needle)) results.push({ section })

    for (const item of section.controls ?? []) {
      const controlText = [
        translate(item.labelKey),
        ...translatedText(item.descriptionKeys, translate),
        ...(item.keywords ?? [])
      ]
        .map(normalized)
        .join(' ')
      if (controlText.includes(needle)) results.push({ section, control: item })
    }
  }
  return results
}

export function settingsSection(key: SettingsSectionKey): SettingsSectionMetadata {
  return SETTINGS_REGISTRY.find((section) => section.key === key)!
}

export function settingsControl(
  sectionKey: SettingsSectionKey,
  controlKey: string
): SettingsControlMetadata | undefined {
  return settingsSection(sectionKey).controls?.find((item) => item.key === controlKey)
}
