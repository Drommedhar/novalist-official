import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

interface StateOverride {
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

  const load = useCallback(async () => {
    setOverrides(
      await rpc.request<StateOverride[]>('entities/getStateOverrides', [
        props.entityType,
        props.entityId
      ])
    )
  }, [props.entityType, props.entityId])

  useEffect(() => {
    void load()
  }, [load])

  const save = async (next: StateOverride[]): Promise<void> => {
    setOverrides(next)
    setOverrides(
      await rpc.request<StateOverride[]>('entities/setStateOverrides', [
        props.entityType,
        props.entityId,
        next
      ])
    )
  }

  const edit = (index: number, patch: Partial<StateOverride>): void => {
    setOverrides(overrides.map((o, i) => (i === index ? { ...o, ...patch } : o)))
  }

  const add = (): void => {
    setOverrides([
      ...overrides,
      {
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
        <div key={index} className="state-override">
          <div className="match-row">
            <select
              className="inspector-input"
              value={override.chapter ?? ''}
              onChange={(e) => edit(index, { chapter: e.target.value })}
            >
              {chapters.map((chapter) => (
                <option key={chapter.guid} value={chapter.guid}>
                  {chapter.title}
                </option>
              ))}
            </select>
            <select
              className="inspector-input"
              value={override.scene ?? ''}
              onChange={(e) => edit(index, { scene: e.target.value || null })}
            >
              <option value="">{t('stateOverride.wholeChapter')}</option>
              {(chapters.find((c) => c.guid === override.chapter)?.scenes ?? []).map((scene) => (
                <option key={scene.id} value={scene.title}>
                  {scene.title}
                </option>
              ))}
            </select>
            <button
              className="dialog-button"
              title={t('stateOverride.remove')}
              onClick={() => void save(overrides.filter((_, i) => i !== index))}
            >
              <Trash2 size={14} />
            </button>
          </div>

          <input
            className="inspector-input"
            value={override.name ?? ''}
            placeholder={t('stateOverride.namePlaceholder')}
            onChange={(e) => edit(index, { name: e.target.value || null })}
            onBlur={() => void save(overrides)}
          />
          <textarea
            className="inspector-input"
            value={override.description ?? ''}
            placeholder={t('stateOverride.descriptionPlaceholder')}
            onChange={(e) => edit(index, { description: e.target.value || null })}
            onBlur={() => void save(overrides)}
          />
          <input
            className="inspector-input"
            value={override.note ?? ''}
            placeholder={t('stateOverride.notePlaceholder')}
            onChange={(e) => edit(index, { note: e.target.value || null })}
            onBlur={() => void save(overrides)}
          />
          {/* Novalist tracked what an entry was like at a point and never that
              it had stopped being in the story, so nothing could notice a
              character standing two chapters after their own funeral. */}
          <label className="state-override-gone">
            <input
              type="checkbox"
              checked={override.gone}
              onChange={(e) => {
                edit(index, { gone: e.target.checked })
                void save(
                  overrides.map((o, i) => (i === index ? { ...o, gone: e.target.checked } : o))
                )
              }}
            />
            {t('stateOverride.gone')}
          </label>
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
