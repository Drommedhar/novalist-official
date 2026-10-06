import { useCodexStore } from '../../stores/codexStore'

export interface OverrideImage {
  name: string
  path: string
  /** Project-root-relative display URL resolved by the backend. */
  url?: string
}

export interface OverrideRelationship {
  role: string
  target: string
  inverseRole?: string
}

export interface OverrideSection {
  title: string
  content: string
}

export interface CharacterOverride {
  act: string | null
  chapter: string
  scene: string | null
  customProperties?: Record<string, string> | null
  /** null = inherit the base list; an array (possibly empty) replaces it. */
  images?: OverrideImage[] | null
  relationships?: OverrideRelationship[] | null
  sections?: OverrideSection[] | null
  [field: string]: unknown
}

interface OverrideField {
  key: string
  labelKey: string
  multiline?: boolean
}

interface OverrideGroup {
  titleKey: string
  fields: OverrideField[]
}

/** The full character field set that a chapter/scene override can restate,
 * grouped like the base editor. Diff-stored: a blank input inherits the base. */
export const OVERRIDE_GROUPS: OverrideGroup[] = [
  {
    titleKey: 'entityEditor.basicInfo',
    fields: [
      { key: 'name', labelKey: 'entityEditor.name' },
      { key: 'surname', labelKey: 'entityEditor.surname' },
      { key: 'role', labelKey: 'entityEditor.rolePlaceholder' },
      { key: 'gender', labelKey: 'entityEditor.gender' },
      { key: 'age', labelKey: 'entityEditor.age' }
    ]
  },
  {
    titleKey: 'entityEditor.physicalAttributes',
    fields: [
      { key: 'eyeColor', labelKey: 'entityEditor.eyeColor' },
      { key: 'hairColor', labelKey: 'entityEditor.hairColor' },
      { key: 'hairLength', labelKey: 'entityEditor.hairLength' },
      { key: 'height', labelKey: 'entityEditor.height' },
      { key: 'build', labelKey: 'entityEditor.build' },
      { key: 'skinTone', labelKey: 'entityEditor.skinTone' },
      { key: 'distinguishingFeatures', labelKey: 'entityEditor.distinguishingFeatures', multiline: true }
    ]
  }
]

export const OVERRIDABLE = OVERRIDE_GROUPS.flatMap((g) => g.fields)

export type Scope = { chapter: string; scene: string | null }

export const scopeKey = (chapter: string, scene: string | null): string => `${chapter}|${scene ?? ''}`

export const matchesScope = (over: CharacterOverride, scope: Scope): boolean =>
  over.chapter === scope.chapter && (over.scene ?? null) === scope.scene

export const apply = (updated: Record<string, unknown>): void => {
  const current = useCodexStore.getState()
  if (current.entityType === 'character' && current.selectedId === updated.id) {
    useCodexStore.setState({ selectedRecord: updated })
  }
}
