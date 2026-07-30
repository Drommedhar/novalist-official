import { useEffect, useRef, useState } from 'react'
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
  /**
   * What kind of tie this is. The graph colours by it, and it was previously
   * guessed from keywords in the role - which only ever worked in English.
   */
  category?: string
  inverseRole?: string
}

/** The kinds the picker offers. Blank is allowed: not every tie has a kind. */
const TIE_KINDS = ['', 'family', 'ally', 'rival', 'member', 'owner', 'place']

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
  const [relationships, setRelationshipsState] = useState<RelationshipRow[]>([])
  /**
   * The rows as they stand right now, for the blur handlers.
   *
   * Every input persisted `relationships` from the closure it was created in.
   * A keystroke calls setState, which does not update that closure until React
   * has re-rendered - so typing a target and immediately clicking away saved
   * the array from *before* the typing, and the value was gone. Only the field
   * blurred a second time ever survived.
   */
  const rowsRef = useRef<RelationshipRow[]>([])
  const setRelationships = (next: RelationshipRow[]): void => {
    rowsRef.current = next
    setRelationshipsState(next)
  }
  const [nameSuggestions, setNameSuggestions] = useState<string[]>([])
  const [roleSuggestions, setRoleSuggestions] = useState<string[]>([])

  /**
   * The entry these fields were last loaded for.
   *
   * Saving echoes the stored record back into the store, which re-ran this
   * effect and overwrote the fields from it. Blurring the role field to reach
   * the target field saved the role, and the reply - which has no target yet -
   * landed on top of the target being typed. The value never reached disk, on
   * any entry type. Reloading once per entry leaves editing alone.
   */
  const loadedFor = useRef<string | null>(null)

  useEffect(() => {
    if (!record || !selectedId) return
    if (loadedFor.current === selectedId) return
    loadedFor.current = selectedId
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
    // Every type that carries relationships, not characters alone: a location
    // with no name suggestions is a relationship row nobody fills in.
    if (!RELATIONSHIP_TYPES.includes(entityType)) return
    void rpc
      .request<{ characterNames: string[]; roles: string[] }>('entities/relationshipSuggestions')
      .then((s) => {
        setNameSuggestions(s.characterNames)
        setRoleSuggestions(s.roles)
      })
      .catch(() => {})
  }, [entityType, selectedId])

  /**
   * Saves run one after another, never at the same time.
   *
   * Every field's blur saves the whole row set, and moving across a row blurs
   * three fields in a row. Those saves used to be in flight together, and each
   * one writes the subject and then the other end of every tie it names - so
   * two of them interleaving could write the reciprocal from the older set and
   * lose the one the writer had just finished typing. Queueing them costs
   * nothing here and makes the order the writer's, not the network's.
   */
  const saveQueue = useRef<Promise<unknown>>(Promise.resolve())

  const persistRelationships = (next: RelationshipRow[]): void => {
    if (!selectedId) return
    saveQueue.current = saveQueue.current
      .catch(() => {})
      .then(() =>
        rpc.request<Record<string, unknown>>('entities/setRelationships', [
          selectedId,
          next.map((r) => ({
            role: r.role,
            target: r.target,
            inverseRole: r.inverseRole ?? '',
            category: r.category ?? ''
          })),
          // Without this the backend falls back to "character", so saving a tie
          // on a location, an item or a piece of lore looked for a character with
          // that id, found none, and threw. The write-back stopped being
          // character-only; the call never said so.
          entityType
        ])
      )
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
            // Built from the live rows too, so two edits landing before a
            // re-render do not overwrite each other - which is what happened
            // when the inverse role arrived from the backend mid-typing.
            const patch = (p: Partial<RelationshipRow>): void =>
              setRelationships(rowsRef.current.map((r, i) => (i === index ? { ...r, ...p } : r)))
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
                    persistRelationships(rowsRef.current)
                  }}
                />
                <input
                  className="outliner-input"
                  list="codex-rel-names"
                  placeholder={t('entityEditor.targetNames')}
                  value={rel.target}
                  onChange={(e) => patch({ target: e.target.value })}
                  onBlur={() => persistRelationships(rowsRef.current)}
                />
                <select
                  className="outliner-input codex-rel-kind"
                  aria-label={t('entityEditor.tieKind')}
                  value={rel.category ?? ''}
                  onChange={(e) => {
                    const next = relationships.map((r, i) =>
                      i === index ? { ...r, category: e.target.value } : r
                    )
                    setRelationships(next)
                    persistRelationships(next)
                  }}
                >
                  {TIE_KINDS.map((kind) => (
                    <option key={kind} value={kind}>
                      {t(`entityEditor.tieKind${kind}`)}
                    </option>
                  ))}
                </select>
                <input
                  className="outliner-input codex-rel-inverse"
                  placeholder={t('entityEditor.inverseRole')}
                  list="codex-rel-roles"
                  value={rel.inverseRole ?? ''}
                  onChange={(e) => patch({ inverseRole: e.target.value })}
                  onBlur={() => persistRelationships(rowsRef.current)}
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
            className="binder-rail-item codex-add-relationship"
            onClick={() =>
              setRelationships([
                ...relationships,
                { role: '', target: '', category: '', inverseRole: '' }
              ])
            }
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
