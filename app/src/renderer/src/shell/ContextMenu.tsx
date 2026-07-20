import { useEffect, useRef } from 'react'

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

  // Keep the menu inside the viewport.
  const style: React.CSSProperties = {
    left: Math.min(x, window.innerWidth - 200),
    top: Math.min(y, window.innerHeight - items.length * 30 - 16)
  }

  return (
    <div ref={ref} className="context-menu" style={style} role="menu">
      {items.map((item) => (
        <button
          key={item.label}
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
