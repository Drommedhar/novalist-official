import { useEffect, useLayoutEffect, useRef, useState } from 'react'

/** Breathing room kept between the menu and the edge of the window. */
const VIEWPORT_MARGIN = 8

export interface ContextMenuItem {
  label: string
  danger?: boolean
  onClick(): void
}

interface ContextMenuProps {
  x: number
  y: number
  items: ContextMenuItem[]
  onClose(): void
}

/** Lightweight positioned context menu; closes on outside press or Escape. */
export function ContextMenu({ x, y, items, onClose }: ContextMenuProps): React.JSX.Element {
  const ref = useRef<HTMLDivElement>(null)
  const [pos, setPos] = useState({ left: x, top: y })

  // Keep the menu inside the window, measured rather than estimated: a guess at
  // the row height is wrong by a little for a three-item menu and by a screenful
  // for a long one, which is exactly when being pushed off the edge shows. The
  // stylesheet caps the height, so a menu too long for the window scrolls
  // instead of overflowing it. Runs before paint, so nothing is seen to move.
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    setPos({
      left: Math.max(VIEWPORT_MARGIN, Math.min(x, window.innerWidth - el.offsetWidth - VIEWPORT_MARGIN)),
      top: Math.max(VIEWPORT_MARGIN, Math.min(y, window.innerHeight - el.offsetHeight - VIEWPORT_MARGIN))
    })
  }, [x, y, items.length])

  useEffect(() => {
    const onPointerDown = (e: PointerEvent): void => {
      if (!ref.current?.contains(e.target as Node)) onClose()
    }
    const onKeyDown = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('pointerdown', onPointerDown)
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('pointerdown', onPointerDown)
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [onClose])

  return (
    <div ref={ref} className="context-menu" style={pos} role="menu">
      {items.map((item, index) => (
        <button
          key={`${index}-${item.label}`}
          className={`context-menu-item${item.danger ? ' danger' : ''}`}
          role="menuitem"
          onClick={() => {
            onClose()
            item.onClick()
          }}
        >
          {item.label}
        </button>
      ))}
    </div>
  )
}
