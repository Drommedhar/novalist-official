import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Plus,
  Pencil,
  Trash2,
  Maximize,
  Crosshair,
  Box,
  Scissors,
  Spline as SplineIcon,
  ImageDown,
  Mountain,
  Eye,
  X
} from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import {
  persistPendingWrite,
  registerPendingWrite,
  retainPendingWrite
} from '../../stores/pendingWrites'
import { InputDialog } from '../../shell/InputDialog'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { ToolRail } from './ToolRail'
import { LayerPanel, firstLeafId } from './LayerPanel'
import {
  buildMapStrings,
  deleteNode as deleteNodeInTree,
  findNode,
  moveNode,
  newId,
  type DropPosition,
  type ElementKind,
  type MapDataT,
  type MapProfileT,
  type MapWindow,
  type ToolMode
} from './mapModel'
import { MapScaleDialog } from './MapScaleDialog'
import './map.css'

const MAP_WRITE_KEY = 'maps:document'

interface MapRefDto {
  id: string
  name: string
}

interface EntityOption {
  id: string
  type: string
  name: string
}

interface PeekData {
  name: string
  detail: string
  imageUrl: string | null
}

interface Loading3D {
  progress: number
  status: string
}

const MAP_AUTOSAVE_MS = 1200
const IMAGE_BASE_URL = 'novalist-project://nl/'

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

export function MapsView(): React.JSX.Element {
  const { t } = useTranslation()
  const [maps, setMaps] = useState<MapRefDto[]>([])
  const [activeId, setActiveId] = useState<string | null>(null)
  const [mapModel, setMapModel] = useState<MapDataT | null>(null)
  const [activeTool, setActiveTool] = useState<ToolMode>('select')
  // The last distance the ruler reported, shown until the next measurement or
  // until the tool is put down.
  const [measured, setMeasured] = useState<string | null>(null)
  const [scaleOpen, setScaleOpen] = useState(false)
  const [editMode, setEditMode] = useState(true)
  const [is3D, setIs3D] = useState(false)
  const [loading3D, setLoading3D] = useState<Loading3D | null>(null)
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const [isolated, setIsolated] = useState<{ kind: string; id: string } | null>(null)
  const [selection, setSelection] = useState<{ kind: string } | null>(null)
  const [peek, setPeek] = useState<PeekData | null>(null)
  const [buildingScale, setBuildingScale] = useState(1)
  const [creating, setCreating] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [generating, setGenerating] = useState(false)
  // 1x is what is on screen; 2x and 4x give a raster fit for endpapers or an
  // EPUB rather than whatever size the window happened to be.
  const [exportScale, setExportScale] = useState(2)
  const [renaming, setRenaming] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [imagePicker, setImagePicker] = useState<{ path: string; url: string }[] | null>(null)

  const iframeRef = useRef<HTMLIFrameElement>(null)
  const readyRef = useRef(false)
  // Pin to centre once the next map load finishes (focus-peek "ON MAPS" deep link).
  const pendingFocusPinRef = useRef<string | null>(null)
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const entityOptionsRef = useRef<EntityOption[]>([])
  const mapModelRef = useRef<MapDataT | null>(null)
  mapModelRef.current = mapModel
  const apiRef = useRef<{ onMapMessage(msg: MapMessage): void }>({
    onMapMessage: () => {}
  })

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

  useEffect(
    () => registerPendingWrite(flushPendingMapSave),
    [flushPendingMapSave]
  )

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

  /** Read authoritative state from the canvas, mutate, push back + persist. */
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

  const pushStringsAndOptions = useCallback((): void => {
    const win = getWin()
    if (!win) return
    win.setContextMenuLabels(t('map.imageMenuMove'), t('map.imageMenuClip'), t('map.imageMenuDelete'))
    win.setMapStrings(buildMapStrings(t))
    win.setEntityOptions(JSON.stringify(entityOptionsRef.current))
  }, [getWin, t])

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

  // ── Map list ────────────────────────────────────────────────────────────
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

  // Reload the active map whenever the selection changes.
  useEffect(() => {
    setSelectedNodeId(null)
    setSelection(null)
    setIsolated(null)
    void pushMap()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId])

  // Edit / view mode toggle.
  useEffect(() => {
    const win = getWin()
    if (win && readyRef.current) win.setMode(editMode ? 'edit' : 'view')
  }, [editMode, getWin])

  // Consume a focus-peek "ON MAPS" deep link: open the target map and centre the
  // pin. If the map is already active the reload effect won't fire, so focus now.
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

  // ── Focus peek fetch ──────────────────────────────────────────────────────
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

  // ── Inbound message routing ───────────────────────────────────────────────
  const start3DLoading = useCallback((status: string, progress: number): void => {
    setLoading3D({ status, progress })
  }, [])

  const selectImageOwner = useCallback((imageId: string): void => {
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
  }, [getWin])

  const handleMessage = useCallback(
    (msg: MapMessage): void => {
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
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [getWin, pushMap, pushStringsAndOptions, refreshModelFromView, retain, showPinPeek, start3DLoading, t]
  )

  apiRef.current.onMapMessage = handleMessage

  // Single, stable message listener; delegates through apiRef to avoid
  // re-subscribing (which would reset readyRef / autosave timers).
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
      apiRef.current.onMapMessage(message)
    }
    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      readyRef.current = false
      if (saveTimer.current) clearTimeout(saveTimer.current)
    }
  }, [])

  // ── Tool-rail handlers ────────────────────────────────────────────────────
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

  const onAddImage = useCallback((): void => {
    void rpc
      .request<{ path: string; url: string }[]>('gallery/list')
      .then((imgs) => setImagePicker(imgs))
  }, [])

  const placeImage = useCallback(
    (path: string, url: string): void => {
      const win = getWin()
      setImagePicker(null)
      if (!win) return
      const probe = new Image()
      probe.onload = () => win.addImageToMap(path, probe.naturalWidth, probe.naturalHeight)
      probe.onerror = () => win.addImageToMap(path, 0, 0)
      probe.src = url
    },
    [getWin]
  )

  // ── Layer-panel handlers ──────────────────────────────────────────────────
  const onSelectNode = useCallback(
    (id: string): void => {
      setSelectedNodeId(id)
      getWin()?.setActiveLayer(id)
    },
    [getWin]
  )

  const onToggleExpand = useCallback((id: string): void => {
    setExpanded((prev) => {
      const model = mapModelRef.current
      const node = model ? findNode(model, id) : null
      const current = prev[id] ?? node?.expanded ?? true
      return { ...prev, [id]: !current }
    })
  }, [])

  const onAddLayer = useCallback((): void => {
    let newNodeId = ''
    commitMap((data) => {
      newNodeId = newId('layer')
      data.layers.push({
        id: newNodeId,
        name: `Layer ${data.layers.length + 1}`,
        opacity: 1,
        locked: false,
        hidden: false,
        expanded: true,
        images: [],
        children: []
      })
    })
    if (newNodeId) onSelectNode(newNodeId)
  }, [commitMap, onSelectNode])

  const onAddChild = useCallback(
    (parentId: string): void => {
      let newNodeId = ''
      commitMap((data) => {
        const parent = findNode(data, parentId)
        if (!parent) return
        parent.children = parent.children ?? []
        newNodeId = newId('layer')
        parent.children.push({
          id: newNodeId,
          name: `Layer ${parent.children.length + 1}`,
          opacity: 1,
          locked: false,
          hidden: false,
          expanded: true,
          images: [],
          children: []
        })
        parent.expanded = true
      })
      setExpanded((prev) => ({ ...prev, [parentId]: true }))
      if (newNodeId) onSelectNode(newNodeId)
    },
    [commitMap, onSelectNode]
  )

  const onDeleteNode = useCallback(
    (id: string): void => {
      commitMap((data) => {
        deleteNodeInTree(data, id)
      })
      setSelectedNodeId((prev) => (prev === id ? null : prev))
    },
    [commitMap]
  )

  const onRenameNode = useCallback(
    (id: string, name: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.name = name
      })
    },
    [commitMap]
  )

  const onToggleHidden = useCallback(
    (id: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.hidden = !node.hidden
      })
    },
    [commitMap]
  )

  const onToggleLocked = useCallback(
    (id: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.locked = !node.locked
      })
    },
    [commitMap]
  )

  const onMoveNode = useCallback(
    (dragId: string, targetId: string, pos: DropPosition): void => {
      commitMap((data) => {
        moveNode(data, dragId, targetId, pos)
      })
    },
    [commitMap]
  )

  const onMoveToRoot = useCallback(
    (dragId: string): void => {
      commitMap((data) => {
        const node = findNode(data, dragId)
        if (!node) return
        deleteNodeInTree(data, dragId)
        data.layers.push(node)
      })
    },
    [commitMap]
  )

  const onSetOpacity = useCallback(
    (id: string, opacity: number): void => {
      const clamped = Math.round(Math.max(0, Math.min(1, opacity)) * 100) / 100
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.opacity = clamped
      }, 400)
    },
    [commitMap]
  )

  const onSetNodeZoom = useCallback(
    (id: string, min: number, max: number): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (!node) return
        node.minZoom = min > 0 ? min : null
        node.maxZoom = max > 0 ? max : null
      }, 400)
    },
    [commitMap]
  )

  const onSetFloorMode = useCallback(
    (id: string, on: boolean): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (!node) return
        node.isConnectedSet = on
        if (on && !node.defaultMemberLayerId && node.children?.length)
          node.defaultMemberLayerId = node.children[0].id
      })
    },
    [commitMap]
  )

  const onSetActiveFloor = useCallback(
    (id: string, memberId: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.defaultMemberLayerId = memberId || null
      })
    },
    [commitMap]
  )

  const onSetElementZoom = useCallback(
    (kind: ElementKind, id: string, min: number, max: number): void => {
      const win = getWin()
      if (!win) return
      // Images use updateImageZoomRange; every other kind goes through
      // setElementZoomRange (map.html's elementById() has no image case). Both
      // emit mapChanged, which refreshes the panel model and persists.
      if (kind === 'image') win.updateImageZoomRange(id, min, max)
      else win.setElementZoomRange(kind, id, min, max)
    },
    [getWin]
  )

  const onToggleIsolate = useCallback(
    (kind: ElementKind, id: string): void => {
      const win = getWin()
      if (!win) return
      const nowOn = !(isolated && isolated.kind === kind && isolated.id === id)
      if (kind === 'image') win.setIsolatedImage(nowOn ? id : '')
      else win.setIsolatedElement(nowOn ? kind : '', nowOn ? id : '')
      setIsolated(nowOn ? { kind, id } : null)
    },
    [getWin, isolated]
  )

  // ── Toolbar actions ───────────────────────────────────────────────────────
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

  const activeMap = maps.find((m) => m.id === activeId) ?? null
  const hasMap = !!activeId

  /**
   * Writes the map to a PNG.
   *
   * The 2D map is a DOM tree with overlays and an SVG border, so there is no
   * single canvas to rasterise; the window capture sees exactly what the writer
   * sees, and works identically once the 3D view is showing.
   */
  /**
   * A first coastline for this map.
   *
   * The seed is shown after the fact rather than asked for up front: a writer
   * pressing this wants to see land, not to fill in a form. It goes into the
   * layer's name, so the one they liked can be made again.
   */
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

  return (
    <div className="mapsview">
      <div className="map-toolbar">
        <button className="map-tb-btn" onClick={() => setCreating(true)}>
          <Plus size={14} strokeWidth={2} />
          {t('map.menuNewMap')}
        </button>
        {/* Every coastline used to be drawn by hand from a blank canvas, which
            is the part of mapmaking that stops a writer who is not an
            illustrator. What comes out is ordinary shapes on a layer of their
            own, so the first move can be to drag a headland about. */}
        <button
          className="map-tb-btn"
          disabled={!activeId || generating}
          onClick={() => void generateTerrain()}
          title={t('map.generateTerrainHint')}
        >
          <Mountain size={14} strokeWidth={2} />
          {generating ? t('map.generating') : t('map.generateTerrain')}
        </button>
        <button
          className="map-tb-btn"
          disabled={!activeId || exporting}
          onClick={() => void exportImage()}
          title={t('map.exportImageHint')}
        >
          <ImageDown size={14} strokeWidth={2} />
          {exporting ? t('map.exporting') : t('map.exportImage')}
        </button>
        <select
          className="map-tb-select"
          value={exportScale}
          onChange={(e) => setExportScale(Number(e.target.value))}
          title={t('map.exportScaleHint')}
        >
          {[1, 2, 4].map((s) => (
            <option key={s} value={s}>
              {t('map.exportScaleOption', { scale: s })}
            </option>
          ))}
        </select>
        <div className="map-tabs">
          {maps.map((map) => (
            <button
              key={map.id}
              className={`map-tab${activeId === map.id ? ' active' : ''}`}
              onClick={() => setActiveId(map.id)}
            >
              {map.name}
            </button>
          ))}
        </div>
        <div className="map-tb-spacer" />
        <button
          className={`map-tb-icon${editMode ? ' active' : ''}`}
          title={t('map.modeToggle')}
          disabled={!hasMap || is3D}
          onClick={() => setEditMode((v) => !v)}
        >
          {editMode ? <Pencil size={15} /> : <Eye size={15} />}
        </button>
        <button
          className="map-tb-icon"
          title={t('map.toolZoomFitTooltip')}
          disabled={!hasMap}
          onClick={() => getWin()?.zoomToFit()}
        >
          <Maximize size={15} />
        </button>
        <button
          className="map-tb-icon"
          title={t('map.toolResetViewTooltip')}
          disabled={!hasMap}
          onClick={() => getWin()?.resetView()}
        >
          <Crosshair size={15} />
        </button>
        <button
          className={`map-tb-icon${is3D ? ' active' : ''}`}
          title={t('map.view3d')}
          disabled={!hasMap}
          onClick={toggle3D}
        >
          <Box size={15} />
        </button>
        <span className="map-tb-sep" />
        <button
          className="map-tb-icon"
          title={t('map.toolBorderTooltip')}
          disabled={!hasMap || is3D}
          onClick={() => selectTool('border')}
        >
          <SplineIcon size={15} />
        </button>
        <button
          className="map-tb-icon"
          title={t('map.editClipTooltip')}
          disabled={selection?.kind !== 'image' || is3D}
          onClick={() => getWin()?.toggleClipEditOnSelected()}
        >
          <Scissors size={15} />
        </button>
        <button
          className="map-tb-icon"
          title={t('map.splineEditHint')}
          disabled={selection?.kind !== 'spline' || is3D}
          onClick={() => getWin()?.toggleSplineEditOnSelected()}
        >
          <Pencil size={15} />
        </button>
        <button
          className="map-tb-icon danger"
          title={t('map.toolDeleteTooltip')}
          disabled={!selection || is3D}
          onClick={() => getWin()?.deleteSelected()}
        >
          <Trash2 size={15} />
        </button>
        <span className="map-tb-sep" />
        <button
          className="map-tb-icon"
          title={t('map.menuRenameMap')}
          disabled={!hasMap}
          onClick={() => setRenaming(true)}
        >
          <Pencil size={15} />
        </button>
        <button
          className="map-tb-icon danger"
          title={t('map.menuDeleteMap')}
          disabled={!hasMap}
          onClick={() => setConfirmingDelete(true)}
        >
          <Trash2 size={15} />
        </button>
      </div>

      {maps.length === 0 ? (
        <p className="codex-empty">{t('map.emptyState')}</p>
      ) : (
        <div className="map-body">
          {/* The drawing tools and the ruler are drawn over the map, and in 3D
              the map is a world you fly through - so a rail of greyed-out 2D
              tools sat on top of it and the measure bar landed across the sky
              controls. Disabled is right for a toolbar the writer is reading;
              something painted over the thing it cannot act on is better
              gone. */}
          {!is3D && (
            <ToolRail
              activeTool={activeTool}
              disabled={!hasMap || !editMode}
              buildingScale={buildingScale}
              customProfiles={(mapModel?.customProfiles ?? []) as MapProfileT[]}
              onSelectTool={selectTool}
              onAddImage={onAddImage}
              onSplinePreset={onSplinePreset}
              onTerrain={onTerrain}
              onBuilding={onBuilding}
              onBuildingScale={onBuildingScale}
            />
          )}
          <div className="map-stage">
            {/* Measuring is a question about the world, not an edit to it, so
                the ruler and the scale are reachable while reading too - but
                not in 3D, where they measure nothing and cover the sky
                controls. */}
            {!is3D && (
              <div className="map-measure-bar">
                <button
                  className={`dialog-button${activeTool === 'ruler' ? ' primary' : ''}`}
                  disabled={!hasMap}
                  onClick={() => {
                    const next = activeTool === 'ruler' ? 'select' : 'ruler'
                    setMeasured(null)
                    selectTool(next)
                  }}
                >
                  {t('maps.ruler')}
                </button>
                <button
                  className="dialog-button"
                  disabled={!hasMap}
                  onClick={() => setScaleOpen(true)}
                >
                  {t('maps.scale')}
                </button>
                {measured && <span className="map-measured">{measured}</span>}
              </div>
            )}
            <iframe
              ref={iframeRef}
              className="editor-frame"
              src="./map/map.html"
              title="map"
              /* allow-pointer-lock, or looking around in 3D is impossible: the
                 camera reads the pointer through a lock, and a sandbox without
                 this token refuses the request outright - so clicking into the
                 world and dragging did nothing, with no error a writer sees. */
              sandbox="allow-scripts allow-same-origin allow-pointer-lock"
            />
            {loading3D && (
              <div className="map-loading-overlay">
                <div className="map-loading-card">
                  <div className="map-loading-title">{t('map.loading3DTitle')}</div>
                  <div className="map-loading-status">{loading3D.status}</div>
                  <div className="map-loading-track">
                    <div
                      className="map-loading-bar"
                      style={{ width: `${Math.round(loading3D.progress * 100)}%` }}
                    />
                  </div>
                </div>
              </div>
            )}
            {peek && (
              <div className="map-peek" role="dialog">
                <button className="map-peek-close" onClick={() => setPeek(null)} title={t('dialog.cancel')}>
                  <X size={13} />
                </button>
                {peek.imageUrl && (
                  <img
                    className="map-peek-image"
                    src={peek.imageUrl}
                    alt=""
                    onError={(e) => {
                      ;(e.currentTarget as HTMLImageElement).style.display = 'none'
                    }}
                  />
                )}
                <div className="map-peek-name">{peek.name}</div>
                {peek.detail && <div className="map-peek-detail">{peek.detail}</div>}
              </div>
            )}
          </div>
          <LayerPanel
            data={mapModel}
            selectedNodeId={selectedNodeId}
            expanded={expanded}
            isolated={isolated}
            onSelectNode={onSelectNode}
            onToggleExpand={onToggleExpand}
            onAddLayer={onAddLayer}
            onAddChild={onAddChild}
            onDeleteNode={onDeleteNode}
            onRename={onRenameNode}
            onToggleHidden={onToggleHidden}
            onToggleLocked={onToggleLocked}
            onMoveNode={onMoveNode}
            onMoveToRoot={onMoveToRoot}
            onSetOpacity={onSetOpacity}
            onSetNodeZoom={onSetNodeZoom}
            onSetFloorMode={onSetFloorMode}
            onSetActiveFloor={onSetActiveFloor}
            onSetElementZoom={onSetElementZoom}
            onToggleIsolate={onToggleIsolate}
          />
        </div>
      )}

      {creating && (
        <InputDialog
          title={t('map.createTitle')}
          placeholder={t('map.createPrompt')}
          onCancel={() => setCreating(false)}
          onSubmit={onCreateMap}
        />
      )}
      {renaming && (
        <InputDialog
          title={t('map.renameTitle')}
          placeholder={t('map.renamePrompt')}
          onCancel={() => setRenaming(false)}
          onSubmit={onRenameMap}
        />
      )}
      {confirmingDelete && activeMap && (
        <ConfirmDialog
          title={t('map.deleteTitle')}
          message={t('map.deleteMessage').replace('{0}', activeMap.name)}
          onCancel={() => setConfirmingDelete(false)}
          onConfirm={onDeleteMap}
        />
      )}
      {imagePicker && (
        <div
          className="dialog-overlay"
          onPointerDown={(e) => e.target === e.currentTarget && setImagePicker(null)}
        >
          <div className="dialog-card map-image-picker" role="dialog">
            <div className="dialog-title">{t('map.toolAddImageTooltip')}</div>
            <div className="map-image-grid">
              {imagePicker.map((img) => (
                <button
                  key={img.path}
                  type="button"
                  className="map-image-choice"
                  onClick={() => placeImage(img.path, img.url)}
                >
                  <img src={img.url} alt="" />
                </button>
              ))}
              {imagePicker.length === 0 && (
                <div className="map-image-empty">{t('imageGallery.noImages')}</div>
              )}
            </div>
            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setImagePicker(null)}>
                {t('dialog.cancel')}
              </button>
            </div>
          </div>
        </div>
      )}
      {scaleOpen && (
        <MapScaleDialog
          initial={getWin()?.getMapScale?.() ?? null}
          onCancel={() => setScaleOpen(false)}
          onSubmit={(scale) => {
            getWin()?.setMapScale(scale)
            setScaleOpen(false)
          }}
        />
      )}
    </div>
  )
}

// ── Message + model helpers ────────────────────────────────────────────────

interface MapMessage {
  type: string
  x?: number
  y?: number
  imageId?: string
  entityId?: string
  entityType?: string
  step?: string
  /** A pin that opens another map carries its id. */
  targetMapId?: string
  /** Ruler result: raw world units, and the same in the declared unit. */
  worldUnits?: number
  ground?: number
  unit?: string
}
