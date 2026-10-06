import { X } from 'lucide-react'
import { ToolRail } from './ToolRail'
import { LayerPanel } from './LayerPanel'
import { type MapProfileT } from './mapModel'
import { type MapsViewState } from './mapViewModel'

export function MapCanvas({
  is3D,
  activeTool,
  hasMap,
  editMode,
  buildingScale,
  mapModel,
  selectTool,
  onAddImage,
  onSplinePreset,
  onTerrain,
  onBuilding,
  onBuildingScale,
  setMeasured,
  t,
  setScaleOpen,
  measured,
  iframeRef,
  loading3D,
  peek,
  setPeek,
  selectedNodeId,
  expanded,
  isolated,
  onSelectNode,
  onToggleExpand,
  onAddLayer,
  onAddChild,
  onDeleteNode,
  onRenameNode,
  onToggleHidden,
  onToggleLocked,
  onMoveNode,
  onMoveToRoot,
  onSetOpacity,
  onSetNodeZoom,
  onSetFloorMode,
  onSetActiveFloor,
  onSetElementZoom,
  onToggleIsolate
}: MapCanvasProps): React.JSX.Element {
  return (
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
            <button
              className="map-peek-close"
              onClick={() => setPeek(null)}
              title={t('dialog.cancel')}
            >
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
  )
}

type MapCanvasProps = {
  is3D: MapsViewState['is3D']
  activeTool: MapsViewState['activeTool']
  hasMap: MapsViewState['hasMap']
  editMode: MapsViewState['editMode']
  buildingScale: MapsViewState['buildingScale']
  mapModel: MapsViewState['mapModel']
  selectTool: MapsViewState['selectTool']
  onAddImage: MapsViewState['onAddImage']
  onSplinePreset: MapsViewState['onSplinePreset']
  onTerrain: MapsViewState['onTerrain']
  onBuilding: MapsViewState['onBuilding']
  onBuildingScale: MapsViewState['onBuildingScale']
  setMeasured: MapsViewState['setMeasured']
  t: MapsViewState['t']
  setScaleOpen: MapsViewState['setScaleOpen']
  measured: MapsViewState['measured']
  iframeRef: MapsViewState['iframeRef']
  loading3D: MapsViewState['loading3D']
  peek: MapsViewState['peek']
  setPeek: MapsViewState['setPeek']
  selectedNodeId: MapsViewState['selectedNodeId']
  expanded: MapsViewState['expanded']
  isolated: MapsViewState['isolated']
  onSelectNode: MapsViewState['onSelectNode']
  onToggleExpand: MapsViewState['onToggleExpand']
  onAddLayer: MapsViewState['onAddLayer']
  onAddChild: MapsViewState['onAddChild']
  onDeleteNode: MapsViewState['onDeleteNode']
  onRenameNode: MapsViewState['onRenameNode']
  onToggleHidden: MapsViewState['onToggleHidden']
  onToggleLocked: MapsViewState['onToggleLocked']
  onMoveNode: MapsViewState['onMoveNode']
  onMoveToRoot: MapsViewState['onMoveToRoot']
  onSetOpacity: MapsViewState['onSetOpacity']
  onSetNodeZoom: MapsViewState['onSetNodeZoom']
  onSetFloorMode: MapsViewState['onSetFloorMode']
  onSetActiveFloor: MapsViewState['onSetActiveFloor']
  onSetElementZoom: MapsViewState['onSetElementZoom']
  onToggleIsolate: MapsViewState['onToggleIsolate']
}
