import { BackupsCard } from './BackupsCard'
import { CompletionCard } from './CompletionCard'
import { ExtensionsCard } from './ExtensionsCard'
import { GroupsCard } from './GroupsCard'
import { HotkeysCard } from './HotkeysCard'
import { LanguagePacksCard } from './LanguagePacksCard'
import { ManuscriptPropertiesCard } from './ManuscriptPropertiesCard'
import { NarrationCard } from './NarrationCard'
import { SceneLabelsCard } from './SceneLabelsCard'
import { SceneStagesCard } from './SceneStagesCard'
import { SceneTemplatesCard } from './SceneTemplatesCard'
import { SectionBodyDef } from './settingsViewTypes'
import { TagsCard } from './TagsCard'
import { TemplatesCard } from './TemplatesCard'
import { ThemeTokensCard } from './ThemeTokensCard'

import { accessibilitySettingsBody } from './accessibilitySettingsBody'
import { appearanceSettingsBody } from './appearanceSettingsBody'
import { diagnosticsSettingsBody } from './diagnosticsSettingsBody'
import { editorSettingsBody } from './editorSettingsBody'
import { projectSettingsBody } from './projectSettingsBody'
import { type SettingsBodyContext } from './settingsBodyContext'
import { updatesIntegrationsSettingsBody } from './updatesIntegrationsSettingsBody'
import { writingAssistanceSettingsBody } from './writingAssistanceSettingsBody'
import { writingGoalsSettingsBody } from './writingGoalsSettingsBody'
const sectionFactories = [
  {
    /* Mobile only (see the registry): the way out of a project, which on
       desktop is a menu item and a palette command instead. */
    key: 'project',
    body: (context: SettingsBodyContext) => projectSettingsBody(context)
  },
  {
    key: 'appearance',
    body: (context: SettingsBodyContext) => appearanceSettingsBody(context)
  },
  {
    key: 'editor',
    body: (context: SettingsBodyContext) => editorSettingsBody(context)
  },
  {
    key: 'accessibility',
    body: (context: SettingsBodyContext) => accessibilitySettingsBody(context)
  },
  {
    key: 'writingGoals',
    body: (context: SettingsBodyContext) => writingGoalsSettingsBody(context)
  },
  {
    key: 'writingAssistance',
    body: (context: SettingsBodyContext) => writingAssistanceSettingsBody(context)
  },
  {
    key: 'templates',
    body: () => <TemplatesCard />,
    standalone: true
  },
  {
    key: 'hotkeys',
    body: () => <HotkeysCard />,
    standalone: true
  },
  {
    key: 'updatesIntegrations',
    body: (context: SettingsBodyContext) => updatesIntegrationsSettingsBody(context)
  },
  {
    key: 'backups',
    body: () => <BackupsCard />
  },
  {
    key: 'sceneStages',
    body: () => <SceneStagesCard />
  },
  {
    key: 'sceneLabels',
    body: () => <SceneLabelsCard />
  },
  {
    key: 'themeTokens',
    body: () => <ThemeTokensCard />
  },
  {
    key: 'completion',
    body: () => <CompletionCard />
  },
  {
    key: 'groups',
    body: () => <GroupsCard />
  },
  {
    key: 'sceneTemplates',
    body: () => <SceneTemplatesCard />
  },
  {
    key: 'tags',
    body: () => <TagsCard />
  },
  {
    key: 'manuscriptProperties',
    body: () => <ManuscriptPropertiesCard />
  },
  {
    key: 'languagePacks',
    body: () => <LanguagePacksCard />
  },
  {
    key: 'diagnostics',
    body: (context: SettingsBodyContext) => diagnosticsSettingsBody(context)
  },
  {
    key: 'narration',
    body: () => <NarrationCard />
  },
  {
    key: 'extensions',
    body: () => <ExtensionsCard />,
    standalone: true
  }
] satisfies Array<Omit<SectionBodyDef, 'body'> & { body(context: SettingsBodyContext): React.ReactNode }>

export function settingsSectionBodies(context: SettingsBodyContext): SectionBodyDef[] {
  return sectionFactories.map(({ body, ...metadata }) => ({ ...metadata, body: body(context) }))
}
