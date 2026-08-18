import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  ChevronLeft,
  MessageCircleQuestion,
  Settings2,
  SlidersHorizontal,
  Trash2
} from 'lucide-react'
import { useCodexStore, type EntityType } from '../../stores/codexStore'
import { useShellStore } from '../../stores/shellStore'
import { useBookScope } from '../../stores/projectStore'
import { rpc } from '../../rpc/client'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { EntityListsEditor } from './EntityListsEditor'
import { NameSuggestions } from './NameSuggestions'
import { EntityHistoryPanel } from './EntityHistoryPanel'
import { CustomTypeManager, type CustomTypeDefinition } from './CustomTypeManager'
import { WizardDialog } from './WizardDialog'
import {
  buildGuidedSteps,
  buildInterviewSteps,
  INTERVIEW_SECTIONS,
  type WizardStepDef
} from './wizards'
import { EntityImages } from './EntityImages'
import { EntityAttachments } from './EntityAttachments'
import { CustomPropsEditor } from './CustomPropsEditor'
import { MatchSettingsEditor } from './MatchSettingsEditor'
import { AiPolicyEditor } from './AiPolicyEditor'
import { ReaderPolicyEditor } from './ReaderPolicyEditor'
import { StateOverridesEditor } from './StateOverridesEditor'
import { ArcEditor } from './ArcEditor'
import { UnlinkedMentionsPanel } from './UnlinkedMentionsPanel'
import { OverridesEditor } from './OverridesEditor'
import { CodexNav } from './CodexNav'
import { EntityTable } from './EntityTable'
import {
  EntityDetailFields,
  builtInFieldKeys,
  builtInFieldLabelKeys
} from './EntityDetailFields'
import { ArrangeFieldsDialog } from './ArrangeFieldsDialog'
import type { EntitySummary } from '../../stores/codexStore'
import { MobileGroup, MobileNav, MobileRow, useMobileNav } from '../../shell/MobileNav'
import { useIsPhone } from '../../shell/useIsPhone'

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
  render: (entityType: EntityType, entityId: string) => React.ReactNode
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
        render: () => <EntityListsEditor />
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
function phoneEntityPage(
  id: string,
  entityType: EntityType,
  entityId: string | null
): React.ReactNode {
  const row = PHONE_ENTITY_GROUPS.flatMap((group) => group.rows).find(
    (candidate) => candidate.id === id
  )
  if (!row || !row.applies(entityType, entityId)) return null
  return <div className="codex-phone-page">{row.render(entityType, entityId ?? '')}</div>
}

function PhoneEntitySections({
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

const TYPES: { type: EntityType; key: string }[] = [
  { type: 'character', key: 'codexHub.characters' },
  { type: 'location', key: 'codexHub.locations' },
  { type: 'item', key: 'codexHub.items' },
  { type: 'lore', key: 'codexHub.lore' }
]

export function CodexView(): React.JSX.Element {
  const { t } = useTranslation()
  const [customTypes, setCustomTypes] = useState<CustomTypeDefinition[]>([])
  const [typeManagerOpen, setTypeManagerOpen] = useState(false)
  const [unlinkedOpen, setUnlinkedOpen] = useState(false)
  // Session state: a way of looking at the Codex, not a property of it.
  const [tableMode, setTableMode] = useState(false)
  const entityType = useCodexStore((s) => s.entityType)
  const entities = useCodexStore((s) => s.entities)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const setType = useCodexStore((s) => s.setType)
  const refresh = useCodexStore((s) => s.refresh)
  const bookScope = useBookScope()
  const select = useCodexStore((s) => s.select)
  const updateField = useCodexStore((s) => s.updateField)
  const create = useCodexStore((s) => s.create)
  const remove = useCodexStore((s) => s.remove)
  const moveWorldBible = useCodexStore((s) => s.moveWorldBible)
  // Single-pane only where the width forces it. An iPad in the tablet layout has
  // room for the desktop list + detail side by side, so it keeps both panes.
  const tabletLayout = useShellStore((s) => s.mobileLayout) === 'tablet'
  const isMobile = window.novalist.isMobile === true && !tabletLayout
  const isPhone = useIsPhone()
  const [arrangeOpen, setArrangeOpen] = useState(false)
  const [pending, setPending] = useState<
    { kind: 'create' } | { kind: 'delete'; entity: EntitySummary } | null
  >(null)
  const [templates, setTemplates] = useState<{ id: string; name: string }[]>([])
  const [templateId, setTemplateId] = useState<string>('')
  const [useWizard, setUseWizard] = useState(false)
  const [wizard, setWizard] = useState<{
    title: string
    steps: WizardStepDef[]
    apply: (answers: Record<string, string>) => Promise<void>
  } | null>(null)

  useEffect(() => {
    void refresh()
    void rpc
      .request<CustomTypeDefinition[]>('entities/customTypes')
      .then(setCustomTypes)
      .catch(() => setCustomTypes([]))
    // Entries belong to the active book. Keyed on `refresh` alone this ran once
    // per mount, so the Codex kept the previous book's entries until the view
    // was navigated away from and back.
  }, [refresh, bookScope])

  const selected = entities.find((e) => e.id === selectedId)

  const openCreate = (): void => {
    setTemplateId('')
    void rpc
      .request<{ id: string; name: string }[]>('entities/templates', [entityType])
      .then(setTemplates)
      .catch(() => setTemplates([]))
    setPending({ kind: 'create' })
  }

  /** Pours guided-wizard answers into the just-created entity. A character's
   * description becomes a "Description" section; everything else is a scalar
   * (or, for custom types, a Fields entry). */
  const applyGuided = async (answers: Record<string, string>): Promise<void> => {
    const { entityType: type, selectedId: id, selectedRecord } = useCodexStore.getState()
    if (!id) return
    for (const [key, v] of Object.entries(answers)) {
      if (!v.trim()) continue
      if (type === 'character' && key === 'description') continue
      await updateField(key, v.trim())
    }
    const description = (answers.description ?? '').trim()
    if (type === 'character' && description) {
      const sections = [
        ...((selectedRecord?.sections as { title: string; content: string }[]) ?? []),
        { title: 'Description', content: description }
      ]
      await rpc.request('entities/updateLists', [type, id, null, sections, null])
    }
    await useCodexStore.getState().select(id)
    await refresh()
  }

  /** Replace-or-append the seven interview sections on the open character. */
  const applyInterview = async (answers: Record<string, string>): Promise<void> => {
    const { selectedId: id, selectedRecord } = useCodexStore.getState()
    if (!id) return
    const sections = [
      ...((selectedRecord?.sections as { title: string; content: string }[]) ?? [])
    ]
    for (const [stepId, title] of INTERVIEW_SECTIONS) {
      const v = (answers[stepId] ?? '').trim()
      if (!v) continue
      const existing = sections.find((s) => s.title.toLowerCase() === title.toLowerCase())
      if (existing) existing.content = v
      else sections.push({ title, content: v })
    }
    await rpc.request('entities/updateLists', ['character', id, null, sections, null])
    await useCodexStore.getState().select(id)
  }

  const startCreate = async (name: string): Promise<void> => {
    await create(name, templateId || null)
    if (!useWizard) return
    const customDef = customTypes.find((d) => d.typeKey === entityType)
    const steps = buildGuidedSteps(entityType, customDef, t)
    if (steps.length > 0) {
      setWizard({
        title: customDef ? customDef.displayName : t(`wizard.entity.${entityType}.displayName`),
        steps,
        apply: applyGuided
      })
    }
  }

  const tree = (
    <div className="codex">
      <div className="codex-tabs">
        {TYPES.map(({ type, key }) => (
          <button
            key={type}
            className={`codex-tab${entityType === type ? ' active' : ''}`}
            onClick={() => void setType(type)}
          >
            {t(key)}
          </button>
        ))}
        {customTypes.map((custom) => (
          <button
            key={custom.typeKey}
            className={`codex-tab${entityType === custom.typeKey ? ' active' : ''}`}
            onClick={() => void setType(custom.typeKey)}
          >
            {custom.displayNamePlural}
          </button>
        ))}
        {/* Names the prose uses without linking them. Book-wide rather than
            per-entry: the scan reads every scene once either way. */}
        <button
          className="codex-tab codex-tab-unlinked"
          onClick={() => setUnlinkedOpen(!unlinkedOpen)}
        >
          {t('unlinked.title')}
        </button>
        {/* The Codex edited one form at a time, so filing forty characters into
            their houses meant forty round trips through the detail pane. A
            table is the shape that work actually has. */}
        <button
          className={`codex-tab codex-tab-table${tableMode ? ' active' : ''}`}
          onClick={() => setTableMode(!tableMode)}
        >
          {t('codexTable.title')}
        </button>
        <button className="codex-tab codex-tab-manage" onClick={() => setTypeManagerOpen(true)}>
          <Settings2 size={13} strokeWidth={2} /> {t('codexHub.manageTypes')}
        </button>
      </div>
      {unlinkedOpen && (
        <div className="codex-unlinked">
          <UnlinkedMentionsPanel />
        </div>
      )}
      {tableMode && <EntityTable />}
      {!tableMode && (
      <div className={`codex-body${isMobile ? ' codex-body-mobile' : ''}`}>
        {/* Mobile is single-pane: the entity list, or the detail (with a back
            button) once an entry is selected. Desktop shows both side by side. */}
        {(!isMobile || !selectedId) && (
          <CodexNav
            entityType={entityType}
            entities={entities}
            selectedId={selectedId}
            onSelect={(id) => void select(id)}
            onCreate={openCreate}
            onMove={(id, toWorldBible) => void moveWorldBible(id, toWorldBible)}
            onDelete={(entity) => setPending({ kind: 'delete', entity })}
          />
        )}
        {(!isMobile || selectedId) && (
          <div className="codex-detail">
            {isMobile && selectedId && (
              <button
                type="button"
                className="mobile-back codex-detail-back"
                onClick={() => useCodexStore.setState({ selectedId: null, selectedRecord: null })}
              >
                <ChevronLeft size={20} strokeWidth={2} />
                <span>{t('shell.view.codex')}</span>
              </button>
            )}
            {record ? (
              <>
                <div className="codex-detail-actions">
                {entityType === 'character' && (
                  <button
                    className="dialog-button"
                    onClick={() =>
                      setWizard({
                        title: t('wizard.interview.displayName'),
                        steps: buildInterviewSteps(t),
                        apply: applyInterview
                      })
                    }
                  >
                    <MessageCircleQuestion size={13} strokeWidth={2} /> {t('wizard.runInterview')}
                  </button>
                )}
                {builtInFieldKeys(entityType).length > 0 && (
                  <button className="dialog-button" onClick={() => setArrangeOpen(true)}>
                    <SlidersHorizontal size={13} strokeWidth={2} />{' '}
                    {t('entityEditor.arrangeFields')}
                  </button>
                )}
                <button
                  className="dialog-button danger"
                  onClick={() => selected && setPending({ kind: 'delete', entity: selected })}
                >
                  <Trash2 size={13} strokeWidth={2} /> {t('explorer.contextDelete')}
                </button>
              </div>
              <EntityDetailFields
                entityType={entityType}
                record={record}
                customDef={customTypes.find((d) => d.typeKey === entityType)}
                updateField={updateField}
              />
              {isPhone ? (
                /* On a phone the entry reads like a contact card: the fields
                   above are what the writer came for, and everything below is
                   an editor in its own right. Stacked, they made the page a
                   scroll of a dozen forms; behind rows, each is one tap away
                   and the page stays a page. */
                <PhoneEntitySections
                  entityType={entityType}
                  entityId={selectedId}
                />
              ) : (
                <>
              <EntityImages />
              <EntityAttachments />
              <CustomPropsEditor />
              {/* How this entry's name is picked up in prose. Collapsed by
                  default: the defaults are right for most entries. */}
              {selectedId && (
                <details className="codex-match">
                  <summary>{t('match.title')}</summary>
                  <MatchSettingsEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {/* What this entry is like at points in the story. Characters
                  have their own richer editor below; this is for everything
                  else, which had none at all. */}
              {selectedId && entityType !== 'character' && (
                <details className="codex-match">
                  <summary>{t('stateOverride.title')}</summary>
                  <StateOverridesEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {/* Where the character starts, ends, and turns. Characters only:
                  a location does not have an arc, it has a state. */}
              {selectedId && entityType === 'character' && (
                <details className="codex-match">
                  <summary>{t('arc.title')}</summary>
                  <ArcEditor characterId={selectedId} />
                </details>
              )}
              {/* What an AI extension may see of this entry. Collapsed by
                  default; the default policy is what Novalist always did. */}
              {/* What the entry said before its last few saves. Folded away:
                  it is a way out of a mistake, not something read daily. */}
              {selectedId && (
                <details className="codex-match">
                  <summary>{t('entityHistory.title')}</summary>
                  <EntityHistoryPanel entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {selectedId && (
                <details className="codex-match">
                  <summary>{t('aiPolicy.title')}</summary>
                  <AiPolicyEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {/* A different question from the AI one: what a reader may see,
                  when the world goes out of the app as a page. */}
              {selectedId && (
                <details className="codex-match">
                  <summary>{t('readerPolicy.title')}</summary>
                  <ReaderPolicyEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              <OverridesEditor />
              <EntityListsEditor />
                </>
              )}
            </>
            ) : (
              <p className="codex-empty">{t('codexHub.selectHint')}</p>
            )}
          </div>
        )}
      </div>
      )}
      {pending?.kind === 'create' && (
        <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && setPending(null)}>
          <div className="dialog-card" role="dialog" aria-label={t('codexHub.newEntry')}>
            <div className="dialog-title">{t('codexHub.newEntry')}</div>
            <input
              id="codex-create-name"
              className="dialog-input"
              autoFocus
              onKeyDown={(e) => {
                if (e.key === 'Escape') setPending(null)
                if (e.key === 'Enter') {
                  const name = (e.target as HTMLInputElement).value.trim()
                  if (name) {
                    setPending(null)
                    void startCreate(name)
                  }
                }
              }}
            />
            {/* Where the name is actually typed, so the moment naming stops
                the work is the moment it is offered. */}
            <NameSuggestions
              onPick={(name) => {
                const input = document.getElementById(
                  'codex-create-name'
                ) as HTMLInputElement | null
                if (input) {
                  input.value = name
                  input.focus()
                }
              }}
            />
            {templates.length > 0 && (
              <select
                className="dialog-input"
                value={templateId}
                onChange={(e) => setTemplateId(e.target.value)}
              >
                <option value="">{t('welcome.template')}</option>
                {templates.map((tpl) => (
                  <option key={tpl.id} value={tpl.id}>
                    {tpl.name}
                  </option>
                ))}
              </select>
            )}
            <label className="type-manager-check">
              <input
                type="checkbox"
                checked={useWizard}
                onChange={(e) => setUseWizard(e.target.checked)}
              />
              {t('wizard.useWizard')}
            </label>
            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setPending(null)}>
                {t('dialog.cancel')}
              </button>
              <button
                className="dialog-button primary"
                onClick={() => {
                  const input = document.getElementById('codex-create-name') as HTMLInputElement | null
                  const name = input?.value.trim()
                  if (name) {
                    setPending(null)
                    void startCreate(name)
                  }
                }}
              >
                {t('dialog.ok')}
              </button>
            </div>
          </div>
        </div>
      )}
      {arrangeOpen && (
        <ArrangeFieldsDialog
          entityType={entityType}
          fields={builtInFieldKeys(entityType)}
          labels={Object.fromEntries(
            Object.entries(builtInFieldLabelKeys(entityType)).map(([key, labelKey]) => [
              key,
              t(labelKey)
            ])
          )}
          onClose={() => setArrangeOpen(false)}
        />
      )}
      {wizard && (
        <WizardDialog
          title={wizard.title}
          steps={wizard.steps}
          onFinish={(answers) => {
            const apply = wizard.apply
            setWizard(null)
            void apply(answers)
          }}
          onClose={() => setWizard(null)}
        />
      )}
      {typeManagerOpen && (
        <CustomTypeManager
          types={customTypes}
          onChanged={(updated) => {
            setCustomTypes(updated)
            if (
              !['character', 'location', 'item', 'lore'].includes(entityType) &&
              !updated.some((d) => d.typeKey === entityType)
            ) {
              void setType('character')
            }
          }}
          onClose={() => setTypeManagerOpen(false)}
        />
      )}
      {pending?.kind === 'delete' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={pending.entity.name}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const entity = pending.entity
            setPending(null)
            void remove(entity.id, entity.isWorldBible)
          }}
        />
      )}
    </div>
  )

  // The phone wraps the whole view, not just the detail: a pushed editor should
  // cover the type tabs and the entry list too, the way a pushed page does on
  // iOS. Switching tabs unmounts this and takes the stack with it.
  return isPhone ? (
    <MobileNav
      title={t('shell.view.codex')}
      renderPage={(id) => phoneEntityPage(id, entityType, selectedId)}
    >
      {tree}
    </MobileNav>
  ) : (
    tree
  )
}
