import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'

interface SceneMeta {
  notes?: string | null
}

const MIN_HEIGHT = 80
const MAX_HEIGHT = 480
const DEFAULT_HEIGHT = 180

/**
 * Bottom-docked scene notes panel (Synopsis + Notes), mirroring the desktop
 * SceneNotesView. Belongs to the editor only and is toggled from the toolbar /
 * Ctrl+Shift+N. Resizable by dragging the top edge.
 */
export function SceneNotesDock(): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)

  const [synopsis, setSynopsis] = useState('')
  const [notes, setNotes] = useState('')
  const [height, setHeight] = useState(DEFAULT_HEIGHT)
  const dragState = useRef<{ startY: number; startHeight: number } | null>(null)

  useEffect(() => {
    setSynopsis(scene?.synopsis ?? '')
    setNotes('')
    if (openChapterGuid && openSceneId) {
      void rpc
        .request<SceneMeta>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => setNotes(meta.notes ?? ''))
        .catch(() => setNotes(''))
    }
  }, [openChapterGuid, openSceneId, scene?.synopsis])

  const onResizePointerDown = (e: React.PointerEvent): void => {
    dragState.current = { startY: e.clientY, startHeight: height }
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  const onResizePointerMove = (e: React.PointerEvent): void => {
    if (!dragState.current) return
    // Drag up grows the dock (it is docked to the bottom).
    const delta = dragState.current.startY - e.clientY
    const next = Math.min(MAX_HEIGHT, Math.max(MIN_HEIGHT, dragState.current.startHeight + delta))
    setHeight(next)
  }
  const onResizePointerUp = (e: React.PointerEvent): void => {
    dragState.current = null
    e.currentTarget.releasePointerCapture(e.pointerId)
  }

  const sceneOpen = Boolean(openChapterGuid && openSceneId && scene)

  return (
    <section className="notes-dock" style={{ height }} aria-label={t('sceneNotes.title')}>
      <div
        className="notes-dock-resize"
        role="separator"
        aria-orientation="horizontal"
        onPointerDown={onResizePointerDown}
        onPointerMove={onResizePointerMove}
        onPointerUp={onResizePointerUp}
      />
      {sceneOpen ? (
        <div className="notes-dock-body">
          <div className="notes-dock-col">
            <label className="notes-dock-label" htmlFor="dock-synopsis">
              {t('sceneNotes.synopsisTitle')}
            </label>
            <textarea
              id="dock-synopsis"
              className="notes-dock-textarea notes-dock-synopsis"
              placeholder={t('sceneNotes.synopsisPlaceholder')}
              value={synopsis}
              onChange={(e) => setSynopsis(e.target.value)}
              onBlur={() =>
                void rpc.request('scenes/setSynopsis', [openChapterGuid, openSceneId, synopsis])
              }
            />
          </div>
          <div className="notes-dock-col notes-dock-col-grow">
            <label className="notes-dock-label" htmlFor="dock-notes">
              {t('sceneNotes.title')}
            </label>
            <textarea
              id="dock-notes"
              className="notes-dock-textarea"
              placeholder={t('sceneNotes.placeholder')}
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              onBlur={() =>
                void rpc.request('scenes/setNotes', [openChapterGuid, openSceneId, notes])
              }
            />
          </div>
        </div>
      ) : (
        <div className="notes-dock-empty">{t('sceneNotes.empty')}</div>
      )}
    </section>
  )
}
