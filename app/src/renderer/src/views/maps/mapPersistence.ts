import { type MapViewState } from './mapViewState'
import { MAP_WRITE_KEY } from './mapViewTypes'
import { useCallback, useEffect } from 'react'
import { rpc } from '../../rpc/client'
import { persistPendingWrite, registerPendingWrite, retainPendingWrite } from '../../stores/pendingWrites'
import { type MapDataT, type MapWindow } from './mapModel'

type MapPersistenceContext = Pick<MapViewState, 'iframeRef' | 'saveTimer' | 'readyRef' | 'setMapModel'>

export function useMapPersistence(context: MapPersistenceContext) {
  const {
    iframeRef, saveTimer, readyRef, setMapModel
  } = context
  const getWin = useCallback((): MapWindow | null => {
    return (iframeRef.current?.contentWindow as MapWindow | null) ?? null
  }, [])

  const persist = useCallback((json: string): Promise<void> => {
    return persistPendingWrite(MAP_WRITE_KEY, () => rpc.request('maps/save', [json]))
  }, [])

  const retain = useCallback((json: string): void => {
    retainPendingWrite(MAP_WRITE_KEY, () => rpc.request('maps/save', [json]))
  }, [])

  const flushPendingMapSave = useCallback(async (): Promise<void> => {
    if (saveTimer.current) clearTimeout(saveTimer.current)
    saveTimer.current = null
    const win = getWin()
    if (!win || !readyRef.current || typeof win.getMapData !== 'function') return
    // Read the iframe rather than React state: a final pointer event can reach
    // the map before its postMessage reaches the host.
    await persist(win.getMapData())
  }, [getWin, persist])

  useEffect(() => registerPendingWrite(flushPendingMapSave), [flushPendingMapSave])

  useEffect(
    () => () => {
      if (saveTimer.current) clearTimeout(saveTimer.current)
      const win = getWin()
      if (!win || !readyRef.current || typeof win.getMapData !== 'function') return
      const json = win.getMapData()
      retain(json)
    },
    [getWin, retain]
  )

  const commitMap = useCallback(
    (mutate: (data: MapDataT) => void, debounceSaveMs?: number): void => {
      const win = getWin()
      if (!win || typeof win.getMapData !== 'function') return
      let data: MapDataT
      try {
        data = JSON.parse(win.getMapData()) as MapDataT
      } catch {
        return
      }
      mutate(data)
      const json = JSON.stringify(data)
      win.setMapData(json)
      setMapModel(data)
      if (debounceSaveMs) {
        if (saveTimer.current) clearTimeout(saveTimer.current)
        saveTimer.current = setTimeout(() => {
          saveTimer.current = null
          retain(json)
        }, debounceSaveMs)
      } else {
        retain(json)
      }
    },
    [getWin, retain]
  )

  const refreshModelFromView = useCallback((): void => {
    const win = getWin()
    if (!win || typeof win.getMapData !== 'function') return
    try {
      setMapModel(JSON.parse(win.getMapData()) as MapDataT)
    } catch {
      /* ignore malformed */
    }
  }, [getWin])
  return { getWin, persist, retain, flushPendingMapSave, commitMap, refreshModelFromView }
}
