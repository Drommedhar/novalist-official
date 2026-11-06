import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { type OverrideRelationship, type CharacterOverride, type Scope, apply } from './overrideModel'
import { OverrideMediaHeader } from './OverrideMediaHeader'
import { useEntityFieldSuggestions } from './useEntityFieldSuggestions'
import { useCodexStore } from '../../stores/codexStore'
import { SharedMetadataField, metadataBinding } from '../../collaboration/SharedMetadataField'

/** Per-scope override relationships editor. Mirrors the relationships block of
 * EntityListsEditor.tsx but persists to the override scope via
 * entities/setOverrideRelationships (null resets to inherit the base list). */
export function OverrideRelationships({
  selectedId,
  scope,
  override,
  baseRelationships
}: {
  selectedId: string
  scope: Scope
  override: CharacterOverride | undefined
  baseRelationships: OverrideRelationship[]
}): React.JSX.Element {
  const { t } = useTranslation()
  const record = useCodexStore(state => state.selectedRecord)
  const binding = metadataBinding(record, `override:${scope.chapter}:${scope.scene ?? ''}:relationships`)
  const overriding = Array.isArray(override?.relationships)
  const [rows, setRows] = useState<OverrideRelationship[]>([])
  const suggestions = useEntityFieldSuggestions('character', JSON.stringify([selectedId, scope.chapter, scope.scene]))

  const effective = overriding ? (override!.relationships as OverrideRelationship[]) : baseRelationships

  useEffect(() => {
    setRows(effective.map((r) => ({ ...r })))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId, scope.chapter, scope.scene, overriding, JSON.stringify(effective)])

  const persistUnshared = (next: OverrideRelationship[] | null): void => {
    void rpc
      .request<Record<string, unknown>>('entities/setOverrideRelationships', [
        selectedId,
        scope.chapter,
        scope.scene,
        next?.map((r) => ({ role: r.role, target: r.target })) ?? null
      ])
      .then(apply)
  }

  return <SharedMetadataField<OverrideRelationship[] | null> binding={binding}
    value={binding ? override?.relationships ?? null : rows} onChange={next => setRows(next ?? baseRelationships)}>
    {({ value, editable, change }) => {
      const rows = value ?? baseRelationships
      const overriding = value !== null
      const persist = (next: OverrideRelationship[] | null): void => { if (!binding) persistUnshared(next) }
      return (
    <div className="overrides-media">
      <OverrideMediaHeader
        labelKey="entityEditor.relationships"
        overriding={overriding}
        onReset={() => { change(null); if (!binding) persistUnshared(null) }}
      />
      <datalist id="codex-override-rel-names">
        {(suggestions.names.relationshipNames ?? []).map((n) => (
          <option key={n} value={n} />
        ))}
      </datalist>
      <datalist id="codex-override-rel-roles">
        {(suggestions.names.relationshipRoles ?? []).map((r) => (
          <option key={r} value={r} />
        ))}
      </datalist>
      {rows.map((rel, index) => {
        const patch = (p: Partial<OverrideRelationship>): void =>
          change(current => (current ?? baseRelationships).map((r, i) => i === index ? { ...r, ...p } : r))
        return (
          <div key={index} className="entity-rel-row">
            <input
              className="outliner-input"
              list="codex-override-rel-roles"
              onFocus={suggestions.loadRelationships}
              title={suggestions.errors.relationships}
              placeholder={t('entityEditor.rolePlaceholderRel')}
              value={rel.role} readOnly={!editable}
              onChange={(e) => patch({ role: e.target.value })}
              onBlur={() => persist(rows)}
            />
            <input
              className="outliner-input"
              list="codex-override-rel-names"
              onFocus={suggestions.loadRelationships}
              title={suggestions.errors.relationships}
              placeholder={t('entityEditor.targetNames')}
              value={rel.target} readOnly={!editable}
              onChange={(e) => patch({ target: e.target.value })}
              onBlur={() => persist(rows)}
            />
            <button
              className="binder-expand"
              aria-label={t('explorer.contextDelete')}
              onClick={() => {
                const next = rows.filter((_, i) => i !== index)
                change(next)
                persist(next)
              }}
            >
              <X size={12} strokeWidth={2} />
            </button>
          </div>
        )
      })}
      <button
        className="binder-rail-item"
        onClick={() => change(current => [...(current ?? baseRelationships), { role: '', target: '' }])}
      >
        <Plus size={13} strokeWidth={2} />
        {t('entityEditor.addRelationship')}
      </button>
    </div>
      )
    }}
  </SharedMetadataField>
}
