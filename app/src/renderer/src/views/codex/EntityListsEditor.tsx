import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { MarkdownEditor } from '../../shell/MarkdownEditor'

interface SectionRow {
  title: string
  content: string
}

interface RelationshipRow {
  role: string
  target: string
  inverseRole?: string
}

/** Built-in types that carry relationships. Custom types manage their own. */
const RELATIONSHIP_TYPES = ['character', 'location', 'item', 'lore']

/** Aliases, sections, and relationships of the selected entity. */
export function EntityListsEditor(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const [aliases, setAliases] = useState<string[]>([])
  const [aliasDraft, setAliasDraft] = useState('')
  const [sections, setSections] = useState<SectionRow[]>([])
  const [relationships, setRelationships] = useState<RelationshipRow[]>([])
  const [nameSuggestions, setNameSuggestions] = useState<string[]>([])
  const [roleSuggestions, setRoleSuggestions] = useState<string[]>([])

  useEffect(() => {
    if (!record) return
    setAliases(Array.isArray(record.aliases) ? (record.aliases as string[]) : [])
    setSections(
      Array.isArray(record.sections) ? (record.sections as SectionRow[]).map((s) => ({ ...s })) : []
    )
    setRelationships(
      Array.isArray(record.relationships)
        ? (record.relationships as RelationshipRow[]).map((r) => ({ ...r }))
        : []
    )
  }, [selectedId, record])

  useEffect(() => {
    if (entityType !== 'character') return
    void rpc
      .request<{ characterNames: string[]; roles: string[] }>('entities/relationshipSuggestions')
      .then((s) => {
        setNameSuggestions(s.characterNames)
        setRoleSuggestions(s.roles)
      })
      .catch(() => {})
  }, [entityType, selectedId])

  const persistRelationships = (next: RelationshipRow[]): void => {
    if (!selectedId) return
    void rpc
      .request<Record<string, unknown>>('entities/setRelationships', [
        selectedId,
        next.map((r) => ({ role: r.role, target: r.target, inverseRole: r.inverseRole ?? '' }))
      ])
      .then((updated) => useCodexStore.setState({ selectedRecord: updated }))
  }

  if (!record || !selectedId) return null

  const persist = (
    nextAliases: string[] | null,
    nextSections: SectionRow[] | null,
    nextRelationships: RelationshipRow[] | null
  ): void => {
    void rpc
      .request<Record<string, unknown>>('entities/updateLists', [
        entityType,
        selectedId,
        nextAliases,
        nextSections,
        nextRelationships
      ])
      .then((updated) => useCodexStore.setState({ selectedRecord: updated }))
  }

  const addAlias = (): void => {
    const value = aliasDraft.trim()
    if (!value || aliases.includes(value)) return
    const next = [...aliases, value]
    setAliases(next)
    setAliasDraft('')
    persist(next, null, null)
  }

  return (
    <div className="entity-lists">
      <div className="inspector-label">{t('entityEditor.aliases')}</div>
      <div className="entity-chips">
        {aliases.map((alias) => (
          <span key={alias} className="entity-chip">
            {alias}
            <button
              aria-label={`${t('explorer.contextDelete')} ${alias}`}
              onClick={() => {
                const next = aliases.filter((a) => a !== alias)
                setAliases(next)
                persist(next, null, null)
              }}
            >
              <X size={11} strokeWidth={2} />
            </button>
          </span>
        ))}
        <input
          className="entity-chip-input"
          value={aliasDraft}
          placeholder={t('entityEditor.addAlias')}
          onChange={(e) => setAliasDraft(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && addAlias()}
          onBlur={addAlias}
        />
      </div>

      {RELATIONSHIP_TYPES.includes(entityType) && (
        <>
          <div className="inspector-label">{t('entityEditor.relationships')}</div>
          <datalist id="codex-rel-names">
            {nameSuggestions.map((n) => (
              <option key={n} value={n} />
            ))}
          </datalist>
          <datalist id="codex-rel-roles">
            {roleSuggestions.map((r) => (
              <option key={r} value={r} />
            ))}
          </datalist>
          {relationships.map((rel, index) => {
            const patch = (p: Partial<RelationshipRow>): void =>
              setRelationships(relationships.map((r, i) => (i === index ? { ...r, ...p } : r)))
            return (
              <div key={index} className="entity-rel-row">
                <input
                  className="outliner-input"
                  list="codex-rel-roles"
                  placeholder={t('entityEditor.rolePlaceholderRel')}
                  value={rel.role}
                  onChange={(e) => patch({ role: e.target.value })}
                  onBlur={() => {
                    if (rel.role.trim() && !rel.inverseRole) {
                      void rpc
                        .request<string>('entities/inverseRole', [rel.role.trim()])
                        .then((inv) => {
                          if (inv) patch({ inverseRole: inv })
                        })
                    }
                    persistRelationships(relationships)
                  }}
                />
                <input
                  className="outliner-input"
                  list="codex-rel-names"
                  placeholder={t('entityEditor.targetNames')}
                  value={rel.target}
                  onChange={(e) => patch({ target: e.target.value })}
                  onBlur={() => persistRelationships(relationships)}
                />
                <input
                  className="outliner-input codex-rel-inverse"
                  placeholder={t('entityEditor.inverseRole')}
                  list="codex-rel-roles"
                  value={rel.inverseRole ?? ''}
                  onChange={(e) => patch({ inverseRole: e.target.value })}
                  onBlur={() => persistRelationships(relationships)}
                />
                <button
                  className="binder-expand"
                  aria-label={t('explorer.contextDelete')}
                  onClick={() => {
                    const next = relationships.filter((_, i) => i !== index)
                    setRelationships(next)
                    persistRelationships(next)
                  }}
                >
                  <X size={12} strokeWidth={2} />
                </button>
              </div>
            )
          })}
          <button
            className="binder-rail-item"
            onClick={() => setRelationships([...relationships, { role: '', target: '', inverseRole: '' }])}
          >
            <Plus size={13} strokeWidth={2} />
            {t('entityEditor.addRelationship')}
          </button>
        </>
      )}

      <div className="inspector-label">{t('entityEditor.sections')}</div>
      {sections.map((section, index) => (
        <div key={index} className="entity-section">
          <div className="entity-section-head">
            <input
              className="outliner-input entity-section-title"
              value={section.title}
              onChange={(e) =>
                setSections(
                  sections.map((s, i) => (i === index ? { ...s, title: e.target.value } : s))
                )
              }
              onBlur={() => persist(null, sections, null)}
            />
            <button
              className="binder-expand"
              aria-label={t('explorer.contextDelete')}
              onClick={() => {
                const next = sections.filter((_, i) => i !== index)
                setSections(next)
                persist(null, next, null)
              }}
            >
              <X size={12} strokeWidth={2} />
            </button>
          </div>
          <MarkdownEditor
            value={section.content}
            ariaLabel={section.title}
            onChange={(next) =>
              setSections(sections.map((s, i) => (i === index ? { ...s, content: next } : s)))
            }
            onBlur={() => persist(null, sections, null)}
          />
        </div>
      ))}
      <button
        className="binder-rail-item"
        onClick={() => setSections([...sections, { title: t('section.newSection'), content: '' }])}
      >
        <Plus size={13} strokeWidth={2} />
        {t('entityEditor.addSection')}
      </button>
    </div>
  )
}
