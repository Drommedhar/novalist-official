import { useHostBridgeStore } from '../../stores/hostBridgeStore'
import { resolveProjectAssetUrl } from '../../mobile/projectImages'
import { type MapViewState } from './mapViewState'
import { IMAGE_BASE_URL } from './mapViewTypes'
import { useCallback } from 'react'
import { rpc } from '../../rpc/client'
import { type ToolMode } from './mapModel'
import { useMapPersistence } from './mapPersistence'

type MapToolsContext = Pick<ReturnType<typeof useMapPersistence>, 'getWin'> &
  Pick<MapViewState, 'setActiveTool' | 'setBuildingScale'>

export function useMapTools(context: MapToolsContext) {
  const {
    getWin, setActiveTool, setBuildingScale
  } = context
  const selectTool = useCallback(
    (tool: ToolMode): void => {
      const win = getWin()
      if (!win) return
      setActiveTool(tool)
      win.setToolMode(tool)
    },
    [getWin]
  )

  const onSplinePreset = useCallback(
    (kind: string, preset: string): void => {
      const win = getWin()
      if (!win) return
      win.setSplineDraftType(kind, preset)
      win.setToolMode('spline')
      setActiveTool('spline')
    },
    [getWin]
  )

  const onTerrain = useCallback(
    (type: string): void => {
      const win = getWin()
      if (!win) return
      win.setTerrainDraftType(type)
      win.setToolMode('terrain')
      setActiveTool('terrain')
    },
    [getWin]
  )

  const onBuilding = useCallback(
    (type: string): void => {
      const win = getWin()
      if (!win) return
      win.setBuildingDraftType(type)
      win.setToolMode('building')
      setActiveTool('building')
    },
    [getWin]
  )

  const onBuildingScale = useCallback(
    (scale: number): void => {
      setBuildingScale(scale)
      getWin()?.setBuildingScale(scale)
    },
    [getWin]
  )
  return { selectTool, onSplinePreset, onTerrain, onBuilding, onBuildingScale }
}

type MapImagesContext = Pick<MapViewState, 'setImagePicker'> &
  Pick<ReturnType<typeof useMapPersistence>, 'getWin'>

export function useMapImages(context: MapImagesContext) {
  const {
    setImagePicker, getWin
  } = context
  const onAddImage = useCallback((): void => {
    void rpc.request<{ path: string; url: string }[]>('gallery/list').then((imgs) =>
      setImagePicker(
        imgs.map((img) => ({
          ...img,
          // The RPC's url is project-relative. Both the preview and the size
          // probe need the project protocol; path stays book-relative for saving.
          url: IMAGE_BASE_URL + img.url.split('/').map(encodeURIComponent).join('/')
        }))
      )
    )
  }, [])

  const placeImage = useCallback(
    (path: string, url: string): void => {
      const win = getWin()
      setImagePicker(null)
      if (!win) return
      const probe = new Image()
      probe.onload = () => win.addImageToMap(path, probe.naturalWidth, probe.naturalHeight)
      probe.onerror = () => win.addImageToMap(path, 0, 0)
      void resolveProjectAssetUrl(url).then((resolved) => {
        if (resolved && getWin() === win) probe.src = resolved
      }).catch((error: unknown) => {
        if (getWin() === win) useHostBridgeStore.getState().pushToast(String(error))
      })
    },
    [getWin]
  )
  return { onAddImage, placeImage }
}
