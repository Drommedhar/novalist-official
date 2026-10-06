import { type MapViewState } from './mapViewState'
import { type MapRefDto } from './mapViewTypes'
import { useCallback } from 'react'
import { rpc } from '../../rpc/client'
import { useMapPersistence } from './mapPersistence'
import { useMapLoading } from './mapLoading'

type MapListActionsContext = Pick<ReturnType<typeof useMapPersistence>, 'getWin'> &
  Pick<MapViewState, 'is3D' | 'setLoading3D' | 't' | 'setCreating' | 'setMaps' | 'setActiveId' | 'setRenaming' | 'activeId' | 'setConfirmingDelete'>

export function useMapListActions(context: MapListActionsContext) {
  const {
    getWin, is3D, setLoading3D, t, setCreating, setMaps, setActiveId, setRenaming, activeId,
    setConfirmingDelete
  } = context
  const toggle3D = useCallback((): void => {
    const win = getWin()
    if (!win?.Map3D) return
    if (is3D) {
      win.Map3D.exit()
    } else {
      setLoading3D({ status: t('map.loading3DInitialising'), progress: 0.02 })
      win.Map3D.enter()
    }
  }, [getWin, is3D, t])

  const onCreateMap = useCallback((name: string): void => {
    setCreating(false)
    void rpc.request<{ id: string }>('maps/create', [name]).then(async (created) => {
      const list = await rpc.request<MapRefDto[]>('maps/list')
      setMaps(list)
      setActiveId(created.id)
    })
  }, [])

  const onRenameMap = useCallback(
    (name: string): void => {
      setRenaming(false)
      if (!activeId) return
      void rpc.request<MapRefDto[]>('maps/rename', [activeId, name]).then(setMaps)
    },
    [activeId]
  )

  const onDeleteMap = useCallback((): void => {
    setConfirmingDelete(false)
    if (!activeId) return
    void rpc.request<MapRefDto[]>('maps/delete', [activeId]).then((list) => {
      setMaps(list)
      setActiveId(list.length > 0 ? list[0].id : null)
    })
  }, [activeId])
  return { toggle3D, onCreateMap, onRenameMap, onDeleteMap }
}

type MapDocumentActionsContext = Pick<MapViewState, 'maps' | 'activeId' | 'setGenerating' | 'iframeRef' | 'setExporting' | 'exportScale'> &
  Pick<ReturnType<typeof useMapLoading>, 'pushMap'>

export function createMapDocumentActions(context: MapDocumentActionsContext) {
  const {
    maps, activeId, setGenerating, pushMap, iframeRef, setExporting, exportScale
  } = context
  const activeMap = maps.find((m) => m.id === activeId) ?? null

  const hasMap = !!activeId

  const generateTerrain = async (): Promise<void> => {
    if (!activeId) return
    setGenerating(true)
    try {
      const seed = Math.floor(Math.random() * 100000)
      await rpc.request<{ json: string } | null>('maps/generateTerrain', [
        activeId,
        seed,
        1600,
        1200
      ])
      // Read it back the way every other change is read back, so the canvas
      // cannot end up showing something the file does not say.
      await pushMap()
    } finally {
      setGenerating(false)
    }
  }

  const exportImage = async (): Promise<void> => {
    const iframe = iframeRef.current
    if (!iframe || !activeMap) return

    const outputPath = await window.novalist.saveFile(`${activeMap.name || 'map'}.png`)
    if (!outputPath) return

    setExporting(true)
    try {
      const rect = iframe.getBoundingClientRect()
      await window.novalist.captureRegion(
        { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
        outputPath,
        exportScale
      )
    } finally {
      setExporting(false)
    }
  }
  return { activeMap, hasMap, generateTerrain, exportImage }
}
