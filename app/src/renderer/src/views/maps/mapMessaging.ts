import { type MapViewState } from './mapViewState'
import { IMAGE_BASE_URL, type MapMessage } from './mapViewTypes'
import { useCallback, useEffect } from 'react'
import { rpc } from '../../rpc/client'
import { type MapDataT } from './mapModel'
import { useMapPersistence } from './mapPersistence'
import { type MapMessagesContext, dispatchMapMessage } from './mapMessageHandlers'

type MapSelectionContext = Pick<MapViewState, 'setPeek' | 'setLoading3D' | 'mapModelRef' | 'setSelectedNodeId'> &
  Pick<ReturnType<typeof useMapPersistence>, 'getWin'>

export function useMapSelection(context: MapSelectionContext) {
  const {
    setPeek, setLoading3D, mapModelRef, setSelectedNodeId, getWin
  } = context
  const showPinPeek = useCallback(async (entityType: string, entityId: string): Promise<void> => {
    try {
      const list = await rpc.request<
        { id: string; name: string; detail: string; imagePath: string | null }[]
      >('entities/list', [entityType])
      const found = list.find((e) => e.id === entityId)
      if (!found) return
      setPeek({
        name: found.name,
        detail: found.detail ?? '',
        imageUrl: found.imagePath ? IMAGE_BASE_URL + found.imagePath : null
      })
    } catch {
      /* entity type unknown / project closed */
    }
  }, [])

  const start3DLoading = useCallback((status: string, progress: number): void => {
    setLoading3D({ status, progress })
  }, [])

  const selectImageOwner = useCallback(
    (imageId: string): void => {
      const data = mapModelRef.current
      if (!data) return
      let ownerId: string | null = null
      const walk = (nodes: MapDataT['layers']): void => {
        for (const n of nodes) {
          if (!ownerId && (n.images ?? []).some((i) => i.id === imageId)) ownerId = n.id
          if (n.children?.length) walk(n.children)
        }
      }
      walk(data.layers)
      if (ownerId) {
        setSelectedNodeId(ownerId)
        getWin()?.setActiveLayer(ownerId)
      }
    },
    [getWin]
  )
  return { showPinPeek, start3DLoading, selectImageOwner }
}

export function useMapMessages(context: MapMessagesContext) {
  const {
    readyRef, pushStringsAndOptions, pushMap, saveTimer, getWin, retain, refreshModelFromView,
    setActiveTool, setSelection, selectImageOwner, setActiveId, showPinPeek, setMeasured, t,
    start3DLoading, setIs3D, setLoading3D, apiRef, iframeRef
  } = context
  const handleMessage = useCallback(
    (msg: MapMessage): void => {
      dispatchMapMessage(msg, { readyRef, pushStringsAndOptions, pushMap, saveTimer, getWin, retain, refreshModelFromView, setActiveTool, setSelection, selectImageOwner, setActiveId, showPinPeek, setMeasured, t, start3DLoading, setIs3D, setLoading3D })
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [
      getWin,
      pushMap,
      pushStringsAndOptions,
      refreshModelFromView,
      retain,
      showPinPeek,
      start3DLoading,
      t
    ]
  )

  apiRef.current = handleMessage

  useEffect(() => {
    // Read the iframe inside the handler (not at mount): on the first render the
    // map list is still empty, so the iframe is not in the DOM yet and capturing
    // iframeRef.current here would be null — the listener would never attach, the
    // "ready" handshake would be missed, and the map would stay blank.
    const onMessage = (event: MessageEvent): void => {
      const iframe = iframeRef.current
      if (!iframe || event.source !== iframe.contentWindow) return
      const raw = (event.data as { novalistMap?: string })?.novalistMap
      if (typeof raw !== 'string') return
      let message: MapMessage
      try {
        message = JSON.parse(raw) as MapMessage
      } catch {
        return
      }
      apiRef.current?.(message)
    }
    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      readyRef.current = false
      if (saveTimer.current) clearTimeout(saveTimer.current)
    }
  }, [])
  return { handleMessage }
}
