import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Pencil, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { useProjectStore } from '../../stores/projectStore'
import './entity-images.css'
import { type OverrideImage, type OverrideRelationship, type OverrideSection, type CharacterOverride, OVERRIDE_GROUPS, OVERRIDABLE, type Scope, scopeKey, matchesScope, apply } from './overrideModel'
import { OverrideImages } from './OverrideImages'
import { OverrideRelationships } from './OverrideRelationships'
import { OverrideSections } from './OverrideSections'

/** Per-chapter/scene character overrides. Editing happens INLINE in the detail
 * pane (an expandable form under the scope row / add button), never in a modal —
 * mirroring the Avalonia entity editor. Diff-stored, blank = inherit. Covers the
 * full identity + physical field set, per-scope custom-property overrides, and —
 * for an already-saved scope — per-scope image, relationship, and section
 * overrides; resolved values surface in the focus-peek card and context sidebar. */
export function OverridesEditor(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const chapters = useProjectStore((s) => s.chapters)
  const [editing, setEditing] = useState<Scope | 'new' | null>(null)
  const [newScope, setNewScope] = useState<Scope>({ chapter: '', scene: null })
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [customDraft, setCustomDraft] = useState<Record<string, string>>({})

  if (entityType !== 'character' || !record || !selectedId) return null
  const overrides = Array.isArray(record.chapterOverrides)
    ? (record.chapterOverrides as CharacterOverride[])
    : []

  const baseCustom =
    record.customProperties && typeof record.customProperties === 'object'
      ? (record.customProperties as Record<string, string>)
      : {}
  const customKeys = Object.keys(baseCustom)

  const baseImages = Array.isArray(record.images) ? (record.images as OverrideImage[]) : []
  const baseRelationships = Array.isArray(record.relationships)
    ? (record.relationships as OverrideRelationship[])
    : []
  const baseSections = Array.isArray(record.sections) ? (record.sections as OverrideSection[]) : []

  const chapterTitle = (guid: string): string =>
    chapters.find((c) => c.guid === guid)?.title ?? guid

  const scopeLabel = (over: CharacterOverride): string =>
    over.scene ? `${chapterTitle(over.chapter)} → ${over.scene}` : chapterTitle(over.chapter)

  const overriddenLabels = (over: CharacterOverride): string => {
    const parts = OVERRIDABLE.filter(
      (f) => typeof over[f.key] === 'string' && (over[f.key] as string).length > 0
    ).map((f) => t(f.labelKey))
    if (over.customProperties && Object.keys(over.customProperties).length > 0)
      parts.push(t('entityEditor.customProperties'))
    if (Array.isArray(over.images)) parts.push(t('entityEditor.images'))
    if (Array.isArray(over.relationships)) parts.push(t('entityEditor.relationships'))
    if (Array.isArray(over.sections)) parts.push(t('entityEditor.sections'))
    return parts.join(', ')
  }

  const openEditor = (target: Scope | 'new'): void => {
    const scope = target === 'new' ? { chapter: chapters[0]?.guid ?? '', scene: null } : target
    const existing = overrides.find((o) => matchesScope(o, scope))
    const values: Record<string, string> = {}
    for (const field of OVERRIDABLE) {
      const value = existing?.[field.key]
      values[field.key] = typeof value === 'string' ? value : ''
    }
    const custom: Record<string, string> = {}
    for (const key of customKeys) custom[key] = existing?.customProperties?.[key] ?? ''
    setDraft(values)
    setCustomDraft(custom)
    if (target === 'new') setNewScope(scope)
    setEditing(target === 'new' ? 'new' : scope)
  }

  const removeOverride = (over: CharacterOverride): void => {
    setEditing(null)
    void rpc
      .request<Record<string, unknown>>('entities/removeOverride', [
        selectedId,
        over.chapter,
        over.scene ?? null
      ])
      .then(apply)
  }

  const save = (scope: Scope): void => {
    setEditing(null)
    void rpc
      .request<Record<string, unknown>>('entities/setOverride', [
        selectedId,
        scope.chapter,
        scope.scene,
        draft,
        customDraft
      ])
      .then(apply)
  }

  const isEditing = (over: CharacterOverride): boolean =>
    editing !== null && editing !== 'new' && matchesScope(over, editing)

  const newScopeScenes = chapters.find((c) => c.guid === newScope.chapter)?.scenes ?? []



  return (
    <div className="entity-lists">
      <div className="inspector-label">{t('entityEditor.chapterOverrides')}</div>
      {overrides.length === 0 && editing !== 'new' && (
        <p className="codex-empty overrides-empty">{t('entityEditor.overridesHint')}</p>
      )}
      <div className="overrides-list">
        {overrides.map((over) => (
          <div key={scopeKey(over.chapter, over.scene ?? null)} className="overrides-item">
            <div className="overrides-row">
              <button
                className="overrides-scope"
                onClick={() =>
                  isEditing(over)
                    ? setEditing(null)
                    : openEditor({ chapter: over.chapter, scene: over.scene ?? null })
                }
              >
                <span className="overrides-scope-label">{scopeLabel(over)}</span>
                <span className="overrides-scope-fields">
                  {overriddenLabels(over) || t('entityEditor.overridesNoneSet')}
                </span>
              </button>
              <button
                className="binder-expand"
                aria-label={t('entityEditor.editOverride')}
                onClick={() =>
                  isEditing(over)
                    ? setEditing(null)
                    : openEditor({ chapter: over.chapter, scene: over.scene ?? null })
                }
              >
                <Pencil size={12} strokeWidth={2} />
              </button>
              <button
                className="binder-expand"
                aria-label={t('explorer.contextDelete')}
                onClick={() => removeOverride(over)}
              >
                <X size={12} strokeWidth={2} />
              </button>
            </div>
            {isEditing(over) && <OverrideForm scope={{ chapter: over.chapter, scene: over.scene ?? null }} pickScope={false} overrides={overrides} t={t} setNewScope={setNewScope} chapters={chapters} newScopeScenes={newScopeScenes} record={record} draft={draft} setDraft={setDraft} customKeys={customKeys} baseCustom={baseCustom} customDraft={customDraft} setCustomDraft={setCustomDraft} setEditing={setEditing} save={save} selectedId={selectedId} baseImages={baseImages} baseRelationships={baseRelationships} baseSections={baseSections} />}
          </div>
        ))}
      </div>
      {editing === 'new' ? (
        <OverrideForm scope={newScope} pickScope overrides={overrides} t={t} setNewScope={setNewScope} chapters={chapters} newScopeScenes={newScopeScenes} record={record} draft={draft} setDraft={setDraft} customKeys={customKeys} baseCustom={baseCustom} customDraft={customDraft} setCustomDraft={setCustomDraft} setEditing={setEditing} save={save} selectedId={selectedId} baseImages={baseImages} baseRelationships={baseRelationships} baseSections={baseSections} />
      ) : (
        chapters.length > 0 && (
          <button className="binder-rail-item" onClick={() => openEditor('new')}>
            <Plus size={13} strokeWidth={2} />
            {t('entityEditor.addOverride')}
          </button>
        )
      )}
    </div>
  )
}

interface OverrideFormProps {
  scope: Scope
  pickScope: boolean
  overrides: CharacterOverride[]
  t: ReturnType<typeof useTranslation>['t']
  setNewScope: React.Dispatch<React.SetStateAction<Scope>>
  chapters: ReturnType<typeof useProjectStore.getState>['chapters']
  newScopeScenes: ReturnType<typeof useProjectStore.getState>['chapters'][number]['scenes']
  record: Record<string, unknown>
  draft: Record<string, string>
  setDraft: React.Dispatch<React.SetStateAction<Record<string, string>>>
  customKeys: string[]
  baseCustom: Record<string, string>
  customDraft: Record<string, string>
  setCustomDraft: React.Dispatch<React.SetStateAction<Record<string, string>>>
  setEditing: React.Dispatch<React.SetStateAction<Scope | 'new' | null>>
  save: (scope: Scope) => void
  selectedId: string
  baseImages: OverrideImage[]
  baseRelationships: OverrideRelationship[]
  baseSections: OverrideSection[]
}

function OverrideForm({scope, pickScope, overrides, t, setNewScope, chapters, newScopeScenes, record, draft, setDraft, customKeys, baseCustom, customDraft, setCustomDraft, setEditing, save, selectedId, baseImages, baseRelationships, baseSections}: OverrideFormProps): React.JSX.Element {
    const existing = overrides.find((o) => matchesScope(o, scope))
    return (
      <div className="overrides-inline" role="group" aria-label={t('entityEditor.chapterOverrides')}>
        {pickScope && (
          <div className="overrides-scope-pickers">
            <select
              className="dialog-input"
              aria-label={t('entityEditor.chapterOverrides')}
              value={scope.chapter}
              onChange={(e) => setNewScope({ chapter: e.target.value, scene: null })}
            >
              {chapters.map((c) => (
                <option key={c.guid} value={c.guid}>
                  {c.title}
                </option>
              ))}
            </select>
            <select
              className="dialog-input"
              aria-label={t('entityEditor.wholeChapter')}
              value={scope.scene ?? ''}
              onChange={(e) => setNewScope({ chapter: scope.chapter, scene: e.target.value || null })}
            >
              <option value="">{t('entityEditor.wholeChapter')}</option>
              {newScopeScenes.map((s) => (
                <option key={s.id} value={s.title}>
                  {s.title}
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="overrides-form">
          {OVERRIDE_GROUPS.map((group) => (
            <div key={group.titleKey} className="codex-field-section">
              <div className="inspector-label">{t(group.titleKey)}</div>
              {group.fields.map((field) => (
                <div key={field.key} className="codex-field">
                  <dt>{t(field.labelKey)}</dt>
                  <dd>
                    {field.multiline ? (
                      <MarkdownEditor
                        className="md-compact"
                        minRows={2}
                        placeholder={String(record[field.key] ?? '')}
                        ariaLabel={t(field.labelKey)}
                        value={draft[field.key] ?? ''}
                        onChange={(next) => setDraft({ ...draft, [field.key]: next })}
                      />
                    ) : (
                      <input
                        className="outliner-input codex-field-input"
                        placeholder={String(record[field.key] ?? '')}
                        value={draft[field.key] ?? ''}
                        onChange={(e) => setDraft({ ...draft, [field.key]: e.target.value })}
                      />
                    )}
                  </dd>
                </div>
              ))}
            </div>
          ))}
          {customKeys.length > 0 && (
            <div className="codex-field-section">
              <div className="inspector-label">{t('entityEditor.customProperties')}</div>
              {customKeys.map((key) => (
                <div key={key} className="codex-field">
                  <dt>{key}</dt>
                  <dd>
                    <input
                      className="outliner-input codex-field-input"
                      placeholder={baseCustom[key]}
                      value={customDraft[key] ?? ''}
                      onChange={(e) => setCustomDraft({ ...customDraft, [key]: e.target.value })}
                    />
                  </dd>
                </div>
              ))}
            </div>
          )}
        </div>
        <div className="dialog-actions">
          <button className="dialog-button" onClick={() => setEditing(null)}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" onClick={() => save(scope)}>
            {t('dialog.save')}
          </button>
        </div>
        {pickScope ? (
          <p className="codex-empty overrides-empty">{t('entityEditor.overrideMediaHint')}</p>
        ) : (
          <div className="overrides-media-group">
            <OverrideImages
              selectedId={selectedId}
              scope={scope}
              override={existing}
              baseImages={baseImages}
            />
            <OverrideRelationships
              selectedId={selectedId}
              scope={scope}
              override={existing}
              baseRelationships={baseRelationships}
            />
            <OverrideSections
              selectedId={selectedId}
              scope={scope}
              override={existing}
              baseSections={baseSections}
            />
          </div>
        )}
      </div>
    )
  }
