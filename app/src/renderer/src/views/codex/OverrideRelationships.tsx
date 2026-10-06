import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { type OverrideRelationship, type CharacterOverride, type Scope, apply } from './overrideModel'
import { OverrideMediaHeader } from './OverrideMediaHeader'

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
  const overriding = Array.isArray(override?.relationships)
  const [rows, setRows] = useState<OverrideRelationship[]>([])
  const [nameSuggestions, setNameSuggestions] = useState<string[]>([])
  const [roleSuggestions, setRoleSuggestions] = useState<string[]>([])

  const effective = overriding ? (override!.relationships as OverrideRelationship[]) : baseRelationships

  useEffect(() => {
    setRows(effective.map((r) => ({ ...r })))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId, scope.chapter, scope.scene, overriding, JSON.stringify(effective)])

  useEffect(() => {
    void rpc
      .request<{ characterNames: string[]; roles: string[] }>('entities/relationshipSuggestions')
      .then((s) => {
        setNameSuggestions(s.characterNames)
        setRoleSuggestions(s.roles)
      })
      .catch(() => {})
  }, [selectedId])

  const persist = (next: OverrideRelationship[] | null): void => {
    void rpc
      .request<Record<string, unknown>>('entities/setOverrideRelationships', [
        selectedId,
        scope.chapter,
        scope.scene,
        next?.map((r) => ({ role: r.role, target: r.target })) ?? null
      ])
      .then(apply)
  }

  return (
    <div className="overrides-media">
      <OverrideMediaHeader
        labelKey="entityEditor.relationships"
        overriding={overriding}
        onReset={() => persist(null)}
      />
      <datalist id="codex-override-rel-names">
        {nameSuggestions.map((n) => (
          <option key={n} value={n} />
        ))}
      </datalist>
      <datalist id="codex-override-rel-roles">
        {roleSuggestions.map((r) => (
          <option key={r} value={r} />
        ))}
      </datalist>
      {rows.map((rel, index) => {
        const patch = (p: Partial<OverrideRelationship>): void =>
          setRows(rows.map((r, i) => (i === index ? { ...r, ...p } : r)))
        return (
          <div key={index} className="entity-rel-row">
            <input
              className="outliner-input"
              list="codex-override-rel-roles"
              placeholder={t('entityEditor.rolePlaceholderRel')}
              value={rel.role}
              onChange={(e) => patch({ role: e.target.value })}
              onBlur={() => persist(rows)}
            />
            <input
              className="outliner-input"
              list="codex-override-rel-names"
              placeholder={t('entityEditor.targetNames')}
              value={rel.target}
              onChange={(e) => patch({ target: e.target.value })}
              onBlur={() => persist(rows)}
            />
            <button
              className="binder-expand"
              aria-label={t('explorer.contextDelete')}
              onClick={() => {
                const next = rows.filter((_, i) => i !== index)
                setRows(next)
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
        onClick={() => setRows([...rows, { role: '', target: '' }])}
      >
        <Plus size={13} strokeWidth={2} />
        {t('entityEditor.addRelationship')}
      </button>
    </div>
  )
}
