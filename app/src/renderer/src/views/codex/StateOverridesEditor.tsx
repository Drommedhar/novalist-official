import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import { useCodexStore } from '../../stores/codexStore'
import { SharedMetadataField, metadataBinding } from '../../collaboration/SharedMetadataField'
import { proseBinding } from '../../collaboration/sharedMarkdown'
import { MarkdownEditor } from '../../shell/MarkdownEditor'

interface StateOverride {
  id?: string
  act: string | null
  chapter: string | null
  scene: string | null
  name: string | null
  description: string | null
  fields: Record<string, string> | null
  note: string | null
  /** From here the entry is out of the story: dead, departed, destroyed. */
  gone: boolean
}

/**
 * What this entry is like at particular points in the story.
 *
 * Characters have had this for a long time; nothing else did, so a city razed
 * in act two could only be described as it is at the end - and a reader of the
 * Codex in chapter three was told the ending.
 */
export function StateOverridesEditor(props: {
  entityType: string
  entityId: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [overrides, setOverrides] = useState<StateOverride[]>([])
  const chapters = useProjectStore((s) => s.chapters)
  const record = useCodexStore(state => state.selectedRecord)

  const previous = useRef<StateOverride[]>([])
  const loadedFor = useRef('')
  useEffect(() => {
    if (record?.id !== props.entityId) {
      let active = true
      void rpc.request<StateOverride[]>('entities/getStateOverrides', [props.entityType, props.entityId])
        .then(rows => { if (active) setOverrides(rows) })
      return () => { active = false }
    }
    const incoming = Array.isArray(record.stateOverrides) ? record.stateOverrides as StateOverride[] : []
    const owner = `${props.entityType}:${props.entityId}`
    if (loadedFor.current !== owner) setOverrides(incoming)
    else {
      const before = previous.current
      setOverrides(current => [
        ...incoming.map(row => {
          const local = current.find(candidate => candidate.id === row.id)
          const saved = before.find(candidate => candidate.id === row.id)
          return local && saved && JSON.stringify(local) !== JSON.stringify(saved) ? local : row
        }),
        ...current.filter(row => !incoming.some(candidate => candidate.id === row.id) && !before.some(candidate => candidate.id === row.id))
      ])
    }
    previous.current = incoming
    loadedFor.current = owner
  }, [props.entityType, props.entityId, record])

  const save = async (next: StateOverride[]): Promise<void> => {
    setOverrides(next)
    const saved = await rpc.request<StateOverride[]>('entities/setStateOverrides', [
        props.entityType,
        props.entityId,
        next.map(row => ({ ...row, description: row.id && proseBinding(record, `state:${row.id}:description`) ? null : row.description,
          note: row.id && proseBinding(record, `state:${row.id}:note`) ? null : row.note }))
      ])
    previous.current = saved
    setOverrides(current => current.map(row => {
      const submitted = next.find(candidate => candidate.id === row.id)
      return submitted && JSON.stringify(submitted) === JSON.stringify(row)
        ? saved.find(candidate => candidate.id === row.id) ?? row : row
    }))
  }

  const edit = (index: number, patch: Partial<StateOverride>): void => {
    setOverrides(overrides.map((o, i) => (i === index ? { ...o, ...patch } : o)))
  }

  const add = (): void => {
    setOverrides([
      ...overrides,
      {
        id: crypto.randomUUID(),
        act: null,
        chapter: chapters[0]?.guid ?? '',
        scene: null,
        name: null,
        description: null,
        fields: null,
        note: null,
        gone: false
      }
    ])
  }

  return (
    <div className="match-settings">
      <div className="match-hint">{t('stateOverride.intro')}</div>

      {overrides.map((override, index) => (
        <div key={override.id ?? index} className="state-override">
          <div className="match-row">
            <SharedMetadataField<string | null> binding={override.id ? metadataBinding(record, `state:${override.id}:chapter`) : undefined}
              value={override.chapter} onChange={next => edit(index, { chapter: next })}>
              {({ value, editable, change }) => (
            <select
              className="inspector-input"
              value={value ?? ''} aria-disabled={!editable}
              onChange={event => change(event.target.value)}
            >
              {chapters.map((chapter) => (
                <option key={chapter.guid} value={chapter.guid}>
                  {chapter.title}
                </option>
              ))}
            </select>
              )}
            </SharedMetadataField>
            <SharedMetadataField<string | null> binding={override.id ? metadataBinding(record, `state:${override.id}:scene`) : undefined}
              value={override.scene} onChange={next => edit(index, { scene: next })}>
              {({ value, editable, change }) => (
            <select
              className="inspector-input"
              value={value ?? ''} aria-disabled={!editable}
              onChange={event => change(event.target.value || null)}
            >
              <option value="">{t('stateOverride.wholeChapter')}</option>
              {(chapters.find((c) => c.guid === override.chapter)?.scenes ?? []).map((scene) => (
                <option key={scene.id} value={scene.title}>
                  {scene.title}
                </option>
              ))}
            </select>
              )}
            </SharedMetadataField>
            <button
              className="dialog-button"
              title={t('stateOverride.remove')}
              onClick={() => void save(overrides.filter((_, i) => i !== index))}
            >
              <Trash2 size={14} />
            </button>
          </div>

          <SharedMetadataField<string | null> binding={override.id ? metadataBinding(record, `state:${override.id}:name`) : undefined}
            value={override.name} onChange={next => edit(index, { name: next })}>
            {({ value, editable, change }) => <input className="inspector-input" value={value ?? ''} readOnly={!editable}
              placeholder={t('stateOverride.namePlaceholder')} onChange={event => change(event.target.value || null)}
              onBlur={() => { if (!override.id || !metadataBinding(record, `state:${override.id}:name`)) void save(overrides) }} />}
          </SharedMetadataField>
          {(['description', 'note'] as const).map(key => <MarkdownEditor key={key}
            shared={override.id ? proseBinding(record, `state:${override.id}:${key}`) : undefined}
            value={override[key] ?? ''} placeholder={t(`stateOverride.${key}Placeholder`)}
            onChange={next => edit(index, { [key]: next || null })}
            onBlur={() => { if (!override.id || !proseBinding(record, `state:${override.id}:${key}`)) void save(overrides) }} />)}
          {/* Novalist tracked what an entry was like at a point and never that
              it had stopped being in the story, so nothing could notice a
              character standing two chapters after their own funeral. */}
          <SharedMetadataField<boolean | null> binding={override.id ? metadataBinding(record, `state:${override.id}:gone`) : undefined}
            value={override.gone} onChange={next => void save(overrides.map((row, i) => i === index ? { ...row, gone: next ?? false } : row))}>
            {({ value, editable, change }) => <label className="state-override-gone">
              <input type="checkbox" checked={value ?? false} aria-disabled={!editable} onChange={event => change(event.target.checked)} />
              {t('stateOverride.gone')}
            </label>}
          </SharedMetadataField>
        </div>
      ))}

      <div className="match-row">
        <button className="dialog-button" disabled={chapters.length === 0} onClick={add}>
          <Plus size={14} /> {t('stateOverride.add')}
        </button>
      </div>
    </div>
  )
}
