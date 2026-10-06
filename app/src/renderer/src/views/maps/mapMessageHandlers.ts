import { type MapViewState } from './mapViewState'
import { MAP_AUTOSAVE_MS, type MapMessage } from './mapViewTypes'
import { useMapPersistence } from './mapPersistence'
import { useMapStrings, useMapLoading } from './mapLoading'
import { useMapSelection } from './mapMessaging'

export type MapMessagesContext = Pick<MapViewState, 'readyRef' | 'saveTimer' | 'setActiveTool' | 'setSelection' | 'setActiveId' | 'setMeasured' | 't' | 'setIs3D' | 'setLoading3D' | 'apiRef' | 'iframeRef'> &
  Pick<ReturnType<typeof useMapStrings>, 'pushStringsAndOptions'> &
  Pick<ReturnType<typeof useMapLoading>, 'pushMap'> &
  Pick<ReturnType<typeof useMapPersistence>, 'getWin' | 'retain' | 'refreshModelFromView'> &
  Pick<ReturnType<typeof useMapSelection>, 'selectImageOwner' | 'showPinPeek' | 'start3DLoading'>

type MapMessageHandlerContext = Pick<MapMessagesContext, 'readyRef' | 'pushStringsAndOptions' | 'pushMap' | 'saveTimer' | 'getWin' | 'retain' | 'refreshModelFromView' | 'setActiveTool' | 'setSelection' | 'selectImageOwner' | 'setActiveId' | 'showPinPeek' | 'setMeasured' | 't' | 'start3DLoading' | 'setIs3D' | 'setLoading3D'>

type MapMessageHandler = (msg: MapMessage, context: MapMessageHandlerContext) => void

function handleMapFrameMessage(msg: MapMessage, context: MapMessageHandlerContext): void {
  const {
    readyRef, pushStringsAndOptions, pushMap, saveTimer, getWin, retain, refreshModelFromView,
    setActiveTool
  } = context
  switch (msg.type) {
case 'ready':
          readyRef.current = true
          pushStringsAndOptions()
          void pushMap()
          break
case 'mapChanged':
          if (saveTimer.current) clearTimeout(saveTimer.current)
          saveTimer.current = setTimeout(() => {
            saveTimer.current = null
            const win = getWin()
            if (!win || typeof win.getMapData !== 'function') return
            const json = win.getMapData()
            retain(json)
          }, MAP_AUTOSAVE_MS)
          refreshModelFromView()
          break
case 'viewChanged':
          if (saveTimer.current) clearTimeout(saveTimer.current)
          saveTimer.current = setTimeout(() => {
            saveTimer.current = null
            const win = getWin()
            if (win && typeof win.getMapData === 'function') {
              const json = win.getMapData()
              retain(json)
            }
          }, MAP_AUTOSAVE_MS)
          break
case 'placePinAt': {
          const win = getWin()
          if (win) win.addPinAtPoint(msg.x ?? 0, msg.y ?? 0, '', '', '', '')
          setActiveTool('select')
          break
        }
  }
}

function handleMapSelectionMessage(msg: MapMessage, context: MapMessageHandlerContext): void {
  const {
    setActiveTool, setSelection, selectImageOwner
  } = context
  switch (msg.type) {
case 'imageSelected':
          setSelection({ kind: 'image' })
          if (msg.imageId) selectImageOwner(msg.imageId)
          break
case 'pinSelected':
          setSelection({ kind: 'pin' })
          break
case 'labelSelected':
          setSelection({ kind: 'label' })
          break
case 'splineSelected':
          setSelection({ kind: 'spline' })
          break
case 'shapeSelected':
          setSelection({ kind: 'shape' })
          break
case 'buildingSelected':
          setSelection({ kind: 'building' })
          break
case 'borderSelected':
          setSelection({ kind: 'border' })
          break
case 'selectionCleared':
case 'pinDeselected':
case 'labelDeselected':
case 'splineDeselected':
case 'shapeDeselected':
case 'buildingDeselected':
case 'borderDeselected':
          setSelection(null)
          break
case 'cancelPinPlace':
case 'cancelLabelPlace':
case 'cancelSplineMode':
case 'cancelTerrainMode':
case 'cancelBuildingMode':
case 'cancelBorderMode':
          setActiveTool('select')
          break
  }
}

function handleMapNavigationMessage(msg: MapMessage, context: MapMessageHandlerContext): void {
  const {
    setActiveId, showPinPeek, setMeasured, t
  } = context
  switch (msg.type) {
case 'pinClick':
          // A pin that opens a map wins over one that opens an entry: the
          // writer put a target on it precisely so it would lead somewhere.
          if (msg.targetMapId) {
            setActiveId(msg.targetMapId)
            break
          }
          if (msg.entityId && msg.entityType) void showPinPeek(msg.entityType, msg.entityId)
          break
case 'measured':
          // Answered in the map's own units, and said so - a number with no
          // unit behind it is the problem the scale exists to solve.
          setMeasured(
            msg.ground && msg.unit
              ? t('maps.measuredGround', {
                  distance: Number(msg.ground).toFixed(1),
                  unit: msg.unit
                })
              : t('maps.measuredUnits', { distance: Number(msg.worldUnits ?? 0).toFixed(0) })
          )
          break
  }
}

function handleMapThreeDMessage(msg: MapMessage, context: MapMessageHandlerContext): void {
  const {
    t, start3DLoading, setIs3D, setLoading3D
  } = context
  switch (msg.type) {
case 'map3dLoading':
          start3DLoading(t('map.loading3DInitialising'), 0.05)
          break
case 'map3dStep':
          switch (msg.step) {
            case 'before-build':
              start3DLoading(t('map.loading3DAssets'), 0.1)
              break
            case 'after-tree-assets':
              start3DLoading(t('map.loading3DScene'), 0.55)
              break
            case 'after-build':
              start3DLoading(t('map.loading3DCamera'), 0.85)
              break
            case 'after-frame':
              start3DLoading(t('map.loading3DAlmost'), 0.95)
              break
          }
          break
case 'map3dEntered':
          setIs3D(true)
          setLoading3D(null)
          break
case 'map3dExited':
          setIs3D(false)
          setLoading3D(null)
          break
case 'map3dError':
          setIs3D(false)
          setLoading3D(null)
          break
  }
}

const MAP_MESSAGE_HANDLERS = new Map<string, MapMessageHandler>([
  ['ready', handleMapFrameMessage],
  ['mapChanged', handleMapFrameMessage],
  ['viewChanged', handleMapFrameMessage],
  ['placePinAt', handleMapFrameMessage],
  ['imageSelected', handleMapSelectionMessage],
  ['pinSelected', handleMapSelectionMessage],
  ['labelSelected', handleMapSelectionMessage],
  ['splineSelected', handleMapSelectionMessage],
  ['shapeSelected', handleMapSelectionMessage],
  ['buildingSelected', handleMapSelectionMessage],
  ['borderSelected', handleMapSelectionMessage],
  ['pinClick', handleMapNavigationMessage],
  ['measured', handleMapNavigationMessage],
  ['selectionCleared', handleMapSelectionMessage],
  ['pinDeselected', handleMapSelectionMessage],
  ['labelDeselected', handleMapSelectionMessage],
  ['splineDeselected', handleMapSelectionMessage],
  ['shapeDeselected', handleMapSelectionMessage],
  ['buildingDeselected', handleMapSelectionMessage],
  ['borderDeselected', handleMapSelectionMessage],
  ['cancelPinPlace', handleMapSelectionMessage],
  ['cancelLabelPlace', handleMapSelectionMessage],
  ['cancelSplineMode', handleMapSelectionMessage],
  ['cancelTerrainMode', handleMapSelectionMessage],
  ['cancelBuildingMode', handleMapSelectionMessage],
  ['cancelBorderMode', handleMapSelectionMessage],
  ['map3dLoading', handleMapThreeDMessage],
  ['map3dStep', handleMapThreeDMessage],
  ['map3dEntered', handleMapThreeDMessage],
  ['map3dExited', handleMapThreeDMessage],
  ['map3dError', handleMapThreeDMessage]
])

export function dispatchMapMessage(msg: MapMessage, context: MapMessageHandlerContext): void {
  MAP_MESSAGE_HANDLERS.get(msg.type)?.(msg, context)
}
