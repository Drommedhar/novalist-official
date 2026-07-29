import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

interface TensionPoint {
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
  intensity: number | null
  emotion: string
}

/**
 * The book's tension, scene by scene.
 *
 * Intensity has been computed and hand-overridable per scene for a long time
 * and shown only as one number in the Inspector - where a curve is the one
 * shape it has anything to say in. A run of flat scenes or a climax in the
 * wrong place is visible here and nowhere else.
 */
export function TensionCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [points, setPoints] = useState<TensionPoint[]>([])

  useEffect(() => {
    void rpc.request<TensionPoint[]>('analytics/tension').then(setPoints).catch(() => setPoints([]))
  }, [])

  const rated = points.filter((p) => p.intensity !== null)
  // Nothing to draw until at least a couple of scenes have been rated: a
  // single point is not a curve, and an empty chart reads as flat writing.
  if (rated.length < 2) return <></>

  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">{t('tension.title')}</div>
      <div className="dashboard-echo-desc">{t('tension.intro')}</div>

      <div className="tension-chart" role="img" aria-label={t('tension.title')}>
        {points.map((point) => {
          // -10..+10 mapped onto the height, with the midline at zero.
          const value = point.intensity ?? 0
          const height = point.intensity === null ? 2 : Math.max(2, Math.abs(value) * 5)
          return (
            <button
              key={point.sceneId}
              className={`tension-bar${point.intensity === null ? ' unrated' : ''}${
                value < 0 ? ' negative' : ''
              }`}
              style={{ height: `${height}%` }}
              title={`${point.chapterTitle} - ${point.sceneTitle}: ${
                point.intensity === null ? t('tension.unrated') : value
              }${point.emotion ? ` (${point.emotion})` : ''}`}
              onClick={() =>
                void useProjectStore.getState().openScene(point.chapterGuid, point.sceneId)
              }
            />
          )
        })}
      </div>
      <div className="settings-hint">
        {t('tension.rated', { rated: rated.length, total: points.length })}
      </div>
    </div>
  )
}
