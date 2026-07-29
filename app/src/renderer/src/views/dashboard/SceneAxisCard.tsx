import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import { useManuscriptPropsStore } from '../../stores/manuscriptPropsStore'

interface AxisPoint {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
  intensity: number | null
}

/**
 * One of the writer's own numeric scene fields, charted across the book.
 *
 * A rating axis - stakes, pace, how much the viewpoint character knows - reads
 * flat as a column of numbers and obvious as a curve: a run of identical
 * scenes, a peak in the wrong place, a thread that goes quiet for eighty
 * pages. Which axes exist is the writer's business; these are their fields.
 */
export function SceneAxisCard(): React.JSX.Element {
  const { t } = useTranslation()
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const [key, setKey] = useState('')
  const [points, setPoints] = useState<AxisPoint[]>([])

  const axes = definitions.filter((d) => d.scope === 'Scene' && d.type === 'Int')

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  // Falls onto the first axis so the card arrives showing something. Picking
  // one from an empty chart would be a step for no reason.
  useEffect(() => {
    if (axes.length > 0 && !axes.some((a) => a.key === key)) setKey(axes[0].key)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [axes.length])

  useEffect(() => {
    if (!key) {
      setPoints([])
      return
    }
    void rpc
      .request<AxisPoint[]>('analytics/sceneFieldCurve', [key])
      .then(setPoints)
      .catch(() => setPoints([]))
  }, [key])

  // No numeric scene fields means no axes to chart, and an empty card would be
  // a standing advertisement for a feature the writer has not set up.
  if (axes.length === 0) return <></>

  const rated = points.filter((p) => p.intensity !== null)
  const highest = Math.max(1, ...rated.map((p) => Math.abs(p.intensity ?? 0)))

  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">{t('sceneAxis.title')}</div>
      <div className="dashboard-echo-desc">{t('sceneAxis.intro')}</div>

      {axes.length > 1 && (
        <select
          className="dashboard-range"
          aria-label={t('sceneAxis.pick')}
          value={key}
          onChange={(e) => setKey(e.target.value)}
        >
          {axes.map((axis) => (
            <option key={axis.key} value={axis.key}>
              {axis.label}
            </option>
          ))}
        </select>
      )}

      {rated.length < 2 ? (
        <div className="settings-hint">{t('sceneAxis.notEnough')}</div>
      ) : (
        <div className="tension-chart" role="img" aria-label={t('sceneAxis.title')}>
          {points.map((point) => {
            // Scaled to the largest value the writer actually used: their axis
            // may run 1-5 or 0-100, and a fixed range would flatten one of them.
            const value = point.intensity ?? 0
            const height =
              point.intensity === null ? 2 : Math.max(2, (Math.abs(value) / highest) * 100)
            return (
              <button
                key={point.sceneId}
                className={`tension-bar${point.intensity === null ? ' unrated' : ''}${
                  value < 0 ? ' negative' : ''
                }`}
                style={{ height: `${height}%` }}
                title={`${point.chapterTitle} - ${point.sceneTitle}: ${
                  point.intensity === null ? t('tension.unrated') : value
                }`}
                onClick={() =>
                  void useProjectStore.getState().openScene(point.chapterGuid, point.sceneId)
                }
              />
            )
          })}
        </div>
      )}
    </div>
  )
}
