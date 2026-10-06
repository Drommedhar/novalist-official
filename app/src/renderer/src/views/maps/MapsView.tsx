import { DesktopViewActions } from '../../shell/DesktopViewFrame'
import { Plus, Pencil, Trash2, Maximize, Crosshair, Box, Scissors, Spline as SplineIcon, ImageDown, Mountain, Eye } from 'lucide-react'
import { InputDialog } from '../../shell/InputDialog'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { MapScaleDialog } from './MapScaleDialog'
import './map.css'
import { useMapViewModel, type MapsViewState } from './mapViewModel'
import { MapCanvas } from './MapCanvas'

export function MapsView(): React.JSX.Element {
  const { setCreating, t, activeId, generating, generateTerrain, exporting, exportImage, exportScale, setExportScale, maps, setActiveId, editMode, hasMap, is3D, setEditMode, getWin, toggle3D, selectTool, selection, setRenaming, setConfirmingDelete, activeTool, buildingScale, mapModel, onAddImage, onSplinePreset, onTerrain, onBuilding, onBuildingScale, setMeasured, setScaleOpen, measured, iframeRef, loading3D, peek, setPeek, selectedNodeId, expanded, isolated, onSelectNode, onToggleExpand, onAddLayer, onAddChild, onDeleteNode, onRenameNode, onToggleHidden, onToggleLocked, onMoveNode, onMoveToRoot, onSetOpacity, onSetNodeZoom, onSetFloorMode, onSetActiveFloor, onSetElementZoom, onToggleIsolate, creating, onCreateMap, renaming, onRenameMap, confirmingDelete, activeMap, onDeleteMap, imagePicker, setImagePicker, placeImage, scaleOpen } = useMapViewModel()
  return (
    <div className="mapsview">
      <MapToolbar setCreating={setCreating} t={t} activeId={activeId} generating={generating} generateTerrain={generateTerrain} exporting={exporting} exportImage={exportImage} exportScale={exportScale} setExportScale={setExportScale} maps={maps} setActiveId={setActiveId} editMode={editMode} hasMap={hasMap} is3D={is3D} setEditMode={setEditMode} getWin={getWin} toggle3D={toggle3D} selectTool={selectTool} selection={selection} setRenaming={setRenaming} setConfirmingDelete={setConfirmingDelete} />

      {maps.length === 0 ? (
        <p className="codex-empty">{t('map.emptyState')}</p>
      ) : (
        <MapCanvas is3D={is3D} activeTool={activeTool} hasMap={hasMap} editMode={editMode} buildingScale={buildingScale} mapModel={mapModel} selectTool={selectTool} onAddImage={onAddImage} onSplinePreset={onSplinePreset} onTerrain={onTerrain} onBuilding={onBuilding} onBuildingScale={onBuildingScale} setMeasured={setMeasured} t={t} setScaleOpen={setScaleOpen} measured={measured} iframeRef={iframeRef} loading3D={loading3D} peek={peek} setPeek={setPeek} selectedNodeId={selectedNodeId} expanded={expanded} isolated={isolated} onSelectNode={onSelectNode} onToggleExpand={onToggleExpand} onAddLayer={onAddLayer} onAddChild={onAddChild} onDeleteNode={onDeleteNode} onRenameNode={onRenameNode} onToggleHidden={onToggleHidden} onToggleLocked={onToggleLocked} onMoveNode={onMoveNode} onMoveToRoot={onMoveToRoot} onSetOpacity={onSetOpacity} onSetNodeZoom={onSetNodeZoom} onSetFloorMode={onSetFloorMode} onSetActiveFloor={onSetActiveFloor} onSetElementZoom={onSetElementZoom} onToggleIsolate={onToggleIsolate} />
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

function MapToolbar({
  setCreating,
  t,
  activeId,
  generating,
  generateTerrain,
  exporting,
  exportImage,
  exportScale,
  setExportScale,
  maps,
  setActiveId,
  editMode,
  hasMap,
  is3D,
  setEditMode,
  getWin,
  toggle3D,
  selectTool,
  selection,
  setRenaming,
  setConfirmingDelete
}: MapToolbarProps): React.JSX.Element {
  return (
    <div className="map-toolbar">
      <DesktopViewActions>
        {' '}
        <button className="dialog-button primary" onClick={() => setCreating(true)}>
          <Plus size={14} strokeWidth={2} />
          {t('map.menuNewMap')}
        </button>
      </DesktopViewActions>
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
  )
}

type MapToolbarProps = {
  setCreating: MapsViewState['setCreating']
  t: MapsViewState['t']
  activeId: MapsViewState['activeId']
  generating: MapsViewState['generating']
  generateTerrain: MapsViewState['generateTerrain']
  exporting: MapsViewState['exporting']
  exportImage: MapsViewState['exportImage']
  exportScale: MapsViewState['exportScale']
  setExportScale: MapsViewState['setExportScale']
  maps: MapsViewState['maps']
  setActiveId: MapsViewState['setActiveId']
  editMode: MapsViewState['editMode']
  hasMap: MapsViewState['hasMap']
  is3D: MapsViewState['is3D']
  setEditMode: MapsViewState['setEditMode']
  getWin: MapsViewState['getWin']
  toggle3D: MapsViewState['toggle3D']
  selectTool: MapsViewState['selectTool']
  selection: MapsViewState['selection']
  setRenaming: MapsViewState['setRenaming']
  setConfirmingDelete: MapsViewState['setConfirmingDelete']
}
