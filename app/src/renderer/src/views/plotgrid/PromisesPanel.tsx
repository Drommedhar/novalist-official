import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

interface PromiseDto {
  sceneId: string
  sceneTitle: string
  chapterGuid: string
  chapterTitle: string
  promiseId: string
  label: string
  payoffSceneId: string | null
  payoffSceneTitle: string | null
  state: 'Kept' | 'Unpaid' | 'Broken' | 'OutOfOrder'
}

/**
 * What the book has promised the reader, and whether anything answers it.
 *
 * Novalist had no edge between two scenes at all - the only links in the
 * product joined entities - so the one question worth asking about a setup,
 * whether anything pays it off, could not be asked.
 */
export function PromisesPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const [promises, setPromises] = useState<PromiseDto[]>([])
  const [sceneId, setSceneId] = useState('')
  const [label, setLabel] = useState('')

  useEffect(() => {
    void rpc.request<PromiseDto[]>('promises/report').then(setPromises).catch(() => setPromises([]))
  }, [])

  const scenes = chapters.flatMap((chapter) =>
    chapter.scenes.map((scene) => ({
      id: scene.id,
      guid: chapter.guid,
      label: `${chapter.title} - ${scene.title}`
    }))
  )

  const add = (): void => {
    if (!sceneId || label.trim().length === 0) return
    void rpc
      .request<PromiseDto[]>('promises/save', [sceneId, null, label.trim(), null])
      .then((next) => {
        setPromises(next)
        setLabel('')
      })
  }

  const setPayoff = (promise: PromiseDto, payoff: string): void => {
    void rpc
      .request<PromiseDto[]>('promises/save', [
        promise.sceneId,
        promise.promiseId,
        promise.label,
        payoff || null
      ])
      .then(setPromises)
  }

  // Unanswered first: a kept promise needs no attention, and burying the
  // open ones under it is how they get forgotten.
  const order: Record<PromiseDto['state'], number> = {
    Unpaid: 0,
    Broken: 1,
    OutOfOrder: 2,
    Kept: 3
  }
  const sorted = [...promises].sort((a, b) => order[a.state] - order[b.state])

  return (
    <div className="promises">
      <div className="dashboard-card-title">{t('promises.title')}</div>
      <div className="dashboard-echo-desc">{t('promises.intro')}</div>

      <div className="promises-add">
        <select
          className="inspector-input"
          aria-label={t('promises.scene')}
          value={sceneId}
          onChange={(e) => setSceneId(e.target.value)}
        >
          <option value="">{t('promises.chooseScene')}</option>
          {scenes.map((scene) => (
            <option key={scene.id} value={scene.id}>
              {scene.label}
            </option>
          ))}
        </select>
        <input
          className="inspector-input"
          placeholder={t('promises.labelPlaceholder')}
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && add()}
        />
        <button className="dialog-button" disabled={!sceneId || !label.trim()} onClick={add}>
          <Plus size={14} /> {t('promises.add')}
        </button>
      </div>

      {sorted.length === 0 && <div className="settings-hint">{t('promises.none')}</div>}

      {sorted.map((promise) => (
        <div key={promise.promiseId} className={`promise-row state-${promise.state.toLowerCase()}`}>
          <span className={`promise-state state-${promise.state.toLowerCase()}`}>
            {t(`promises.state${promise.state}`)}
          </span>
          <span className="promise-label">{promise.label}</span>
          <button
            className="promise-scene"
            onClick={() =>
              void useProjectStore.getState().openScene(promise.chapterGuid, promise.sceneId)
            }
          >
            {promise.chapterTitle} - {promise.sceneTitle}
          </button>
          <select
            className="inspector-input"
            aria-label={t('promises.payoff')}
            value={promise.payoffSceneId ?? ''}
            onChange={(e) => setPayoff(promise, e.target.value)}
          >
            <option value="">{t('promises.noPayoff')}</option>
            {scenes.map((scene) => (
              <option key={scene.id} value={scene.id}>
                {scene.label}
              </option>
            ))}
          </select>
          <button
            className="dialog-button danger"
            title={t('promises.remove')}
            onClick={() =>
              void rpc
                .request<PromiseDto[]>('promises/delete', [promise.sceneId, promise.promiseId])
                .then(setPromises)
            }
          >
            <Trash2 size={14} />
          </button>
        </div>
      ))}
    </div>
  )
}
