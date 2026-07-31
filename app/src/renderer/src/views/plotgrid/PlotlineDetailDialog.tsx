import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RevisionsPanel } from '../../shell/RevisionsPanel'
import { Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'

export interface PlotlineStep {
  id: string
  text: string
  sceneId?: string | null
  resolved: boolean
  order: number
}

export interface Plotline {
  id: string
  name: string
  color: string
  order: number
  importance: string
  description: string
  castIds: string[]
  steps: PlotlineStep[]
  unresolvedSteps: number
}

interface EntityOption {
  id: string
  name: string
  type: string
}

const IMPORTANCES = ['Main', 'Subplot', 'Minor']
const CAST_TYPES = ['character', 'location', 'item', 'lore']

/**
 * A thread as an object rather than a row of ticks.
 *
 * The grid answers "is this thread in this scene" and can answer nothing else.
 * It cannot say which thread is the spine and which is a running joke, whose
 * story a thread is, or - the question a revision actually asks - whether the
 * thread ever resolves. All three were already in the model and in the backend;
 * nothing called them.
 */
export function PlotlineDetailDialog(props: {
  plotline: Plotline
  onClose: () => void
  onSaved: (grid: unknown) => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [importance, setImportance] = useState(props.plotline.importance || 'Subplot')
  const [color, setColor] = useState(props.plotline.color)
  const [description, setDescription] = useState(props.plotline.description ?? '')
  const [castIds, setCastIds] = useState<string[]>(props.plotline.castIds ?? [])
  const [steps, setSteps] = useState<PlotlineStep[]>(props.plotline.steps ?? [])
  const [entities, setEntities] = useState<EntityOption[]>([])
  const [query, setQuery] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    void Promise.all(
      CAST_TYPES.map(async (key) =>
        (await rpc.request<{ id: string; name: string }[]>('entities/list', [key]).catch(() => []))
          .map((e) => ({ ...e, type: key }))
      )
    ).then((lists) => setEntities(lists.flat()))
  }, [])

  const byId = useMemo(() => new Map(entities.map((e) => [e.id, e] as const)), [entities])

  const needle = query.trim().toLocaleLowerCase()
  const matches =
    needle.length === 0
      ? []
      : entities
          .filter((e) => !castIds.includes(e.id))
          .filter((e) => e.name.toLocaleLowerCase().includes(needle))
          .slice(0, 8)

  const unresolved = steps.filter((s) => !s.resolved).length

  const save = async (): Promise<void> => {
    setSaving(true)
    const grid = await rpc.request('plot/setPlotlineDetail', [
      props.plotline.id,
      importance,
      castIds,
      steps.map((step, index) => ({ ...step, order: index })),
      color,
      description
    ])
    props.onSaved(grid)
    props.onClose()
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && props.onClose()}
    >
      <div className="dialog-card plotline-detail" role="dialog" aria-label={props.plotline.name}>
        <span className="dialog-title">{props.plotline.name}</span>

        <div className="plotline-detail-row">
          <label className="inspector-label" htmlFor="plotline-importance">
            {t('plotGrid.importance')}
          </label>
          <select
            id="plotline-importance"
            className="inspector-input"
            value={importance}
            onChange={(e) => setImportance(e.target.value)}
          >
            {IMPORTANCES.map((value) => (
              <option key={value} value={value}>
                {t(`plotGrid.importance${value}`)}
              </option>
            ))}
          </select>
        </div>

        <div className="plotline-detail-row">
          <label className="inspector-label" htmlFor="plotline-colour">
            {t('plotGrid.colour')}
          </label>
          <input
            id="plotline-colour"
            type="color"
            className="inspector-input plotline-colour"
            value={color}
            onChange={(e) => setColor(e.target.value)}
          />
        </div>

        <label className="inspector-label" htmlFor="plotline-description">
          {t('plotGrid.description')}
        </label>
        <textarea
          id="plotline-description"
          className="inspector-input plotline-description"
          value={description}
          placeholder={t('plotGrid.descriptionPlaceholder')}
          onChange={(e) => setDescription(e.target.value)}
        />

        <div className="inspector-label">{t('plotGrid.cast')}</div>
        <div className="scene-cast-chips">
          {castIds.map((id) => (
            <span key={id} className="scene-cast-chip">
              {/* An entry deleted from the Codex leaves its id behind rather than
                  vanishing silently, so it can be seen and removed. */}
              <span className="scene-cast-name">{byId.get(id)?.name ?? id}</span>
              <button
                className="scene-cast-remove"
                title={t('cast.remove')}
                onClick={() => setCastIds(castIds.filter((c) => c !== id))}
              >
                <X size={11} strokeWidth={2} />
              </button>
            </span>
          ))}
        </div>
        <input
          className="inspector-input"
          value={query}
          placeholder={t('plotGrid.castPlaceholder')}
          onChange={(e) => setQuery(e.target.value)}
        />
        {matches.length > 0 && (
          <div className="scene-cast-matches">
            {matches.map((entity) => (
              <button
                key={entity.id}
                className="scene-cast-match"
                onClick={() => {
                  setCastIds([...castIds, entity.id])
                  setQuery('')
                }}
              >
                {entity.name}
              </button>
            ))}
          </div>
        )}

        <div className="inspector-label">{t('plotGrid.steps')}</div>
        {/* Zero unresolved with no steps means nothing was planned, not that
            everything is done. Saying so is the whole value of the count. */}
        <p className="inspector-meta">
          {steps.length === 0
            ? t('plotGrid.nothingPlanned')
            : t('plotGrid.unresolvedCount', { count: unresolved })}
        </p>
        {steps.map((step, index) => (
          <div key={step.id} className="plotline-step">
            <input
              type="checkbox"
              checked={step.resolved}
              aria-label={t('plotGrid.resolved')}
              onChange={(e) =>
                setSteps(
                  steps.map((s, i) => (i === index ? { ...s, resolved: e.target.checked } : s))
                )
              }
            />
            <input
              className="inspector-input"
              value={step.text}
              placeholder={t('plotGrid.stepPlaceholder')}
              onChange={(e) =>
                setSteps(steps.map((s, i) => (i === index ? { ...s, text: e.target.value } : s)))
              }
            />
            <button
              className="scene-cast-remove"
              title={t('plotGrid.removeStep')}
              onClick={() => setSteps(steps.filter((_, i) => i !== index))}
            >
              <X size={11} strokeWidth={2} />
            </button>
          </div>
        ))}
        <button
          className="btn-secondary plotline-add-step"
          onClick={() =>
            setSteps([
              ...steps,
              {
                id: `new-${steps.length}-${props.plotline.id}`,
                text: '',
                resolved: false,
                order: steps.length
              }
            ])
          }
        >
          <Plus size={12} strokeWidth={2} />
          {t('plotGrid.addStep')}
        </button>

        {/* Threads had no earlier versions while Codex entries did, so typing
            over a thread's description or its steps had no answer in the app. */}
        <div className="inspector-label">{t('entityHistory.title')}</div>
        <RevisionsPanel
          historyMethod="plot/plotlineHistory"
          restoreMethod="plot/restorePlotlineRevision"
          targetId={props.plotline.id}
          restoreArgs={[props.plotline.id]}
          onRestored={(grid) => {
            props.onSaved(grid)
            props.onClose()
          }}
        />

        <div className="dialog-actions">
          <button className="dialog-button" onClick={props.onClose}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" disabled={saving} onClick={() => void save()}>
            {t('dialog.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
