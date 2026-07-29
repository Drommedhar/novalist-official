import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

interface ArcPoint {
  id: string
  sceneId: string
  label: string
}

interface Arc {
  start: string
  end: string
  points: ArcPoint[]
}

/**
 * Where a character starts, where they end, and the scenes that turn them.
 *
 * Per-scope overrides already say what a character is *like* at a point in the
 * book. An arc says what the change is for - and which scene is the one where
 * it happens, which nothing could record.
 */
export function ArcEditor(props: { characterId: string }): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const [arc, setArc] = useState<Arc>({ start: '', end: '', points: [] })

  const load = useCallback(async () => {
    setArc(await rpc.request<Arc>('arcs/get', [props.characterId]))
  }, [props.characterId])

  useEffect(() => {
    void load()
  }, [load])

  const save = (next: Arc): void => {
    setArc(next)
    void rpc
      .request<Arc>('arcs/save', [props.characterId, next.start, next.end, next.points])
      .then(setArc)
  }

  const scenes = chapters.flatMap((chapter) =>
    chapter.scenes.map((scene) => ({
      id: scene.id,
      label: `${chapter.title} - ${scene.title}`
    }))
  )

  return (
    <div className="match-settings">
      <div className="match-hint">{t('arc.intro')}</div>

      <label className="inspector-label">{t('arc.start')}</label>
      <textarea
        className="inspector-input"
        placeholder={t('arc.startPlaceholder')}
        defaultValue={arc.start}
        key={`start-${arc.start}`}
        onBlur={(e) => save({ ...arc, start: e.target.value })}
      />

      <label className="inspector-label">{t('arc.end')}</label>
      <textarea
        className="inspector-input"
        placeholder={t('arc.endPlaceholder')}
        defaultValue={arc.end}
        key={`end-${arc.end}`}
        onBlur={(e) => save({ ...arc, end: e.target.value })}
      />

      <label className="inspector-label">{t('arc.points')}</label>
      {arc.points.map((point, index) => (
        <div key={point.id} className="match-row">
          <input
            className="inspector-input"
            placeholder={t('arc.pointPlaceholder')}
            defaultValue={point.label}
            onBlur={(e) =>
              save({
                ...arc,
                points: arc.points.map((p, i) =>
                  i === index ? { ...p, label: e.target.value } : p
                )
              })
            }
          />
          {/* A turning point can be written down before the writer knows which
              scene it happens in; that is half the use of writing it down. */}
          <select
            className="inspector-input"
            aria-label={t('arc.scene')}
            value={point.sceneId}
            onChange={(e) =>
              save({
                ...arc,
                points: arc.points.map((p, i) =>
                  i === index ? { ...p, sceneId: e.target.value } : p
                )
              })
            }
          >
            <option value="">{t('arc.noScene')}</option>
            {scenes.map((scene) => (
              <option key={scene.id} value={scene.id}>
                {scene.label}
              </option>
            ))}
          </select>
          <button
            className="dialog-button danger"
            title={t('arc.remove')}
            onClick={() => save({ ...arc, points: arc.points.filter((_, i) => i !== index) })}
          >
            <Trash2 size={14} />
          </button>
        </div>
      ))}

      <div className="match-row">
        <button
          className="dialog-button"
          onClick={() =>
            setArc({
              ...arc,
              points: [...arc.points, { id: '', sceneId: '', label: '' }]
            })
          }
        >
          <Plus size={14} /> {t('arc.addPoint')}
        </button>
      </div>
    </div>
  )
}
