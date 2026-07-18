import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useCodexStore } from '../../stores/codexStore'
import { useProjectStore } from '../../stores/projectStore'

interface CharacterOverride {
  act: string | null
  chapter: string
  scene: string | null
  [field: string]: unknown
}

const OVERRIDABLE = ['name', 'surname', 'role', 'age', 'eyeColor', 'hairColor', 'height', 'build']

/** Per-chapter/scene character overrides: diff-stored, blank = inherit. */
export function OverridesEditor(): React.JSX.Element | null {
  const { t } = useTranslation()
  const entityType = useCodexStore((s) => s.entityType)
  const selectedId = useCodexStore((s) => s.selectedId)
  const record = useCodexStore((s) => s.selectedRecord)
  const chapters = useProjectStore((s) => s.chapters)
  const [editing, setEditing] = useState<{ chapter: string; scene: string | null } | null>(null)
  const [draft, setDraft] = useState<Record<string, string>>({})

  if (entityType !== 'character' || !record || !selectedId) return null
  const overrides = Array.isArray(record.chapterOverrides)
    ? (record.chapterOverrides as CharacterOverride[])
    : []

  const chapterTitle = (guid: string): string =>
    chapters.find((c) => c.guid === guid)?.title ?? guid

  const apply = (updated: Record<string, unknown>): void => {
    useCodexStore.setState({ selectedRecord: updated })
  }

  const openEditor = (target: { chapter: string; scene: string | null }): void => {
    const existing = overrides.find(
      (o) => o.chapter === target.chapter && (o.scene ?? null) === target.scene
    )
    const values: Record<string, string> = {}
    for (const field of OVERRIDABLE) {
      const value = existing?.[field]
      values[field] = typeof value === 'string' ? value : ''
    }
    setDraft(values)
    setEditing(target)
  }

  return (
    <div className="entity-lists">
      <div className="inspector-label">{t('entityEditor.chapterOverrides')}</div>
      {overrides.map((over) => (
        <div key={`${over.chapter}|${over.scene ?? ''}`} className="entity-rel-row">
          <button
            className="binder-scene-title overrides-scope"
            onClick={() => openEditor({ chapter: over.chapter, scene: over.scene ?? null })}
          >
            {chapterTitle(over.chapter)}
            {over.scene ? ` - ${over.scene}` : ''}
          </button>
          <span className="codex-row-detail">
            {OVERRIDABLE.filter((f) => typeof over[f] === 'string').join(', ')}
          </span>
          <button
            className="binder-expand"
            aria-label={t('explorer.contextDelete')}
            onClick={() =>
              void rpc
                .request<Record<string, unknown>>('entities/removeOverride', [
                  selectedId,
                  over.chapter,
                  over.scene ?? null
                ])
                .then(apply)
            }
          >
            <X size={12} strokeWidth={2} />
          </button>
        </div>
      ))}
      {chapters.length > 0 && (
        <button
          className="binder-rail-item"
          onClick={() => openEditor({ chapter: chapters[0].guid, scene: null })}
        >
          <Plus size={13} strokeWidth={2} />
          {t('entityEditor.addOverride')}
        </button>
      )}
      {editing && (
        <div
          className="dialog-overlay"
          onPointerDown={(e) => e.target === e.currentTarget && setEditing(null)}
        >
          <div className="dialog-card" role="dialog" aria-label={t('entityEditor.chapterOverrides')}>
            <div className="dialog-title">{t('entityEditor.chapterOverrides')}</div>
            <select
              className="dialog-input"
              value={editing.chapter}
              onChange={(e) => setEditing({ chapter: e.target.value, scene: null })}
            >
              {chapters.map((c) => (
                <option key={c.guid} value={c.guid}>
                  {c.title}
                </option>
              ))}
            </select>
            <select
              className="dialog-input"
              value={editing.scene ?? ''}
              onChange={(e) => setEditing({ ...editing, scene: e.target.value || null })}
            >
              <option value="">{t('entityEditor.wholeChapter')}</option>
              {(chapters.find((c) => c.guid === editing.chapter)?.scenes ?? []).map((s) => (
                <option key={s.id} value={s.title}>
                  {s.title}
                </option>
              ))}
            </select>
            {OVERRIDABLE.map((field) => (
              <div key={field} className="codex-field">
                <dt>{field}</dt>
                <dd>
                  <input
                    className="outliner-input codex-field-input"
                    placeholder={String(record[field] ?? '')}
                    value={draft[field] ?? ''}
                    onChange={(e) => setDraft({ ...draft, [field]: e.target.value })}
                  />
                </dd>
              </div>
            ))}
            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setEditing(null)}>
                {t('dialog.cancel')}
              </button>
              <button
                className="dialog-button primary"
                onClick={() => {
                  const target = editing
                  setEditing(null)
                  void rpc
                    .request<Record<string, unknown>>('entities/setOverride', [
                      selectedId,
                      target.chapter,
                      target.scene,
                      draft
                    ])
                    .then(apply)
                }}
              >
                {t('dialog.save')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
