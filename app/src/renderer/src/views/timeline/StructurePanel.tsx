import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Wand2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import './structure.css'

interface StructureBeat {
  key: string
  title: string
  description: string
  targetPercent: number
  sceneId: string | null
  sceneTitle: string | null
  chapterGuid: string | null
  /** -1 when nothing is bound; not the same as a beat at the very start. */
  actualPercent: number
  isFilled: boolean
  driftPercent: number
}

interface StructureTemplate {
  id: string
  displayName: string
  description: string
  beatCount: number
}

/** How far off a beat can sit before it is worth saying so. Structures are not
 *  precise instruments, and flagging a four-point drift would cry wolf. */
const DRIFT_TOLERANCE = 8

/**
 * The story structure the book is written against, beat by beat.
 *
 * Applying a template used to append timeline events that by design never
 * touched a chapter or a scene, so the structure and the manuscript had no
 * relationship. Each beat now names the scene that fulfils it, which is what
 * makes "which beats are still holes" and "does the midpoint land in the
 * middle" answerable at all.
 */
export function StructurePanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [templates, setTemplates] = useState<StructureTemplate[]>([])
  const [templateId, setTemplateId] = useState('')
  const [beats, setBeats] = useState<StructureBeat[]>([])
  const chapters = useProjectStore((s) => s.chapters)

  const load = useCallback(async () => {
    setTemplates(await rpc.request<StructureTemplate[]>('structure/templates'))
    setTemplateId(await rpc.request<string>('structure/get'))
    setBeats(await rpc.request<StructureBeat[]>('structure/beats'))
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  // Word counts move as the writer works, so the positions follow the binder.
  useEffect(() => {
    void rpc.request<StructureBeat[]>('structure/beats').then(setBeats)
  }, [chapters])

  const choose = async (id: string): Promise<void> => {
    setTemplateId(id)
    setBeats(await rpc.request<StructureBeat[]>('structure/set', [id]))
  }

  const bind = async (beatKey: string, value: string): Promise<void> => {
    if (!value) {
      // Unbinding needs no scene, but the backend still wants a chapter it can
      // find the scene in; the empty key clears whatever holds the beat.
      const holder = beats.find((b) => b.key === beatKey)
      if (!holder?.sceneId || !holder.chapterGuid) return
      setBeats(
        await rpc.request<StructureBeat[]>('structure/bindScene', [
          holder.chapterGuid,
          holder.sceneId,
          null
        ])
      )
      return
    }
    const [chapterGuid, sceneId] = value.split('|')
    setBeats(
      await rpc.request<StructureBeat[]>('structure/bindScene', [chapterGuid, sceneId, beatKey])
    )
  }

  const fillGaps = async (): Promise<void> => {
    const result = await rpc.request<{
      created: number
      beats: StructureBeat[]
      state: ProjectStateDto
    }>('structure/fillGaps')
    useProjectStore.getState().applyState(result.state)
    setBeats(result.beats)
  }

  const unfilled = beats.filter((b) => !b.isFilled).length

  return (
    <div className="structure-panel">
      <div className="settings-button-row">
        <select
          className="inspector-input"
          value={templateId}
          onChange={(e) => void choose(e.target.value)}
        >
          <option value="">{t('structure.none')}</option>
          {templates.map((template) => (
            <option key={template.id} value={template.id}>
              {template.displayName} ({template.beatCount})
            </option>
          ))}
        </select>
        {beats.length > 0 && unfilled > 0 && (
          <button className="dialog-button" onClick={() => void fillGaps()}>
            <Wand2 size={14} /> {t('structure.fillGaps', { count: unfilled })}
          </button>
        )}
      </div>

      {beats.length === 0 && <p className="settings-hint">{t('structure.pickOne')}</p>}

      {beats.map((beat) => (
        <div key={beat.key} className={`structure-beat${beat.isFilled ? '' : ' unfilled'}`}>
          <div className="structure-beat-head">
            <span className="structure-beat-title">{beat.title}</span>
            {beat.targetPercent > 0 && (
              <span className="structure-beat-target">
                {t('structure.target', { percent: beat.targetPercent })}
              </span>
            )}
          </div>
          <div className="settings-hint">{beat.description}</div>

          <select
            className="inspector-input"
            value={beat.sceneId ? `${beat.chapterGuid}|${beat.sceneId}` : ''}
            onChange={(e) => void bind(beat.key, e.target.value)}
          >
            <option value="">{t('structure.unfilled')}</option>
            {chapters.flatMap((chapter) =>
              chapter.scenes.map((scene) => (
                <option key={scene.id} value={`${chapter.guid}|${scene.id}`}>
                  {chapter.title} - {scene.title}
                </option>
              ))
            )}
          </select>

          {beat.isFilled && beat.targetPercent > 0 && (
            <div
              className={`structure-drift${
                Math.abs(beat.driftPercent) > DRIFT_TOLERANCE ? ' off' : ''
              }`}
            >
              {Math.abs(beat.driftPercent) > DRIFT_TOLERANCE
                ? t('structure.drifted', {
                    actual: beat.actualPercent,
                    direction: t(
                      beat.driftPercent > 0 ? 'structure.late' : 'structure.early'
                    )
                  })
                : t('structure.onTarget', { actual: beat.actualPercent })}
            </div>
          )}
        </div>
      ))}
    </div>
  )
}
