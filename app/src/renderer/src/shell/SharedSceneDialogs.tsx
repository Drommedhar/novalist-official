import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { metadataBinding, SharedMetadataField } from '../collaboration/SharedMetadataField'
import { rpc } from '../rpc/client'
import type { SceneDto } from '../stores/projectTypes'
import type { SceneEditDto } from './sceneEdit'
import { SharedMetadataDialog } from './SharedMetadataDialog'

function useSceneEdit(chapter: string, scene: SceneDto) {
  const [value, setValue] = useState<SceneEditDto | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    void rpc.request<SceneEditDto>('project/getSceneEdit', [chapter, scene.id]).then(next => {
      if (active) { setValue(next); setError('') }
    }).catch(failure => { if (active) setError(String(failure)) })
    return () => { active = false }
  }, [chapter, scene])
  return { value, error }
}

export function SharedSceneEditDialog({ chapter, scene, onClose }: {
  chapter: string; scene: SceneDto; onClose(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const meta = useSceneEdit(chapter, scene)
  return <SharedMetadataDialog title={t('explorer.renameScene')} onClose={onClose}>
    {meta.error && <p role="alert">{meta.error}</p>}
    <label className="inspector-label" htmlFor="shared-scene-title">{t('dialog.sceneName')}</label>
    <SharedMetadataField binding={metadataBinding(scene, 'title')} value={scene.title}>
      {field => <input id="shared-scene-title" autoFocus className="dialog-input" value={field.value} readOnly={!field.editable}
        onChange={event => field.change(event.target.value)} />}
    </SharedMetadataField>
    {meta.value && <>
      <label className="inspector-label" htmlFor="shared-scene-pov">{t('context.pov')}</label>
      <SharedMetadataField binding={metadataBinding(scene, 'analysisOverrides.pov')} value={meta.value.pov || null}>
        {field => <input id="shared-scene-pov" className="dialog-input" value={field.value ?? ''} readOnly={!field.editable}
          placeholder={t('common.povWatermark')} onChange={event => field.change(event.target.value || null)} />}
      </SharedMetadataField>
    </>}
  </SharedMetadataDialog>
}

interface StoryRange { start?: string; end?: string; note?: string; startTime?: string; endTime?: string }

export function SharedStoryDateDialog({ chapter, scene, onClose }: {
  chapter: string; scene: SceneDto; onClose(): void
}): React.JSX.Element {
  const { t } = useTranslation()
  const meta = useSceneEdit(chapter, scene)
  return <SharedMetadataDialog title={t('explorer.contextSetDate')} onClose={onClose}>
    {meta.error && <p role="alert">{meta.error}</p>}
    {meta.value && <SharedMetadataField<StoryRange | null> binding={metadataBinding(scene, 'dateRange')}
      value={{ start: meta.value.dateStart, end: meta.value.dateEnd, note: meta.value.dateNote }}>
      {field => <>
        <div className="date-range-row">
          {(['start', 'end'] as const).map(key => <div className="date-range-col" key={key}>
            <label className="inspector-label" htmlFor={`shared-date-${key}`}>{t(key === 'start' ? 'dialog.dateStart' : 'dialog.dateEnd')}</label>
            <input id={`shared-date-${key}`} autoFocus={key === 'start'} type="date" className="dialog-input"
              value={field.value?.[key] ?? ''} readOnly={!field.editable}
              onChange={event => { const value = event.target.value; field.change(previous => ({ ...previous, [key]: value })) }} />
          </div>)}
        </div>
        <label className="inspector-label" htmlFor="shared-date-note">{t('dialog.dateNoteWatermark')}</label>
        <input id="shared-date-note" className="dialog-input" value={field.value?.note ?? ''} readOnly={!field.editable}
          onChange={event => { const note = event.target.value; field.change(previous => ({ ...previous, note })) }} />
        <div className="dialog-actions">
          <button className="dialog-button" aria-disabled={!field.editable} onClick={() => field.change(null)}>{t('dialog.clear')}</button>
        </div>
      </>}
    </SharedMetadataField>}
  </SharedMetadataDialog>
}
