import { useRef } from 'react'

/**
 * A thin vertical drag handle for resizing a side panel. `edge` says which edge
 * of the panel the handle sits on: a binder handle on its 'right' grows when
 * dragged right; an inspector handle on its 'left' grows when dragged left.
 */
export function PanelResizer({
  edge,
  width,
  onResize,
  onResizeEnd
}: {
  edge: 'left' | 'right'
  width: number
  onResize: (px: number) => void
  /** Fired once when the drag finishes, with the width the store settled on -
   *  the hook for persisting the size rather than writing on every move. */
  onResizeEnd?: (px: number) => void
}): React.JSX.Element {
  const drag = useRef<{ startX: number; startWidth: number } | null>(null)

  const onPointerDown = (e: React.PointerEvent): void => {
    drag.current = { startX: e.clientX, startWidth: width }
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  const onPointerMove = (e: React.PointerEvent): void => {
    if (!drag.current) return
    const delta = e.clientX - drag.current.startX
    onResize(drag.current.startWidth + (edge === 'right' ? delta : -delta))
  }
  const onPointerUp = (e: React.PointerEvent): void => {
    if (drag.current) onResizeEnd?.(width)
    drag.current = null
    e.currentTarget.releasePointerCapture(e.pointerId)
  }

  return (
    <div
      className={`panel-resizer panel-resizer-${edge}`}
      role="separator"
      aria-orientation="vertical"
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
    />
  )
}
