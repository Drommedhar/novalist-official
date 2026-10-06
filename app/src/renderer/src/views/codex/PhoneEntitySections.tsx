import { useTranslation } from 'react-i18next'
import { type EntityType } from '../../stores/codexStore'
import { EntityListsEditor } from './EntityListsEditor'
import { EntityHistoryPanel } from './EntityHistoryPanel'
import { type CustomTypeDefinition } from './CustomTypeManager'
import { EntityImages } from './EntityImages'
import { EntityAttachments } from './EntityAttachments'
import { CustomPropsEditor } from './CustomPropsEditor'
import { MatchSettingsEditor } from './MatchSettingsEditor'
import { AiPolicyEditor } from './AiPolicyEditor'
import { ReaderPolicyEditor } from './ReaderPolicyEditor'
import { StateOverridesEditor } from './StateOverridesEditor'
import { ArcEditor } from './ArcEditor'
import { OverridesEditor } from './OverridesEditor'
import { MobileGroup, MobileRow, useMobileNav } from '../../shell/MobileNav'

/**
 * The entry's editors as rows that open one at a time.
 *
 * A Codex entry carries a dozen editors - relationships, images, attachments,
 * custom properties, chapter overrides, arc, name matching, history, two
 * policies. Down a desktop pane they read as sections of one page. On a phone
 * they became a scroll through a dozen stacked forms to reach the last, so here
 * each is a row that pushes its own page, and the fields above stay the page.
 *
 * Grouped by what the writer is doing: what the entry IS, what it looks like,
 * how it behaves in the story, and the technical settings behind it.
 */
interface PhoneEntityRow {
  /** What the row pushes under, and what names the page back again. */
  id: string
  labelKey: string
  /** Whether this editor applies to the entry in front of the writer. */
  applies: (entityType: EntityType, entityId: string | null) => boolean
  render: (
    entityType: EntityType,
    entityId: string,
    customDef?: CustomTypeDefinition
  ) => React.ReactNode
}

/**
 * The editors a phone keeps behind rows, in the order the entry lists them.
 *
 * One table read twice: once for the rows, once for the page a row opens. The
 * pushed page is rendered from the id it was pushed under rather than from an
 * element captured at the tap, which is what keeps the editor inside it live.
 *
 * The inline labels these sections use are written in caps in the locale files
 * ("RELATIONSHIPS"), which is right above a field and shouting as a page title,
 * so the pushed pages take sentence-case titles of their own.
 */
const PHONE_ENTITY_GROUPS: { headerKey: string; rows: PhoneEntityRow[] }[] = [
  {
    headerKey: 'mobile.entity.relationships',
    rows: [
      {
        id: 'relationships',
        labelKey: 'mobile.entity.relationships',
        applies: () => true,
        render: (entityType, entityId, customDef) => (
          <EntityListsEditor key={`${entityType}:${entityId}`} customDef={customDef} />
        )
      },
      {
        id: 'customProperties',
        labelKey: 'mobile.entity.customProperties',
        applies: () => true,
        render: () => <CustomPropsEditor />
      }
    ]
  },
  {
    headerKey: 'mobile.entity.images',
    rows: [
      {
        id: 'images',
        labelKey: 'mobile.entity.images',
        applies: () => true,
        render: () => <EntityImages />
      },
      {
        id: 'attachments',
        labelKey: 'attachments.title',
        applies: () => true,
        render: () => <EntityAttachments />
      }
    ]
  },
  {
    headerKey: 'mobile.entity.chapterOverrides',
    rows: [
      {
        id: 'chapterOverrides',
        labelKey: 'mobile.entity.chapterOverrides',
        applies: () => true,
        render: () => <OverridesEditor />
      },
      {
        id: 'arc',
        labelKey: 'arc.title',
        applies: (entityType, entityId) => entityId !== null && entityType === 'character',
        render: (_entityType, entityId) => <ArcEditor characterId={entityId} />
      },
      {
        id: 'stateOverrides',
        labelKey: 'stateOverride.title',
        applies: (entityType, entityId) => entityId !== null && entityType !== 'character',
        render: (entityType, entityId) => (
          <StateOverridesEditor entityType={entityType} entityId={entityId} />
        )
      }
    ]
  },
  {
    headerKey: 'match.title',
    rows: [
      {
        id: 'match',
        labelKey: 'match.title',
        applies: (_entityType, entityId) => entityId !== null,
        render: (entityType, entityId) => (
          <MatchSettingsEditor entityType={entityType} entityId={entityId} />
        )
      },
      {
        id: 'history',
        labelKey: 'entityHistory.title',
        applies: (_entityType, entityId) => entityId !== null,
        render: (entityType, entityId) => (
          <EntityHistoryPanel entityType={entityType} entityId={entityId} />
        )
      },
      {
        id: 'aiPolicy',
        labelKey: 'aiPolicy.title',
        applies: (_entityType, entityId) => entityId !== null,
        render: (entityType, entityId) => (
          <AiPolicyEditor entityType={entityType} entityId={entityId} />
        )
      },
      {
        id: 'readerPolicy',
        labelKey: 'readerPolicy.title',
        applies: (_entityType, entityId) => entityId !== null,
        render: (entityType, entityId) => (
          <ReaderPolicyEditor entityType={entityType} entityId={entityId} />
        )
      }
    ]
  }
]

/** The page a codex row opens, resolved from the id it was pushed under. */
export function phoneEntityPage(
  id: string,
  entityType: EntityType,
  entityId: string | null,
  customDef?: CustomTypeDefinition
): React.ReactNode {
  const row = PHONE_ENTITY_GROUPS.flatMap((group) => group.rows).find(
    (candidate) => candidate.id === id
  )
  if (!row || !row.applies(entityType, entityId)) return null
  return <div className="codex-phone-page">{row.render(entityType, entityId ?? '', customDef)}</div>
}

export function PhoneEntitySections({
  entityType,
  entityId
}: {
  entityType: EntityType
  entityId: string | null
}): React.JSX.Element {
  const { t } = useTranslation()
  const nav = useMobileNav()

  return (
    <div className="codex-phone-sections">
      {PHONE_ENTITY_GROUPS.map((group) => {
        const rows = group.rows.filter((row) => row.applies(entityType, entityId))
        if (rows.length === 0) return null
        return (
          <MobileGroup key={group.headerKey} header={t(group.headerKey)}>
            {rows.map((row) => (
              <MobileRow
                key={row.id}
                label={t(row.labelKey)}
                onClick={() => nav.push({ id: row.id, title: t(row.labelKey) })}
              />
            ))}
          </MobileGroup>
        )
      })}
    </div>
  )
}
