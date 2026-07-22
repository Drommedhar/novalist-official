import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SceneNotesFields } from './SceneNotesFields'

const MIN_HEIGHT = 80
const MAX_HEIGHT = 480
const DEFAULT_HEIGHT = 180

/**
 * Bottom-docked scene notes panel (Synopsis + Notes), mirroring the desktop
 * SceneNotesView. Belongs to the editor only and is toggled from the toolbar /
 * Ctrl+Shift+N. Resizable by dragging the top edge. The fields themselves live in
 * SceneNotesFields, shared with the mobile writing-hub sheet.
 */
export function SceneNotesDock(): React.JSX.Element {
  const { t } = useTranslation()
  const [height, setHeight] = useState(DEFAULT_HEIGHT)
  const dragState = useRef<{ startY: number; startHeight: number } | null>(null)

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
      <SceneNotesFields />
    </section>
  )
}
