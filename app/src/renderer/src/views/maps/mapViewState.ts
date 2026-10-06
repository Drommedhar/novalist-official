import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { MapDataT, ToolMode } from './mapModel'
import type { MapRefDto, EntityOption, PeekData, Loading3D, MapMessage } from './mapViewTypes'

export function useMapViewState() {
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
  const apiRef = useRef<((msg: MapMessage) => void) | null>(null)

  return {
    t, maps, setMaps, activeId, setActiveId, mapModel, setMapModel, activeTool, setActiveTool,
    measured, setMeasured, scaleOpen, setScaleOpen, editMode, setEditMode, is3D, setIs3D,
    loading3D, setLoading3D, selectedNodeId, setSelectedNodeId, expanded, setExpanded,
    isolated, setIsolated, selection, setSelection, peek, setPeek, buildingScale,
    setBuildingScale, creating, setCreating, exporting, setExporting, generating,
    setGenerating, exportScale, setExportScale, renaming, setRenaming, confirmingDelete,
    setConfirmingDelete, imagePicker, setImagePicker, iframeRef, readyRef, pendingFocusPinRef,
    saveTimer, entityOptionsRef, mapModelRef, apiRef
  }
}

export type MapViewState = ReturnType<typeof useMapViewState>
