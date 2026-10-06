import { type MapViewState } from './mapViewState'
import { IMAGE_BASE_URL, type MapRefDto, type EntityOption } from './mapViewTypes'
import { useCallback, useEffect } from 'react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { firstLeafId } from './LayerPanel'
import { buildMapStrings, type MapDataT } from './mapModel'
import { useMapPersistence } from './mapPersistence'

async function loadEntityOptions(): Promise<EntityOption[]> {
  const baseTypes = ['character', 'location', 'item', 'lore']
  let customTypes: string[] = []
  try {
    const custom = await rpc.request<{ typeKey: string }[]>('entities/customTypes')
    customTypes = custom.map((c) => c.typeKey)
  } catch {
    customTypes = []
  }
  const results = await Promise.all(
    [...baseTypes, ...customTypes].map(async (type) => {
      try {
        const list = await rpc.request<{ id: string; name: string }[]>('entities/list', [type])
        return list.map((e) => ({ id: e.id, type, name: e.name }))
      } catch {
        return []
      }
    })
  )
  return results.flat()
}

type MapStringsContext = Pick<ReturnType<typeof useMapPersistence>, 'getWin'> &
  Pick<MapViewState, 't' | 'entityOptionsRef'>

export function useMapStrings(context: MapStringsContext) {
  const {
    getWin, t, entityOptionsRef
  } = context
  const pushStringsAndOptions = useCallback((): void => {
    const win = getWin()
    if (!win) return
    win.setContextMenuLabels(
      t('map.imageMenuMove'),
      t('map.imageMenuClip'),
      t('map.imageMenuDelete')
    )
    win.setMapStrings(buildMapStrings(t))
    win.setEntityOptions(JSON.stringify(entityOptionsRef.current))
  }, [getWin, t])
  return { pushStringsAndOptions }
}

type MapLoadingContext = Pick<ReturnType<typeof useMapPersistence>, 'getWin'> &
  Pick<MapViewState, 'readyRef' | 'activeId' | 'editMode' | 'setMapModel' | 'maps' | 'setSelectedNodeId' | 'pendingFocusPinRef' | 't'>

export function useMapLoading(context: MapLoadingContext) {
  const {
    getWin, readyRef, activeId, editMode, setMapModel, maps, setSelectedNodeId,
    pendingFocusPinRef, t
  } = context
  const pushMap = useCallback(async (): Promise<void> => {
    const win = getWin()
    if (!win || !readyRef.current || !activeId) return
    const loaded = await rpc.request<{ json: string } | null>('maps/load', [activeId])
    if (!loaded) return
    // Map image paths are book-root-relative; prefix the active book folder so
    // the project-rooted protocol resolves them (same scope fix as entity images).
    const base = await rpc.request<string>('maps/imageBase').catch(() => '')
    win.setImageBaseUrl(base ? `${IMAGE_BASE_URL}${encodeURI(base)}/` : IMAGE_BASE_URL)
    win.setMapData(loaded.json)
    win.setMode(editMode ? 'edit' : 'view')
    let data: MapDataT | null = null
    try {
      data = JSON.parse(loaded.json) as MapDataT
    } catch {
      data = null
    }
    setMapModel(data)
    // The map file knows nothing about its siblings, so the host tells it what
    // else a pin could open. Told separately from whether the map is on screen:
    // this threw on any load with nothing selected, and everything below it -
    // the active layer, the resize, the fit to view - went with it.
    try {
      if (typeof win.setOtherMaps === 'function') {
        win.setOtherMaps(
          maps.filter((m) => m.id !== activeId).map((m) => ({ id: m.id, name: m.name }))
        )
      }
    } catch {
      /* a map that cannot list its siblings is still a map */
    }
    if (data) {
      const leaf = firstLeafId(data)
      if (leaf) {
        win.setActiveLayer(leaf)
        setSelectedNodeId((prev) => prev ?? leaf)
      }
    }
    // The engine may have initialised its stage before the view had a size;
    // nudge a resize, then fit once the base images have decoded. This is the
    // half that makes the map visible, and it now runs whatever happened above.
    try {
      win.dispatchEvent(new Event('resize'))
    } catch {
      /* ignore */
    }
    window.setTimeout(() => {
      const w = getWin()
      try {
        w?.dispatchEvent(new Event('resize'))
        // A deep-linked pin (from the focus-peek card) wins over fit-to-view.
        if (pendingFocusPinRef.current && typeof w?.focusOnPin === 'function') {
          w.focusOnPin(pendingFocusPinRef.current)
          pendingFocusPinRef.current = null
        } else {
          w?.zoomToFit()
        }
      } catch {
        /* ignore */
      }
    }, 600)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [getWin, activeId, editMode, t])
  return { pushMap }
}

type MapSubscriptionsContext = Pick<MapViewState, 'setMaps' | 'setActiveId' | 'entityOptionsRef' | 'readyRef' | 'setSelectedNodeId' | 'setSelection' | 'setIsolated' | 'activeId' | 'editMode' | 'pendingFocusPinRef'> &
  Pick<ReturnType<typeof useMapPersistence>, 'getWin'> &
  Pick<ReturnType<typeof useMapLoading>, 'pushMap'>

export function useMapSubscriptions(context: MapSubscriptionsContext) {
  const {
    setMaps, setActiveId, entityOptionsRef, getWin, readyRef, setSelectedNodeId, setSelection,
    setIsolated, pushMap, activeId, editMode, pendingFocusPinRef
  } = context
  useEffect(() => {
    void rpc.request<MapRefDto[]>('maps/list').then((list) => {
      setMaps(list)
      setActiveId((prev) => prev ?? (list.length > 0 ? list[0].id : null))
    })
    void loadEntityOptions().then((opts) => {
      entityOptionsRef.current = opts
      const win = getWin()
      if (win && readyRef.current) win.setEntityOptions(JSON.stringify(opts))
    })
  }, [getWin])

  useEffect(() => {
    setSelectedNodeId(null)
    setSelection(null)
    setIsolated(null)
    void pushMap()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId])

  useEffect(() => {
    const win = getWin()
    if (win && readyRef.current) win.setMode(editMode ? 'edit' : 'view')
  }, [editMode, getWin])

  const pendingMapNav = useShellStore((s) => s.pendingMapNav)

  useEffect(() => {
    if (!pendingMapNav) return
    const { mapId, pinId } = pendingMapNav
    useShellStore.getState().clearPendingMapNav()
    if (mapId === activeId) {
      const win = getWin()
      if (win && typeof win.focusOnPin === 'function') win.focusOnPin(pinId)
    } else {
      pendingFocusPinRef.current = pinId
      setActiveId(mapId)
    }
  }, [pendingMapNav, activeId, getWin])
  return { pendingMapNav }
}
