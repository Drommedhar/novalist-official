import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RevisionsPanel } from '../../shell/RevisionsPanel'
import { useUnsavedGuard } from '../../shell/useUnsavedGuard'
import { ArrowLeft, Plus, X } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'

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

/** Everything an edit can change, in a form two states can be compared in. */
function fingerprint(
  importance: string,
  color: string,
  description: string,
  castIds: string[],
  steps: PlotlineStep[]
): string {
  return JSON.stringify([
    importance,
    color,
    description,
    castIds,
    steps.map((s) => [s.text, s.resolved])
  ])
}

/**
 * A thread as an object rather than a row of ticks.
 *
 * The grid answers "is this thread in this scene" and can answer nothing else.
 * It cannot say which thread is the spine and which is a running joke, whose
 * story a thread is, or - the question a revision actually asks - whether the
 * thread ever resolves.
 *
 * This was a dialog, and a dialog was the wrong shape for it three times over:
 * a card sized to the viewport cannot scroll, so a thread could hold only as
 * many steps as the monitor was tall, and every save added a restore point that
 * ate the room the steps needed; and a stray click on the backdrop threw the
 * lot away. So it is a screen the grid steps aside for. The page scrolls, the
 * step list scrolls inside it, the earlier versions sit in a tab nobody has to
 * look at, and leaving with unsaved edits asks first.
 */
export function PlotlineDetailScreen(props: {
  plotline: Plotline
  onBack: () => void
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
  const [tab, setTab] = useState<'details' | 'history'>('details')
  // What the thread said when this screen was opened, or when it was last
  // saved. Anything else on screen is an edit that would be lost on the way out.
  const [baseline, setBaseline] = useState(() =>
    fingerprint(
      props.plotline.importance || 'Subplot',
      props.plotline.color,
      props.plotline.description ?? '',
      props.plotline.castIds ?? [],
      props.plotline.steps ?? []
    )
  )

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
  const dirty = fingerprint(importance, color, description, castIds, steps) !== baseline

  const save = async (): Promise<void> => {
    setSaving(true)
    const ordered = steps.map((step, index) => ({ ...step, order: index }))
    try {
      const grid = await rpc.request('plot/setPlotlineDetail', [
        props.plotline.id,
        importance,
        castIds,
        ordered,
        color,
        description
      ])
      props.onSaved(grid)
      // Saving keeps you on the thread - a screen is somewhere you stay - so the
      // baseline moves with it and leaving no longer asks.
      setBaseline(fingerprint(importance, color, description, castIds, ordered))
    } finally {
      setSaving(false)
    }
  }

  // Back, the activity bar, the palette and a click in the binder all ask the
  // same question through the shell, rather than this screen guarding its own
  // door and every other door being open.
  useUnsavedGuard(`plotline:${props.plotline.id}`, props.plotline.name, dirty, save)

  const leave = (): void => useShellStore.getState().guardLeave(() => props.onBack())

  return (
    <div className="plotline-detail" role="region" aria-label={props.plotline.name}>
      <div className="plotline-detail-header">
        <button className="plotline-detail-back" onClick={leave}>
          <ArrowLeft size={14} strokeWidth={2} />
          {t('plotGrid.backToGrid')}
        </button>
        <span className="plotline-detail-name">
          <span className="plotgrid-color" style={{ background: color }} />
          {props.plotline.name}
        </span>
        {dirty && <span className="plotline-detail-dirty">{t('plotGrid.unsaved')}</span>}
        <button className="dialog-button" onClick={leave}>
          {t('dialog.cancel')}
        </button>
        <button className="dialog-button primary" disabled={saving} onClick={() => void save()}>
          {t('dialog.save')}
        </button>
      </div>

      {/* Earlier versions are a rare errand, not something to step around on the
          way to the description, so they get a tab of their own. */}
      <div className="plotline-detail-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'details'}
          className={`plotline-detail-tab${tab === 'details' ? ' active' : ''}`}
          onClick={() => setTab('details')}
        >
          {t('plotGrid.tabDetails')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'history'}
          className={`plotline-detail-tab${tab === 'history' ? ' active' : ''}`}
          onClick={() => setTab('history')}
        >
          {t('plotGrid.tabHistory')}
        </button>
      </div>

      {tab === 'details' ? (
        <div className="plotline-detail-body">
          <div className="plotline-detail-form">
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
            {/* A thread can want thirty steps. They scroll among themselves so the
                description above them stays reachable however long the list gets. */}
            <div className="plotline-steps">
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
                      setSteps(
                        steps.map((s, i) => (i === index ? { ...s, text: e.target.value } : s))
                      )
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
            </div>
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
          </div>
        </div>
      ) : (
        <div className="plotline-detail-body">
          <div className="plotline-detail-form">
            {/* Threads had no earlier versions while Codex entries did, so typing
                over a thread's description or its steps had no answer in the app.
                The tab is already named, so the panel is not headed twice. */}
            <RevisionsPanel
              historyMethod="plot/plotlineHistory"
              restoreMethod="plot/restorePlotlineRevision"
              targetId={props.plotline.id}
              restoreArgs={[props.plotline.id]}
              onRestored={(grid) => {
                props.onSaved(grid)
                // A restore rewrites the thread under the screen, so the fields
                // are re-read from what came back rather than left showing the
                // version that was just replaced.
                const restored = (grid as { plotlines?: Plotline[] } | null)?.plotlines?.find(
                  (p) => p.id === props.plotline.id
                )
                if (restored) {
                  setImportance(restored.importance || 'Subplot')
                  setColor(restored.color)
                  setDescription(restored.description ?? '')
                  setCastIds(restored.castIds ?? [])
                  setSteps(restored.steps ?? [])
                  setBaseline(
                    fingerprint(
                      restored.importance || 'Subplot',
                      restored.color,
                      restored.description ?? '',
                      restored.castIds ?? [],
                      restored.steps ?? []
                    )
                  )
                }
                setTab('details')
              }}
            />
          </div>
        </div>
      )}
    </div>
  )
}
