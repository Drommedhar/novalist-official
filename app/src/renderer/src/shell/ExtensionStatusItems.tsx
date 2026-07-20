import { useEffect, useState } from 'react'
import { rpc } from '../rpc/client'

/**
 * Renders extension-contributed status-bar items (IStatusBarContributor) in the
 * status bar's extension slot. Mirrors the desktop's 1-second refresh timer: the
 * backend re-reads each item's GetText/GetTooltip on every poll, and a click
 * routes back to the item's OnClick handler by id.
 */
interface StatusBarItemInfo {
  id: string
  alignment: string
  order: number
  text: string
  tooltip: string | null
  hasCommand: boolean
}

const REFRESH_MS = 1000

export function ExtensionStatusItems(): React.JSX.Element | null {
  const [items, setItems] = useState<StatusBarItemInfo[]>([])

  useEffect(() => {
    let active = true
    const load = (): void => {
      void rpc
        .request<StatusBarItemInfo[]>('extensions/statusBarItems')
        .then((next) => {
          if (active) setItems(next)
        })
        .catch(() => {})
    }
    load()
    const id = window.setInterval(load, REFRESH_MS)
    return () => {
      active = false
      window.clearInterval(id)
    }
  }, [])

  if (items.length === 0) return null

  const click = (item: StatusBarItemInfo): void => {
    if (!item.hasCommand) return
    void rpc.request('extensions/statusBarItem/execute', [item.id]).then(() => {
      void rpc
        .request<StatusBarItemInfo[]>('extensions/statusBarItems')
        .then(setItems)
        .catch(() => {})
    })
  }

  return (
    <span className="status-ext-items">
      {items.map((item) =>
        item.hasCommand ? (
          <button
            key={item.id}
            type="button"
            className="status-ext-item"
            title={item.tooltip ?? undefined}
            onClick={() => click(item)}
          >
            {item.text}
          </button>
        ) : (
          <span key={item.id} className="status-ext-item" title={item.tooltip ?? undefined}>
            {item.text}
          </span>
        )
      )}
    </span>
  )
}
