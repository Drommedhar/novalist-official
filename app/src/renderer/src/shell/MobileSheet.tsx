import { useRef, useState } from 'react'

/**
 * Generic mobile bottom sheet: a panel that slides up over a scrim, with a drag
 * grabber at the top. Tapping the scrim or dragging the grabber down past a
 * threshold dismisses it. Used for the writing-hub Inspector sheet and reusable
 * for other mobile overlays.
 */
export function MobileSheet({
  title,
  onClose,
  children
}: {
  title?: React.ReactNode
  onClose: () => void
  children: React.ReactNode
}): React.JSX.Element {
  const [dragY, setDragY] = useState(0)
  const drag = useRef<{ startY: number } | null>(null)

  const onDown = (e: React.PointerEvent): void => {
    drag.current = { startY: e.clientY }
    e.currentTarget.setPointerCapture(e.pointerId)
  }
  const onMove = (e: React.PointerEvent): void => {
    if (!drag.current) return
    setDragY(Math.max(0, e.clientY - drag.current.startY))
  }
  const onUp = (e: React.PointerEvent): void => {
    const dropped = dragY
    drag.current = null
    e.currentTarget.releasePointerCapture(e.pointerId)
    if (dropped > 120) onClose()
    else setDragY(0)
  }

  return (
    <div
      className="mobile-sheet-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        className="mobile-sheet"
        style={dragY ? { transform: `translateY(${dragY}px)`, transition: 'none' } : undefined}
      >
        <div className="mobile-sheet-grab" onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp}>
          <div className="mobile-sheet-grabber" />
        </div>
        {title && <div className="mobile-sheet-title">{title}</div>}
        <div className="mobile-sheet-body">{children}</div>
      </div>
    </div>
  )
}
