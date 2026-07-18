import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'

/**
 * Right-hand inspector: synopsis and notes for the open scene, saved on blur.
 * Grows Context/Footnotes/extension tabs in later milestones.
 */
export function Inspector(): React.JSX.Element {
  const { t } = useTranslation()
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const scene = useProjectStore((s) =>
    s.chapters
      .find((c) => c.guid === s.openChapterGuid)
      ?.scenes.find((sc) => sc.id === s.openSceneId)
  )
  const [synopsis, setSynopsis] = useState('')
  const [notes, setNotes] = useState('')

  useEffect(() => {
    setSynopsis(scene?.synopsis ?? '')
    setNotes('')
    if (openChapterGuid && openSceneId) {
      // Notes live only in the manifest; fetch the current value on scene switch.
      void rpc
        .request<{ notes?: string | null }>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => setNotes(meta.notes ?? ''))
        .catch(() => setNotes(''))
    }
  }, [openChapterGuid, openSceneId, scene?.synopsis])

  if (!openSceneId || !openChapterGuid || !scene) {
    return (
      <aside className="inspector">
        <div className="inspector-header">{t('shell.inspector')}</div>
        <div className="inspector-placeholder">{t('shell.inspectorEmpty')}</div>
      </aside>
    )
  }

  return (
    <aside className="inspector">
      <div className="inspector-header">{scene.title}</div>
      <div className="inspector-meta">
        {scene.wordCount.toLocaleString()} {t('shell.words')}
      </div>
      <label className="inspector-label" htmlFor="inspector-synopsis">
        {t('sceneNotes.synopsisTitle')}
      </label>
      <textarea
        id="inspector-synopsis"
        className="inspector-textarea"
        rows={4}
        placeholder={t('sceneNotes.synopsisPlaceholder')}
        value={synopsis}
        onChange={(e) => setSynopsis(e.target.value)}
        onBlur={() =>
          void rpc.request('scenes/setSynopsis', [openChapterGuid, openSceneId, synopsis])
        }
      />
      <label className="inspector-label" htmlFor="inspector-notes">
        {t('sceneNotes.title')}
      </label>
      <textarea
        id="inspector-notes"
        className="inspector-textarea inspector-notes"
        rows={8}
        placeholder={t('sceneNotes.placeholder')}
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        onBlur={() => void rpc.request('scenes/setNotes', [openChapterGuid, openSceneId, notes])}
      />
    </aside>
  )
}
