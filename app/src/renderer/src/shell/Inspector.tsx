import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Camera, GitCompare, History, RotateCcw, Trash2 } from 'lucide-react'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { InputDialog } from './InputDialog'
import { ContextPanel } from './ContextPanel'
import { AnnotationsPanel } from './AnnotationsPanel'
import { SnapshotCompareDialog } from './SnapshotCompareDialog'
import './inspector.css'

interface SnapshotDto {
  id: string
  label: string
  takenAt: string
  wordCount: number
}

interface SceneMeta {
  notes?: string | null
  storyDate?: string
  isoDate?: string | null
}

interface CompareView {
  idA: string
  idB: string
  labelA: string
  labelB: string
}

function snapshotLabel(snapshot: SnapshotDto | undefined): string {
  if (!snapshot) return ''
  return snapshot.label || snapshot.takenAt.slice(0, 10)
}

/**
 * Right-hand inspector: scene context/analysis, synopsis, notes, annotations
 * and per-scene snapshot history (take / restore / delete / compare).
 */
export function Inspector(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)
  const sceneIndex = chapter ? chapter.scenes.findIndex((sc) => sc.id === openSceneId) + 1 : 0
  const sceneTotal = chapter?.scenes.length ?? 0

  const [synopsis, setSynopsis] = useState('')
  const [notes, setNotes] = useState('')
  const [storyDate, setStoryDate] = useState('')
  const [isoDate, setIsoDate] = useState<string | null>(null)
  const [snapshots, setSnapshots] = useState<SnapshotDto[]>([])
  const [labelPrompt, setLabelPrompt] = useState(false)
  const [compareBase, setCompareBase] = useState<string | null>(null)
  const [compareView, setCompareView] = useState<CompareView | null>(null)

  useEffect(() => {
    setSynopsis(scene?.synopsis ?? '')
    setNotes('')
    setStoryDate('')
    setIsoDate(null)
    setCompareBase(null)
    if (openChapterGuid && openSceneId) {
      // Notes + the resolved story date live in the manifest; fetch on switch.
      void rpc
        .request<SceneMeta>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => {
          setNotes(meta.notes ?? '')
          setStoryDate(meta.storyDate ?? '')
          setIsoDate(meta.isoDate ?? null)
        })
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

  const weekday = isoDate
    ? new Date(`${isoDate}T00:00:00`).toLocaleDateString(i18n.language, { weekday: 'long' })
    : null
  const dateDisplay = storyDate ? (weekday ? `${storyDate} · ${weekday}` : storyDate) : ''
  const positionText =
    sceneTotal > 0
      ? chapter?.title
        ? t('context.sceneOfChapter')
            .replace('{0}', chapter.title)
            .replace('{1}', String(sceneIndex))
            .replace('{2}', String(sceneTotal))
        : t('context.sceneOf').replace('{0}', String(sceneIndex)).replace('{1}', String(sceneTotal))
      : ''

  const onCompare = (snapshot: SnapshotDto): void => {
    if (!compareBase) {
      setCompareBase(snapshot.id)
      return
    }
    if (compareBase === snapshot.id) {
      setCompareBase(null)
      return
    }
    setCompareView({
      idA: compareBase,
      idB: snapshot.id,
      labelA: snapshotLabel(snapshots.find((s) => s.id === compareBase)),
      labelB: snapshotLabel(snapshot)
    })
    setCompareBase(null)
  }

  return (
    <aside className="inspector">
      <div className="inspector-header">{scene.title}</div>
      {positionText && <div className="inspector-subtitle">{positionText}</div>}
      {dateDisplay && <div className="inspector-date">{dateDisplay}</div>}
      <div className="inspector-meta">
        {scene.wordCount.toLocaleString()} {t('shell.words')}
      </div>
      <ContextPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
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
      <AnnotationsPanel chapterGuid={openChapterGuid} sceneId={openSceneId} />
      <label className="inspector-label">
        <History size={12} strokeWidth={2} /> {t('snapshots.title')}
      </label>
      <button className="binder-rail-item" onClick={() => setLabelPrompt(true)}>
        <Camera size={13} strokeWidth={2} />
        {t('snapshots.take')}
      </button>
      {compareBase && <div className="snapshot-compare-hint">{t('snapshots.comparePick')}</div>}
      {snapshots.map((snapshot) => (
        <div key={snapshot.id} className="snapshot-row">
          <span className="binder-scene-title">{snapshotLabel(snapshot)}</span>
          <span className="binder-scene-words">{snapshot.wordCount.toLocaleString()}</span>
          <div className="snapshot-actions">
            <button
              className={`snapshot-action${compareBase === snapshot.id ? ' active' : ''}`}
              title={t('snapshots.compare')}
              aria-label={t('snapshots.compare')}
              onClick={() => onCompare(snapshot)}
            >
              <GitCompare size={13} strokeWidth={2} />
            </button>
            <button
              className="snapshot-action"
              title={t('snapshots.restore')}
              aria-label={t('snapshots.restore')}
              onClick={() =>
                void rpc
                  .request<boolean>('snapshots/restore', [
                    openChapterGuid,
                    openSceneId,
                    snapshot.id
                  ])
                  .then(async (restored) => {
                    if (restored) {
                      await useProjectStore.getState().openScene(openChapterGuid, openSceneId)
                    }
                  })
              }
            >
              <RotateCcw size={13} strokeWidth={2} />
            </button>
            <button
              className="snapshot-action danger"
              title={t('snapshots.delete')}
              aria-label={t('snapshots.delete')}
              onClick={() =>
                void rpc
                  .request<SnapshotDto[]>('snapshots/delete', [
                    openChapterGuid,
                    openSceneId,
                    snapshot.id
                  ])
                  .then((list) => {
                    setSnapshots(list)
                    if (compareBase === snapshot.id) setCompareBase(null)
                  })
              }
            >
              <Trash2 size={13} strokeWidth={2} />
            </button>
          </div>
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
      {compareView && (
        <SnapshotCompareDialog
          chapterGuid={openChapterGuid}
          sceneId={openSceneId}
          idA={compareView.idA}
          idB={compareView.idB}
          labelA={compareView.labelA}
          labelB={compareView.labelB}
          onClose={() => setCompareView(null)}
        />
      )}
    </aside>
  )
}
