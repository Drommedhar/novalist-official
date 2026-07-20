import type { TFunction } from 'i18next'
import type { CustomTypeDefinition } from './CustomTypeManager'

export interface WizardChoice {
  value: string
  label: string
}

export interface WizardStepDef {
  id: string
  title: string
  help?: string
  skippable: boolean
  kind: 'text' | 'choice' | 'number' | 'date'
  multiline?: boolean
  choices?: WizardChoice[]
  defaultValue?: string
  /** Entity type whose entities populate the choices at dialog-open time. */
  entityRefType?: string
}

const LORE_CATEGORIES = ['Organization', 'Culture', 'History', 'Other']

function text(
  id: string,
  title: string,
  opts: { help?: string; multiline?: boolean; skippable?: boolean } = {}
): WizardStepDef {
  return {
    id,
    title,
    help: opts.help,
    skippable: opts.skippable ?? true,
    kind: 'text',
    multiline: opts.multiline
  }
}

/**
 * Guided-creation steps for an entity that already has its name (the name
 * step is stripped, mirroring the desktop flow after the creation dialog).
 * Ported from EntityGuidedWizard.BuildFor.
 */
export function buildGuidedSteps(
  entityType: string,
  customDef: CustomTypeDefinition | undefined,
  t: TFunction
): WizardStepDef[] {
  switch (entityType) {
    case 'character':
      return [
        text('surname', t('wizard.entity.field.surname'), {
          help: t('wizard.entity.character.surnameHelp')
        }),
        text('gender', t('wizard.entity.field.gender')),
        text('age', t('wizard.entity.field.age'), {
          help: t('wizard.entity.character.ageHelp')
        }),
        text('role', t('wizard.entity.field.role'), {
          help: t('wizard.entity.character.roleHelp')
        }),
        text('group', t('wizard.entity.field.group'), {
          help: t('wizard.entity.character.groupHelp')
        }),
        text('description', t('wizard.entity.field.shortDescription'), {
          help: t('wizard.entity.character.descriptionHelp'),
          multiline: true
        })
      ]
    case 'location':
      return [
        text('type', t('wizard.entity.field.type'), {
          help: t('wizard.entity.location.typeHelp')
        }),
        text('parent', t('wizard.entity.location.parent'), {
          help: t('wizard.entity.location.parentHelp')
        }),
        text('description', t('wizard.entity.field.description'), { multiline: true })
      ]
    case 'item':
      return [
        text('type', t('wizard.entity.field.type'), {
          help: t('wizard.entity.item.typeHelp')
        }),
        text('origin', t('wizard.entity.item.origin'), {
          help: t('wizard.entity.item.originHelp')
        }),
        text('description', t('wizard.entity.field.description'), { multiline: true })
      ]
    case 'lore':
      return [
        {
          id: 'category',
          title: t('wizard.entity.lore.category'),
          skippable: true,
          kind: 'choice',
          choices: LORE_CATEGORIES.map((c) => ({ value: c, label: c }))
        },
        text('description', t('wizard.entity.field.description'), { multiline: true })
      ]
    default:
      return (customDef?.defaultFields ?? []).map((field) => {
        switch (field.type) {
          case 'Int':
            return {
              id: field.key,
              title: field.displayName,
              skippable: true,
              kind: 'number' as const,
              defaultValue: field.defaultValue
            }
          case 'Bool':
            return {
              id: field.key,
              title: field.displayName,
              skippable: true,
              kind: 'choice' as const,
              choices: [
                { value: 'true', label: 'Yes' },
                { value: 'false', label: 'No' }
              ]
            }
          case 'Enum':
            return {
              id: field.key,
              title: field.displayName,
              skippable: true,
              kind: 'choice' as const,
              choices: (field.enumOptions ?? []).map((v) => ({ value: v, label: v }))
            }
          case 'Date':
            return { id: field.key, title: field.displayName, skippable: true, kind: 'date' as const }
          case 'EntityRef':
            return {
              id: field.key,
              title: field.displayName,
              skippable: true,
              kind: 'choice' as const,
              choices: [],
              entityRefType: (field.enumOptions?.[0] ?? 'Character').toLowerCase()
            }
          default:
            return text(field.key, field.displayName)
        }
      })
  }
}

/**
 * Character-interview steps: the seven psychology pillars, mapped into the
 * character's sections. Ported from CharacterInterviewWizard.Build (name
 * step stripped — the character already exists).
 */
export function buildInterviewSteps(t: TFunction): WizardStepDef[] {
  const pillar = (id: string): WizardStepDef =>
    text(id, t(`wizard.interview.${id}.title`), {
      help: t(`wizard.interview.${id}.help`),
      multiline: true
    })
  return [
    pillar('wound'),
    pillar('fear'),
    pillar('lie'),
    pillar('want'),
    pillar('need'),
    pillar('secret'),
    pillar('voice')
  ]
}

/** Section titles the interview answers map to, keyed by step id. */
export const INTERVIEW_SECTIONS: [string, string][] = [
  ['wound', 'Wound'],
  ['fear', 'Fear'],
  ['lie', 'Lie they believe'],
  ['want', 'Want'],
  ['need', 'Need'],
  ['secret', 'Secret'],
  ['voice', 'Voice']
]
