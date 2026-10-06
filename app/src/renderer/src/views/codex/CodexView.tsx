import { DesktopViewActions } from '../../shell/DesktopViewFrame'
import './codex.css'
import { useEffect, useRef, useState } from 'react'
import { useContentTransition } from '../../shell/useContentTransition'
import { MotionPresence } from '../../shell/MotionPresence'
import { useTranslation } from 'react-i18next'
import { ChevronLeft, MessageCircleQuestion, Settings2, SlidersHorizontal, Trash2 } from 'lucide-react'
import { useCodexStore, type EntityType, type EntitySummary } from '../../stores/codexStore'
import { useShellStore } from '../../stores/shellStore'
import { useBookScope } from '../../stores/projectStore'
import { rpc } from '../../rpc/client'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { EntityListsEditor } from './EntityListsEditor'
import { NameSuggestions } from './NameSuggestions'
import { EntityHistoryPanel } from './EntityHistoryPanel'
import { CustomTypeManager, type CustomTypeDefinition } from './CustomTypeManager'
import { WizardDialog } from './WizardDialog'
import { buildGuidedSteps, buildInterviewSteps, INTERVIEW_SECTIONS, type WizardStepDef } from './wizards'
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
import { EntityDetailFields, builtInFieldKeys, builtInFieldLabelKeys } from './EntityDetailFields'
import { ArrangeFieldsDialog } from './ArrangeFieldsDialog'
import { MobileNav } from '../../shell/MobileNav'
import { useIsPhone } from '../../shell/useIsPhone'
import { phoneEntityPage, PhoneEntitySections } from './PhoneEntitySections'

const TYPES: { type: EntityType; key: string }[] = [
  { type: 'character', key: 'codexHub.characters' },
  { type: 'location', key: 'codexHub.locations' },
  { type: 'item', key: 'codexHub.items' },
  { type: 'lore', key: 'codexHub.lore' }
]

function useCodexView() {
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

  const [detailTab, setDetailTab] = useState('overview')

  return {openCreate, t, entityType, setType, customTypes, setUnlinkedOpen, unlinkedOpen, tableMode, setTableMode, setTypeManagerOpen, isMobile, selectedId, entities, select, moveWorldBible, setPending, detailTab, record, selected, setDetailTab, setWizard, setArrangeOpen, updateField, isPhone, pending, startCreate, templates, templateId, setTemplateId, useWizard, setUseWizard, arrangeOpen, wizard, typeManagerOpen, setCustomTypes, remove}
}

export function CodexView(): React.JSX.Element {
  const { openCreate, t, entityType, setType, customTypes, setUnlinkedOpen, unlinkedOpen, tableMode, setTableMode, setTypeManagerOpen, isMobile, selectedId, entities, select, moveWorldBible, setPending, detailTab, record, selected, setDetailTab, setWizard, setArrangeOpen, updateField, isPhone, pending, startCreate, templates, templateId, setTemplateId, useWizard, setUseWizard, arrangeOpen, wizard, typeManagerOpen, setCustomTypes, remove } = useCodexView()
  const contentRef = useRef<HTMLDivElement>(null)
  useContentTransition(contentRef, `${entityType}:${tableMode}`)

  const tree = (
    <div className="codex" ref={contentRef}>
      <DesktopViewActions>
        <button className="dialog-button primary" onClick={openCreate}>
          {t('codexHub.newEntry')}
        </button>
      </DesktopViewActions>
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
      <MotionPresence collapse>{unlinkedOpen && (
        <div className="codex-unlinked">
          <UnlinkedMentionsPanel />
        </div>
      )}</MotionPresence>
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
            <CodexDetail detailTab={detailTab} isMobile={isMobile} selectedId={selectedId} t={t} record={record} selected={selected} setDetailTab={setDetailTab} entityType={entityType} setWizard={setWizard} setArrangeOpen={setArrangeOpen} setPending={setPending} customTypes={customTypes} updateField={updateField} isPhone={isPhone} />
          )}
        </div>
      )}
      <MotionPresence>{pending?.kind === 'create' && (
        <CodexCreateDialog setPending={setPending} t={t} startCreate={startCreate} templates={templates} templateId={templateId} setTemplateId={setTemplateId} useWizard={useWizard} setUseWizard={setUseWizard} />
      )}</MotionPresence>
      <MotionPresence>{arrangeOpen && (
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
      )}</MotionPresence>
      <MotionPresence>{wizard && (
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
      )}</MotionPresence>
      <MotionPresence>{typeManagerOpen && (
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
      )}</MotionPresence>
      <MotionPresence>{pending?.kind === 'delete' && (
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
      )}</MotionPresence>
    </div>
  )

  // The phone wraps the whole view, not just the detail: a pushed editor should
  // cover the type tabs and the entry list too, the way a pushed page does on
  // iOS. Switching tabs unmounts this and takes the stack with it.
  return isPhone ? (
    <MobileNav
      title={t('shell.view.codex')}
      renderPage={(id) =>
        phoneEntityPage(
          id,
          entityType,
          selectedId,
          customTypes.find((d) => d.typeKey === entityType)
        )
      }
    >
      {tree}
    </MobileNav>
  ) : (
    tree
  )
}

  /** Pours guided-wizard answers into the just-created entity. A character's
   * description becomes a "Description" section; everything else is a scalar
   * (or, for custom types, a Fields entry). */
async function applyGuided(answers: Record<string, string>): Promise<void> {
  const {updateField, refresh} = useCodexStore.getState()
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
async function applyInterview(answers: Record<string, string>): Promise<void> {
    const { selectedId: id, selectedRecord } = useCodexStore.getState()
    if (!id) return
    const sections = [...((selectedRecord?.sections as { title: string; content: string }[]) ?? [])]
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


function CodexDetail({ detailTab, isMobile, selectedId, t, record, selected, setDetailTab, entityType, setWizard, setArrangeOpen, setPending, customTypes, updateField, isPhone }: { detailTab: CodexViewState['detailTab']; isMobile: CodexViewState['isMobile']; selectedId: CodexViewState['selectedId']; t: CodexViewState['t']; record: CodexViewState['record']; selected: CodexViewState['selected']; setDetailTab: CodexViewState['setDetailTab']; entityType: CodexViewState['entityType']; setWizard: CodexViewState['setWizard']; setArrangeOpen: CodexViewState['setArrangeOpen']; setPending: CodexViewState['setPending']; customTypes: CodexViewState['customTypes']; updateField: CodexViewState['updateField']; isPhone: CodexViewState['isPhone'] }): React.JSX.Element {
  const detailRef = useRef<HTMLDivElement>(null)
  useContentTransition(detailRef, `${selectedId ?? ''}:${detailTab}`)
  return (
    <div className="codex-detail" data-detail-tab={detailTab} ref={detailRef}>
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
          {!isMobile && (
            <>
              <header className="codex-entry-heading">
                {selected?.imagePath ? (
                  <img
                    className="codex-entry-avatar"
                    src={`novalist-project://nl/${encodeURI(selected.imagePath)}`}
                    alt=""
                  />
                ) : (
                  <span className="codex-entry-avatar">{selected?.name.slice(0, 1)}</span>
                )}
                <div>
                  <h2>{selected?.name}</h2>
                  <p>{selected?.detail}</p>
                </div>
              </header>
              <nav className="codex-entry-tabs" aria-label={t('desktopRefresh.details')}>
                {(
                  [
                    ['overview', 'desktopRefresh.overview'],
                    ['story', 'desktopRefresh.story'],
                    ['relationships', 'desktopRefresh.relationships'],
                    ['details', 'desktopRefresh.details']
                  ] as const
                ).map(([id, key]) => (
                  <button
                    key={id}
                    aria-pressed={detailTab === id}
                    className={detailTab === id ? 'active' : ''}
                    onClick={() => setDetailTab(id)}
                  >
                    {t(key)}
                  </button>
                ))}
              </nav>
            </>
          )}
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
                <MessageCircleQuestion size={13} strokeWidth={2} />{' '}
                {t('wizard.runInterview')}
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
            <PhoneEntitySections entityType={entityType} entityId={selectedId} />
          ) : (
            <>
              <section data-entity-tab="details" className="codex-entry-section">
                <EntityImages />
                <EntityAttachments />
                <CustomPropsEditor />
              </section>
              {/* How this entry's name is picked up in prose. Collapsed by
          default: the defaults are right for most entries. */}
              {selectedId && (
                <details className="codex-match" data-entity-tab="details">
                  <summary>{t('match.title')}</summary>
                  <MatchSettingsEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {/* What this entry is like at points in the story. Characters
          have their own richer editor below; this is for everything
          else, which had none at all. */}
              {selectedId && entityType !== 'character' && (
                <details className="codex-match" data-entity-tab="story">
                  <summary>{t('stateOverride.title')}</summary>
                  <StateOverridesEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {/* Where the character starts, ends, and turns. Characters only:
          a location does not have an arc, it has a state. */}
              {selectedId && entityType === 'character' && (
                <details className="codex-match" data-entity-tab="story">
                  <summary>{t('arc.title')}</summary>
                  <ArcEditor characterId={selectedId} />
                </details>
              )}
              {/* What an AI extension may see of this entry. Collapsed by
          default; the default policy is what Novalist always did. */}
              {/* What the entry said before its last few saves. Folded away:
          it is a way out of a mistake, not something read daily. */}
              {selectedId && (
                <details className="codex-match" data-entity-tab="details">
                  <summary>{t('entityHistory.title')}</summary>
                  <EntityHistoryPanel entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {selectedId && (
                <details className="codex-match" data-entity-tab="details">
                  <summary>{t('aiPolicy.title')}</summary>
                  <AiPolicyEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              {/* A different question from the AI one: what a reader may see,
          when the world goes out of the app as a page. */}
              {selectedId && (
                <details className="codex-match" data-entity-tab="details">
                  <summary>{t('readerPolicy.title')}</summary>
                  <ReaderPolicyEditor entityType={entityType} entityId={selectedId} />
                </details>
              )}
              <section data-entity-tab="story">
                <OverridesEditor />
              </section>
              <EntityListsEditor
                key={`${entityType}:${selectedId}`}
                customDef={customTypes.find((d) => d.typeKey === entityType)}
              />
            </>
          )}
        </>
      ) : (
        <p className="codex-empty">{t('codexHub.selectHint')}</p>
      )}
    </div>
  )
}

type CodexViewState = ReturnType<typeof useCodexView>

function CodexCreateDialog({ setPending, t, startCreate, templates, templateId, setTemplateId, useWizard, setUseWizard }: { setPending: CodexViewState['setPending']; t: CodexViewState['t']; startCreate: CodexViewState['startCreate']; templates: CodexViewState['templates']; templateId: CodexViewState['templateId']; setTemplateId: CodexViewState['setTemplateId']; useWizard: CodexViewState['useWizard']; setUseWizard: CodexViewState['setUseWizard'] }): React.JSX.Element {
  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && setPending(null)}
    >
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
              const input = document.getElementById(
                'codex-create-name'
              ) as HTMLInputElement | null
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
  )
}
