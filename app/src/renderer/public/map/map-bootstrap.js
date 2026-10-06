import { mapState } from './map-state.js';
import { initializeMapRuntime } from './map-runtime.js';
import { initializeMapProfiles } from './map-profiles.js';
import { initializeMapMessages, sendMessage, log } from './map-messages.js';
import { initializeZoomTracking, initializeMapPanEvents, initializeMapWheelEvents } from './map-navigation.js';
import { initializeLayerIsolation, publishElementLayerApi } from './map-layers.js';
import { initializeSplineRendering } from './spline-rendering.js';
import { initializeMapContextMenu } from './map-context-menu.js';
import { initializeMapLocalization } from './map-localization.js';
import { initializePinRendering } from './pin-rendering.js';
import { initializeLabelEditing, publishLabelAppearanceApi } from './label-editing.js';
import { initializeMapControls, publishLayerCount } from './map-controls.js';
import { initializeEntityPicker } from './entity-picker.js';
import { initializeMapToolbar } from './map-toolbar.js';
import { initializeImageClipping, exitClipEdit } from './image-clipping.js';
import { initializeTerrainEditing, cancelTerrainDraft, commitTerrainDraft, exitShapeEdit } from './terrain-editing.js';
import { initializeBorderEditing, cancelBorderDraft, commitBorderDraft, exitBorderEdit } from './border-editing.js';
import { initializeBuildingGeometry, footprintRidgeDir, polyCentroid, buildingTypeDef } from './building-geometry.js';
import { initializeBuildingEditing, rollBuildingDraft, exitBuildingEdit } from './building-editing.js';
import { publishBuildingPropertiesApi, publishBuildingDeletionApi } from './building-actions.js';
import { initializeFloorPlanView, exitFloorPlanEdit } from './floor-plan-view.js';
import { initializeFloorPlanEditing, commitWallDraft } from './floor-plan-editing.js';
import { initializeSplineDraft, cancelSplineDraft, commitSplineDraft } from './spline-draft.js';
import { publishSplinePropertiesApi, publishKnotSharpnessApi, publishSplineEditToggle, exitSplineEdit } from './spline-actions.js';
import { rebuildBuildingSvg } from './building-rendering.js';
import { publishMapViewportApi, publishMapImageAndScaleApi, publishMapToolApi } from './map-viewport-api.js';
import { initializeMapMeasurement, applyToolModeClass } from './map-measurement.js';
import { initializeMapHover } from './map-hover.js';
import { publishPinCreationApi, publishPinPropertiesApi, publishPinDeletionApi } from './pin-api.js';
import { initializeMapLayerApi } from './map-layer-api.js';
import { initializeImageApi } from './image-api.js';
import { initializeMapSelection } from './map-selection.js';
import { splineProfile, sampleSpline, lerpColor } from './spline-geometry.js';

initializeMapRuntime();

initializeMapProfiles();

initializeMapMessages();

initializeZoomTracking();

initializeLayerIsolation();

initializeSplineRendering();

initializeMapContextMenu();

initializeMapLocalization();

initializePinRendering();

initializeLabelEditing();

initializeMapControls();

initializeEntityPicker();

publishLayerCount();

initializeMapToolbar();

publishLabelAppearanceApi();

initializeImageClipping();

initializeTerrainEditing();

initializeBorderEditing();

initializeBuildingGeometry();

initializeBuildingEditing();

publishBuildingPropertiesApi();

publishBuildingDeletionApi();

initializeFloorPlanView();

initializeFloorPlanEditing();

initializeSplineDraft();

publishSplinePropertiesApi();

publishKnotSharpnessApi();

publishElementLayerApi();

publishSplineEditToggle();

window.addEventListener('keydown', (e) => {
    if (mapState.clipEdit) {
        if (e.key === 'Escape') exitClipEdit(false);
        else if (e.key === 'Enter') exitClipEdit(true);
        return;
    }
    if (mapState.splineDraft) {
        if (e.key === 'Escape') cancelSplineDraft();
        else if (e.key === 'Enter') commitSplineDraft();
        return;
    }
    if (mapState.splineEdit) {
        if (e.key === 'Escape' || e.key === 'Enter') exitSplineEdit();
        return;
    }
    if (mapState.terrainDraft) {
        if (e.key === 'Escape') cancelTerrainDraft();
        else if (e.key === 'Enter') commitTerrainDraft();
        return;
    }
    if (mapState.terrainEdit) {
        if (e.key === 'Escape' || e.key === 'Enter') exitShapeEdit();
        return;
    }
    if (e.key === 'Escape' && mapState.toolMode === 'terrain') {
        mapState.toolMode = 'select';
        mapState.stage.style.cursor = 'default';
        sendMessage({ type: 'cancelTerrainMode' });
        return;
    }
    if (mapState.borderDraft) {
        if (e.key === 'Escape') cancelBorderDraft();
        else if (e.key === 'Enter') commitBorderDraft();
        return;
    }
    if (mapState.borderEdit) {
        if (e.key === 'Escape' || e.key === 'Enter') exitBorderEdit();
        return;
    }
    if (e.key === 'Escape' && mapState.toolMode === 'border') {
        mapState.toolMode = 'select';
        mapState.stage.style.cursor = 'default';
        return;
    }
    if (mapState.toolMode === 'building') {
        if (e.key === 'Escape') {
            mapState.buildingDraft = null;
            const dr = mapState.world.querySelector('.nv-building[data-building-id="__buildingDraft__"]');
            if (dr) dr.remove();
            mapState.toolMode = 'select';
            mapState.stage.style.cursor = 'default';
            sendMessage({ type: 'cancelBuildingMode' });
        } else if (e.key === 'r' || e.key === 'R') {
            rollBuildingDraft();
        }
        return;
    }
    if (mapState.floorPlanEdit) {
        if (e.key === 'Escape') {
            if (mapState.stairDraft) { mapState.stairDraft = null; rebuildBuildingSvg(mapState.floorPlanEdit.buildingId); return; }
            if (mapState.wallDraft) commitWallDraft(); else exitFloorPlanEdit();
            return;
        }
        if (e.key === 'Enter') { commitWallDraft(); return; }
    }
    if (mapState.buildingEdit && e.key === 'Escape') { exitBuildingEdit(); return; }
    if (e.key === 'Escape' && mapState.toolMode === 'add-pin') {
        sendMessage({ type: 'cancelPinPlace' });
        mapState.toolMode = 'select';
        mapState.stage.style.cursor = 'default';
        return;
    }
    if (e.key === 'Escape' && mapState.toolMode === 'add-label') {
        sendMessage({ type: 'cancelLabelPlace' });
        mapState.toolMode = 'select';
        mapState.stage.style.cursor = 'default';
        return;
    }
    if (e.key === 'Escape' && mapState.toolMode === 'spline') {
        mapState.toolMode = 'select';
        mapState.stage.style.cursor = 'default';
        sendMessage({ type: 'cancelSplineMode' });
        return;
    }
    if ((e.key === 'Delete' || e.key === 'Backspace') && mapState.selected && mapState.mode === 'edit') {
        // Ignore when user is typing in an input/textarea or contentEditable area.
        const t = e.target;
        if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
        e.preventDefault();
        window.deleteSelected();
    }
});

initializeMapPanEvents();

initializeMapWheelEvents();

publishMapViewportApi();

publishMapImageAndScaleApi();

publishMapToolApi();

initializeMapMeasurement();

initializeMapHover();

publishPinCreationApi();

initializeMapLayerApi();

publishPinPropertiesApi();

initializeImageApi();

publishPinDeletionApi();

initializeMapSelection();

applyToolModeClass();

mapState.renderBottomBar();

sendMessage({ type: 'ready' });

window.Map3DHost = {
    get splineProfile() { return splineProfile; },
    get sampleSpline() { return sampleSpline; },
    get lerpColor() { return lerpColor; },
    get renderBottomBar() { return mapState.renderBottomBar; },
    get mapData() { return mapState.mapData; },
    get footprintRidgeDir() { return footprintRidgeDir; },
    get polyCentroid() { return polyCentroid; },
    get isolatedId() { return mapState.isolatedId; },
    get isolatedKind() { return mapState.isolatedKind; },
    get log() { return log; },
    get buildingTypeDef() { return buildingTypeDef; },
    get hud() { return mapState.hud; },
    get imageBaseUrl() { return mapState.imageBaseUrl; },
    get stage() { return mapState.stage; },
    get sendMessage() { return sendMessage; }
};
