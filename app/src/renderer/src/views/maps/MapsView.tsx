import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { InputDialog } from '../../shell/InputDialog'

interface MapRefDto {
  id: string
  name: string
}

interface MapWindow extends Window {
  setImageBaseUrl(url: string): void
  setMapData(json: string): void
  getMapData(): string
}

const MAP_AUTOSAVE_MS = 1200

export function MapsView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [maps, setMaps] = useState<MapRefDto[]>([])
  const [activeId, setActiveId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const readyRef = useRef(false)
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    if (mainView !== 'maps') return
    void rpc.request<MapRefDto[]>('maps/list').then((list) => {
      setMaps(list)
      if (list.length > 0 && !activeId) setActiveId(list[0].id)
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mainView])

  const pushMap = async (): Promise<void> => {
    const win = iframeRef.current?.contentWindow as MapWindow | null
    if (!win || !readyRef.current || !activeId) return
    const loaded = await rpc.request<{ json: string } | null>('maps/load', [activeId])
    if (loaded) {
      win.setImageBaseUrl('novalist-project://nl/')
      win.setMapData(loaded.json)
    }
  }

  useEffect(() => {
    void pushMap()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId])

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return
    const onMessage = (event: MessageEvent): void => {
      if (event.source !== iframe.contentWindow) return
      const raw = (event.data as { novalistMap?: string })?.novalistMap
      if (typeof raw !== 'string') return
      let message: { type: string }
      try {
        message = JSON.parse(raw)
      } catch {
        return
      }
      if (message.type === 'ready') {
        readyRef.current = true
        void pushMap()
      } else if (message.type === 'mapChanged') {
        if (saveTimer.current) clearTimeout(saveTimer.current)
        saveTimer.current = setTimeout(() => {
          const win = iframe.contentWindow as MapWindow | null
          if (!win || typeof win.getMapData !== 'function') return
          void rpc.request('maps/save', [win.getMapData()])
        }, MAP_AUTOSAVE_MS)
      }
    }
    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      readyRef.current = false
      if (saveTimer.current) clearTimeout(saveTimer.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div className="mapsview">
      <div className="timeline-toolbar">
        <button className="toolbar-button toolbar-action" onClick={() => setCreating(true)}>
          <Plus size={14} strokeWidth={2} />
          {t('map.menuNewMap')}
        </button>
        {maps.map((map) => (
          <button
            key={map.id}
            className={`dashboard-range${activeId === map.id ? ' active' : ''}`}
            onClick={() => setActiveId(map.id)}
          >
            {map.name}
          </button>
        ))}
      </div>
      {maps.length === 0 ? (
        <p className="codex-empty">{t('map.emptyState')}</p>
      ) : (
        <iframe
          ref={iframeRef}
          className="editor-frame"
          src="./map/map.html"
          title="map"
          sandbox="allow-scripts allow-same-origin"
        />
      )}
      {creating && (
        <InputDialog
          title={t('map.menuNewMap')}
          onCancel={() => setCreating(false)}
          onSubmit={(name) => {
            setCreating(false)
            void rpc
              .request<{ id: string }>('maps/create', [name])
              .then(async (created) => {
                const list = await rpc.request<MapRefDto[]>('maps/list')
                setMaps(list)
                setActiveId(created.id)
              })
          }}
        />
      )}
    </div>
  )
}
