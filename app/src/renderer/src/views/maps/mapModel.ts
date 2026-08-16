/**
 * TypeScript mirror of the subset of Novalist.Core.Models.MapData that the
 * React map host reads and writes. The canvas engine (public/map/map.html) owns
 * rendering and most element editing; the host only needs the layer tree plus
 * pin / label references for the panel, and the bridge method surface below.
 */

export interface MapImageT {
  id: string
  path: string
  minZoom?: number | null
  maxZoom?: number | null
}

export interface MapSplineT {
  id: string
  kind?: string
  preset?: string
  minZoom?: number | null
  maxZoom?: number | null
}

export interface MapShapeT {
  id: string
  type?: string
  minZoom?: number | null
  maxZoom?: number | null
}

export interface MapBuildingT {
  id: string
  type?: string
  minZoom?: number | null
  maxZoom?: number | null
}

export interface MapPinT {
  id: string
  label?: string
  layerId?: string
  entityId?: string
  entityType?: string
  minZoom?: number | null
  maxZoom?: number | null
}

export interface MapLabelT {
  id: string
  text?: string
  layerId?: string
  minZoom?: number | null
  maxZoom?: number | null
}

export interface MapLayerNodeT {
  id: string
  name: string
  opacity: number
  locked: boolean
  hidden: boolean
  expanded: boolean
  images: MapImageT[]
  splines?: MapSplineT[]
  shapes?: MapShapeT[]
  buildings?: MapBuildingT[]
  children: MapLayerNodeT[]
  isConnectedSet?: boolean
  defaultMemberLayerId?: string | null
  minZoom?: number | null
  maxZoom?: number | null
}

export interface MapProfileT {
  id: string
  name: string
  kind: string
}

export interface MapDataT {
  id: string
  name: string
  layers: MapLayerNodeT[]
  pins: MapPinT[]
  labels?: MapLabelT[]
  customProfiles?: MapProfileT[]
  border?: unknown
  [key: string]: unknown
}

/** Element kinds the isolate / zoom-range / delete bridge methods accept. */
export type ElementKind = 'image' | 'spline' | 'pin' | 'label' | 'shape' | 'building'

/** Tool-rail modes, matching the strings map.html's setToolMode() accepts. */
export type ToolMode =
  | 'select'
  | 'add-pin'
  | 'add-label'
  | 'spline'
  | 'terrain'
  | 'building'
  | 'border'
  /** Two clicks and a distance, in the map's declared units. */
  | 'ruler'

/** What one world unit on a map is worth on the ground. */
export interface MapScale {
  /** Ground distance per world unit. Positive. */
  unitsPer: number
  /** What that distance is called: "km", "miles", "leagues". */
  unit: string
  /** Grid spacing in world units, or 0 for no grid. */
  gridSpacing: number
}

/**
 * The window.* bridge map.html exposes on the iframe's contentWindow. Only the
 * methods the host calls are declared. Every method is synchronous; the page is
 * same-origin (sandbox allow-same-origin) so these are ordinary calls.
 */
export interface MapWindow extends Window {
  // Data + view lifecycle.
  setImageBaseUrl(url: string): void
  setMapData(json: string): void
  getMapData(): string
  setMode(mode: 'edit' | 'view'): void
  setActiveLayer(layerId: string): void
  setEntityOptions(json: string): void
  setMapStrings(json: string): void
  setContextMenuLabels(move: string, clip: string, del: string): void
  // Navigation.
  zoomToFit(): void
  resetView(): void
  focusOnPin(pinId: string): void
  // Tool modes + drafts.
  setToolMode(mode: ToolMode): void
  /** Declares the map's scale, or clears it with null. */
  setMapScale(scale: MapScale | null): void
  /** The map's declared scale, or null when it has none. */
  getMapScale(): MapScale | null
  /** The other maps in this project, so a pin can be pointed at one. */
  setOtherMaps(maps: { id: string; name: string }[]): void
  setSplineDraftType(kind: string, preset: string): void
  setTerrainDraftType(type: string): void
  setBuildingDraftType(type: string): void
  setBuildingScale(scale: number): void
  // Placement.
  addImageToMap(relPath: string, width: number, height: number): void
  addPinAtPoint(
    wx: number,
    wy: number,
    label: string,
    entityType: string,
    entityId: string,
    color: string
  ): void
  addPinAtCenter(label: string, entityType: string, entityId: string, color: string): void
  // Selection-scoped edits.
  deleteSelected(): void
  toggleClipEditOnSelected(): void
  toggleSplineEditOnSelected(): void
  clearBorder(): void
  moveMapElementToLayer(kind: string, id: string, targetLayerId: string): void
  // Isolate + zoom range (properties panel).
  setIsolatedElement(kind: string, id: string): void
  setIsolatedImage(imageId: string): void
  setElementZoomRange(kind: string, id: string, minZoom: number, maxZoom: number): void
  updateImageZoomRange(imageId: string, minZoom: number, maxZoom: number): void
  // 3D view.
  Map3D?: { enter(): void; exit(): void; isActive(): boolean }
}

/** Depth-first walk over the layer tree. */
export function walkNodes(
  nodes: MapLayerNodeT[],
  cb: (node: MapLayerNodeT, depth: number) => void,
  depth = 0
): void {
  for (const n of nodes) {
    cb(n, depth)
    if (n.children?.length) walkNodes(n.children, cb, depth + 1)
  }
}

export function findNode(data: MapDataT, id: string): MapLayerNodeT | null {
  let found: MapLayerNodeT | null = null
  walkNodes(data.layers, (n) => {
    if (n.id === id) found = n
  })
  return found
}

/** The list that directly contains `id`, plus its parent (null if a root). */
function findContainer(
  data: MapDataT,
  id: string
): { list: MapLayerNodeT[]; parent: MapLayerNodeT | null } | null {
  let result: { list: MapLayerNodeT[]; parent: MapLayerNodeT | null } | null = null
  const recurse = (list: MapLayerNodeT[], parent: MapLayerNodeT | null): void => {
    if (result) return
    if (list.some((n) => n.id === id)) {
      result = { list, parent }
      return
    }
    for (const n of list) recurse(n.children ?? [], n)
  }
  recurse(data.layers, null)
  return result
}

function isDescendant(node: MapLayerNodeT, candidateId: string): boolean {
  for (const c of node.children ?? []) {
    if (c.id === candidateId) return true
    if (isDescendant(c, candidateId)) return true
  }
  return false
}

export type DropPosition = 'before' | 'after' | 'inside'

/** Move `dragId` relative to `targetId`. Mutates `data` in place. Rejects
 * dropping a node into its own subtree. Mirrors MapViewModel.MoveNodeAsync. */
export function moveNode(
  data: MapDataT,
  dragId: string,
  targetId: string,
  position: DropPosition
): boolean {
  if (dragId === targetId) return false
  const dragNode = findNode(data, dragId)
  if (!dragNode) return false
  if (isDescendant(dragNode, targetId)) return false
  const src = findContainer(data, dragId)
  if (!src) return false
  const idx = src.list.findIndex((n) => n.id === dragId)
  src.list.splice(idx, 1)
  if (position === 'inside') {
    const target = findNode(data, targetId)
    if (!target) {
      src.list.push(dragNode)
      return false
    }
    target.children = target.children ?? []
    target.children.push(dragNode)
    target.expanded = true
    return true
  }
  const dst = findContainer(data, targetId)
  if (!dst) {
    src.list.push(dragNode)
    return false
  }
  const targetIdx = dst.list.findIndex((n) => n.id === targetId)
  if (targetIdx < 0) {
    src.list.push(dragNode)
    return false
  }
  dst.list.splice(position === 'after' ? targetIdx + 1 : targetIdx, 0, dragNode)
  return true
}

/** Remove a node (and its subtree) by id. Returns true if removed. */
export function deleteNode(data: MapDataT, id: string): boolean {
  const container = findContainer(data, id)
  if (!container) return false
  const idx = container.list.findIndex((n) => n.id === id)
  if (idx < 0) return false
  container.list.splice(idx, 1)
  return true
}

/** First leaf node (childless) in tree order, or null. */
export function firstLeaf(data: MapDataT): MapLayerNodeT | null {
  let leaf: MapLayerNodeT | null = null
  walkNodes(data.layers, (n) => {
    if (!leaf && !(n.children?.length)) leaf = n
  })
  return leaf
}

export function newId(prefix: string): string {
  return `${prefix}-${Math.random().toString(16).slice(2, 10)}`
}

type Tr = (key: string) => string

/** Localized [value, label] pairs for the road / river spline preset picker. */
export const SPLINE_PRESETS: { kind: string; preset: string; labelKey: string }[] = [
  { kind: 'road', preset: 'motorway', labelKey: 'map.roadMotorway' },
  { kind: 'road', preset: 'primary', labelKey: 'map.roadPrimary' },
  { kind: 'road', preset: 'secondary', labelKey: 'map.roadSecondary' },
  { kind: 'road', preset: 'residential', labelKey: 'map.roadResidential' },
  { kind: 'road', preset: 'service', labelKey: 'map.roadService' },
  { kind: 'road', preset: 'pedestrian', labelKey: 'map.roadPedestrian' },
  { kind: 'road', preset: 'trail', labelKey: 'map.roadTrail' },
  { kind: 'road', preset: 'track', labelKey: 'map.roadTrack' },
  { kind: 'river', preset: 'brook', labelKey: 'map.riverBrook' },
  { kind: 'river', preset: 'stream', labelKey: 'map.riverStream' },
  { kind: 'river', preset: 'river', labelKey: 'map.riverRiver' },
  { kind: 'river', preset: 'canal', labelKey: 'map.riverCanal' },
  { kind: 'river', preset: 'estuary', labelKey: 'map.riverEstuary' }
]

export const TERRAIN_TYPES: { type: string; labelKey: string }[] = [
  { type: 'grass', labelKey: 'map.terrainGrass' },
  { type: 'forest', labelKey: 'map.terrainForest' },
  { type: 'concrete', labelKey: 'map.terrainConcrete' },
  { type: 'sand', labelKey: 'map.terrainSand' },
  { type: 'hills', labelKey: 'map.terrainHills' },
  { type: 'mountain', labelKey: 'map.terrainMountain' },
  { type: 'water', labelKey: 'map.terrainWater' }
]

export const BUILDING_TYPES: { type: string; labelKey: string }[] = [
  { type: 'singleFamily', labelKey: 'map.bldSingleFamily' },
  { type: 'rowHome', labelKey: 'map.bldRowHome' },
  { type: 'school', labelKey: 'map.bldSchool' },
  { type: 'police', labelKey: 'map.bldPolice' },
  { type: 'fireStation', labelKey: 'map.bldFireStation' },
  { type: 'hall', labelKey: 'map.bldHall' },
  { type: 'playground', labelKey: 'map.bldPlayground' },
  { type: 'trainStation', labelKey: 'map.bldTrainStation' }
]

/**
 * Builds the localized strings blob map.html's setMapStrings() consumes to drive
 * its in-canvas bottom bar, context menus, HUD and enum selects. Mirrors
 * MapView.axaml.cs PushMapStrings. Values are looked up through the same flat
 * `map.*` locale keys the Avalonia app used.
 */
export function buildMapStrings(t: Tr): string {
  const pairs = (list: { labelKey: string }[], value: (item: never) => string): string =>
    JSON.stringify(list.map((item) => [value(item as never), t(item.labelKey)]))
  const strings: Record<string, string> = {
    knotDelete: t('map.knotDelete'),
    knotClearOverride: t('map.knotClearOverride'),
    knotTypePrefix: t('map.knotTypePrefix'),
    labelEditText: t('map.labelEditText'),
    ctxDelete: t('map.ctxDelete'),
    clipHint: t('map.clipHint'),
    clipDone: t('map.clipDone'),
    clipClear: t('map.clipClear'),
    clipCancel: t('map.clipCancel'),
    splineEditHint: t('map.splineEditHint'),
    splineEditDone: t('map.splineEditDone'),
    widthHandleTip: t('map.widthHandleTip'),
    ctxEdit: t('map.ctxEdit'),
    moveToLayer: t('map.moveToLayer'),
    knotAddBefore: t('map.knotAddBefore'),
    knotAddAfter: t('map.knotAddAfter'),
    knotClearDirection: t('map.knotClearDirection'),
    splineDelete: t('map.splineDelete'),
    rotHandleTip: t('map.rotHandleTip'),
    shapeDelete: t('map.shapeDelete'),
    terrainEditHint: t('map.terrainEditHint'),
    buildingDelete: t('map.buildingDelete'),
    bldRotateTip: t('map.bldRotateTip'),
    fpWall: t('map.fpWall'),
    fpDoor: t('map.fpDoor'),
    fpWindow: t('map.fpWindow'),
    fpStairs: t('map.fpStairs'),
    fpLabel: t('map.fpLabel'),
    fpPin: t('map.fpPin'),
    fpFlipSide: t('map.fpFlipSide'),
    fpHint: t('map.fpHint'),
    fpNoFloorsWarn: t('map.fpNoFloorsWarn'),
    cancel: t('map.cancel'),
    borderEditHint: t('map.borderEditHint'),
    bbIdle: t('map.bbIdle'),
    bbIdleHint: t('map.bbIdleHint'),
    bb3dLabel: t('map.bb3dLabel'),
    bb3dHint: t('map.bb3dHint'),
    kbd3dMove: t('map.kbd3dMove'),
    bbClipLabel: t('map.bbClipLabel'),
    bbFloorPlanLabel: t('map.bbFloorPlanLabel'),
    bbSplineLabel: t('map.bbSplineLabel'),
    bbTerrainLabel: t('map.bbTerrainLabel'),
    bbBorderLabel: t('map.bbBorderLabel'),
    bbBuildingLabel: t('map.bbBuildingLabel'),
    bbPinLabel: t('map.bbPinLabel'),
    bbLabelSelLabel: t('map.bbLabelSelLabel'),
    bbImageLabel: t('map.bbImageLabel'),
    bbSplineDraft: t('map.bbSplineDraft'),
    bbTerrainDraft: t('map.bbTerrainDraft'),
    bbBorderDraft: t('map.bbBorderDraft'),
    bbBuildingPlace: t('map.bbBuildingPlace'),
    bbBuildingHint: t('map.bbBuildingHint'),
    bbBuildingEditHint: t('map.bbBuildingEditHint'),
    bbEditFloorPlan: t('map.bbEditFloorPlan'),
    bbSelectedHint: t('map.bbSelectedHint'),
    bbClickToAddPoints: t('map.bbClickToAddPoints'),
    kbdEscCancelEnterCommit: t('map.kbdEscCancelEnterCommit'),
    kbdBuildingPlace: t('map.kbdBuildingPlace'),
    kbdFinishDelete: t('map.kbdFinishDelete'),
    kbdFinish: t('map.kbdFinish'),
    kbdEscDeselect: t('map.kbdEscDeselect'),
    kbdDeleteRemoveEscDeselect: t('map.kbdDeleteRemoveEscDeselect'),
    hudMap: t('map.hudMap'),
    hudZoom: t('map.hudZoom'),
    hudRuler: t('map.hudRuler'),
    pinTargetMap: t('map.pinTargetMap'),
    pinNoTargetMap: t('map.pinNoTargetMap'),
    hudLayer: t('map.hudLayer'),
    hudSplineDraft: t('map.hudSplineDraft'),
    hudTerrainDraft: t('map.hudTerrainDraft'),
    hudBorderDraft: t('map.hudBorderDraft'),
    hudWallDraft: t('map.hudWallDraft'),
    hudKnots: t('map.hudKnots'),
    hudVerts: t('map.hudVerts'),
    hudPts: t('map.hudPts'),
    bbLayer: t('map.bbLayer'),
    bbMore: t('map.bbMore'),
    bbColors: t('map.bbColors'),
    bbKnot: t('map.bbKnot'),
    buildingTypesJson: pairs(BUILDING_TYPES, (i: { type: string }) => i.type),
    roofKindsJson: JSON.stringify([
      ['gable', t('map.roofGable')],
      ['hip', t('map.roofHip')],
      ['flat', t('map.roofFlat')]
    ]),
    terrainTypesJson: pairs(TERRAIN_TYPES, (i: { type: string }) => i.type),
    splinePresetsJson: JSON.stringify(
      SPLINE_PRESETS.map((p) => [`${p.kind}:${p.preset}`, t(p.labelKey)])
    ),
    markingStylesJson: JSON.stringify([
      ['', t('map.markPresetDefault')],
      ['none', t('map.markNone')],
      ['single', t('map.markSingle')],
      ['dashed', t('map.markDashed')],
      ['double', t('map.markDouble')],
      ['solid-dashed', t('map.markSolidDashed')]
    ]),
    knotTypesJson: JSON.stringify([
      ['', t('map.knotClearType')],
      ['motorway', t('map.roadMotorway')],
      ['primary', t('map.roadPrimary')],
      ['secondary', t('map.roadSecondary')],
      ['residential', t('map.roadResidential')],
      ['service', t('map.roadService')],
      ['pedestrian', t('map.roadPedestrian')],
      ['trail', t('map.roadTrail')],
      ['track', t('map.roadTrack')],
      ['brook', t('map.riverBrook')],
      ['stream', t('map.riverStream')],
      ['river', t('map.riverRiver')],
      ['canal', t('map.riverCanal')],
      ['estuary', t('map.riverEstuary')]
    ]),
    pinLabel: t('map.pinLabel'),
    pinLabelWatermark: t('map.pinLabelWatermark'),
    pinEntity: t('map.pinEntity'),
    pinEntityWatermark: t('map.pinEntityWatermark'),
    pinEntityNoResults: t('map.pinEntityNoResults'),
    pinColor: t('map.pinColor'),
    pinIcon: t('map.pinIcon'),
    pinIconDot: t('map.pinIconDot'),
    pinIcon_city: t('map.pinIcon_city'),
    pinIcon_town: t('map.pinIcon_town'),
    pinIcon_village: t('map.pinIcon_village'),
    pinIcon_castle: t('map.pinIcon_castle'),
    pinIcon_tower: t('map.pinIcon_tower'),
    pinIcon_ruin: t('map.pinIcon_ruin'),
    pinIcon_temple: t('map.pinIcon_temple'),
    pinIcon_mountain: t('map.pinIcon_mountain'),
    pinIcon_hills: t('map.pinIcon_hills'),
    pinIcon_forest: t('map.pinIcon_forest'),
    pinIcon_lake: t('map.pinIcon_lake'),
    pinIcon_port: t('map.pinIcon_port'),
    pinIcon_bridge: t('map.pinIcon_bridge'),
    pinIcon_mine: t('map.pinIcon_mine'),
    pinIcon_camp: t('map.pinIcon_camp'),
    pinIcon_crossroads: t('map.pinIcon_crossroads'),
    pinIcon_cave: t('map.pinIcon_cave'),
    pinIcon_battle: t('map.pinIcon_battle'),
    labelFontSize: t('map.labelFontSize'),
    labelColor: t('map.labelColor'),
    alignLeft: t('map.alignLeft'),
    alignCenter: t('map.alignCenter'),
    alignRight: t('map.alignRight'),
    shapeType: t('map.shapeType'),
    shapeColor: t('map.shapeColor'),
    shapeSmooth: t('map.shapeSmooth'),
    shapeBlend: t('map.shapeBlend'),
    shapeForward: t('map.shapeForward'),
    shapeBackward: t('map.shapeBackward'),
    buildingType: t('map.buildingType'),
    buildingRoof: t('map.buildingRoof'),
    buildingFloors: t('map.buildingFloors'),
    buildingPlanZoom: t('map.buildingPlanZoom'),
    roofPitch: t('map.roofPitch'),
    splineType: t('map.splineType'),
    splineClosed: t('map.splineClosed'),
    splineMarking: t('map.splineMarking'),
    splineBlend: t('map.splineBlend'),
    knotSharpness: t('map.knotSharpness'),
    colorCasing: t('map.colorCasing'),
    colorFill: t('map.colorFill'),
    colorMarking: t('map.colorMarking'),
    colorReset: t('map.colorReset'),
    borderOutlineColor: t('map.borderOutlineColor'),
    borderOutlineWidth: t('map.borderOutlineWidth'),
    borderClear: t('map.borderClear'),
    clip: t('map.imageMenuClip'),
    imageMinZoom: t('map.imageMinZoom'),
    imageMaxZoom: t('map.imageMaxZoom')
  }
  return JSON.stringify(strings)
}
