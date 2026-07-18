import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Camera, History } from 'lucide-react'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { InputDialog } from './InputDialog'

interface SnapshotDto {
  id: string
  label: string
  takenAt: string
  wordCount: number
}

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
  const [snapshots, setSnapshots] = useState<SnapshotDto[]>([])
  const [labelPrompt, setLabelPrompt] = useState(false)

  useEffect(() => {
    setSynopsis(scene?.synopsis ?? '')
    setNotes('')
    if (openChapterGuid && openSceneId) {
      // Notes live only in the manifest; fetch the current value on scene switch.
      void rpc
        .request<{ notes?: string | null }>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => setNotes(meta.notes ?? ''))
        .catch(() => setNotes(''))
      void rpc
        .request<SnapshotDto[]>('snapshots/list', [openChapterGuid, openSceneId])
        .then(setSnapshots)
        .catch(() => setSnapshots([]))
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
      <label className="inspector-label">
        <History size={12} strokeWidth={2} /> {t('snapshots.title')}
      </label>
      <button className="binder-rail-item" onClick={() => setLabelPrompt(true)}>
        <Camera size={13} strokeWidth={2} />
        {t('snapshots.take')}
      </button>
      {snapshots.map((snapshot) => (
        <div key={snapshot.id} className="snapshot-row">
          <span className="binder-scene-title">
            {snapshot.label || snapshot.takenAt.slice(0, 10)}
          </span>
          <span className="binder-scene-words">{snapshot.wordCount.toLocaleString()}</span>
          <button
            className="snapshot-restore"
            onClick={() =>
              void rpc
                .request<boolean>('snapshots/restore', [openChapterGuid, openSceneId, snapshot.id])
                .then(async (restored) => {
                  if (restored) {
                    await useProjectStore.getState().openScene(openChapterGuid, openSceneId)
                  }
                })
            }
          >
            {t('snapshots.restore')}
          </button>
        </div>
      ))}
      {labelPrompt && (
        <InputDialog
          title={t('snapshots.take')}
          placeholder={t('snapshots.labelWatermark')}
          onCancel={() => setLabelPrompt(false)}
          onSubmit={(label) => {
            setLabelPrompt(false)
            void rpc
              .request<SnapshotDto[]>('snapshots/take', [openChapterGuid, openSceneId, label])
              .then(setSnapshots)
          }}
        />
      )}
    </aside>
  )
}
