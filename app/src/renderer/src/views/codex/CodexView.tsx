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
  const select = useCodexStore((s) => s.select)
  const updateField = useCodexStore((s) => s.updateField)
  const create = useCodexStore((s) => s.create)
  const remove = useCodexStore((s) => s.remove)
  const moveWorldBible = useCodexStore((s) => s.moveWorldBible)
  const isMobile = window.novalist.isMobile === true
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
  }, [refresh])

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

  return (
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
              <OverridesEditor />
              <EntityListsEditor />
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
}
