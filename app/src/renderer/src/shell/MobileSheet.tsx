import { useEffect, useRef, useState } from 'react'
import { X } from 'lucide-react'
import { useTranslation } from 'react-i18next'

/**
 * Generic mobile bottom sheet: a panel that slides up over a scrim, with a drag
 * grabber at the top. Tapping the scrim, pressing Escape, or dragging the grabber
 * down past a threshold dismisses it. Used for the writing-hub Inspector sheet
 * and reusable for other mobile overlays.
 *
 * On a tablet the same markup is styled as a trailing-edge slide-over (see
 * mobile.css), where dragging down makes no sense - hence the explicit close
 * button, which also gives an attached hardware keyboard a focusable target.
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
  const { t } = useTranslation()
  const [dragY, setDragY] = useState(0)
  const drag = useRef<{ startY: number } | null>(null)

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        onClose()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

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
        <div className="mobile-sheet-header">
          {title && <div className="mobile-sheet-title">{title}</div>}
          <button
            type="button"
            className="mobile-sheet-close"
            aria-label={t('dialog.close')}
            onClick={onClose}
          >
            <X size={18} strokeWidth={2} />
          </button>
        </div>
        <div className="mobile-sheet-body">{children}</div>
      </div>
    </div>
  )
}
