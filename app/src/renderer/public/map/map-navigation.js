import { mapState } from './map-state.js';
import { updateHud } from './map-measurement.js';
import { repositionPins, repositionLabels, tryRulerPoint, tryPlacePin, tryPlaceLabel } from './map-placement.js';
import { refreshBuildingsForZoom, renderFloorSelectors } from './floor-plan-view.js';
import { renderSplines } from './spline-rendering.js';
import { updateLayerVisibility, findLayer } from './map-layers.js';
import { refreshBorder, renderBorderEditor, tryPlaceBorderPoint } from './border-editing.js';
import { renderFloorAnnotations, renderBuildingEditor, tryPlaceBuilding, renderBuildingDraft } from './building-editing.js';
import { updateImageHandles } from './image-interactions.js';
import { renderClipEditor } from './image-clipping.js';
import { renderSplineEditor } from './spline-editor.js';
import { renderTerrainEditor, tryPlaceTerrainPoint } from './terrain-editing.js';
import { applyImageTransform } from './image-geometry.js';
import { trySplinePoint } from './spline-draft.js';
import { clearSelection } from './map-selection.js';
import { sendMessage } from './map-messages.js';
import { bbHoverTrack } from './map-hover.js';
import { attachBuildingToRoads } from './building-geometry.js';
import { rebuildBuildingSvg } from './building-rendering.js';

export function applyTransform() {
    // #world only translates by pan. Each image's transform encodes its own zoom
    // so the browser re-rasterizes the image at the current zoom (no GPU bitmap
    // stretch from a cached world-scale layer = sharper zoom-in).
    mapState.world.style.transform = `translate(${mapState.pan.x}px, ${mapState.pan.y}px)`;
    updateHud();
    repositionPins();
    repositionLabels();
    if (mapState.zoom !== mapState.lastZoomApplied) {
        mapState.lastZoomApplied = mapState.zoom;
        applyZoomToAllImages();
        applyZoomToAllTerrain(); // terrain + building <svg> paths are world-unit, scaled by zoom
        refreshBuildingsForZoom(); // roof ↔ floor-plan switch at planMinZoom
        renderSplines(); // spline geometry is authored in world*zoom coords
    }
    // Must run AFTER renderSplines — that wipes + rebuilds the spline SVG, so
    // the layer hide / opacity cascade has to be re-applied to the fresh nodes.
    updateLayerVisibility();
    refreshBorder(); // clip-path + outline track pan / zoom
    renderFloorSelectors(); // inline floor controls track pan / zoom
    renderFloorAnnotations(); // active-floor labels / pins re-evaluated for the new zoom
    updateImageHandles();
    if (mapState.clipEdit) renderClipEditor();
    if (mapState.splineDraft || mapState.splineEdit) renderSplineEditor();
    if (mapState.terrainDraft || mapState.terrainEdit) renderTerrainEditor();
    if (mapState.borderDraft || mapState.borderEdit) renderBorderEditor();
    renderBuildingEditor(); // building rotation handle — after updateImageHandles (it clears the overlay)
}

// Terrain shape + building <svg>s carry world-unit geometry; the current zoom
// is applied as a CSS scale (mirrors how images encode their own zoom).
export function applyZoomToAllTerrain() {
    Array.from(mapState.world.querySelectorAll('.nv-terrain, .nv-building')).forEach(el => {
        el.style.transform = `scale(${mapState.zoom})`;
    });
}

export function applyZoomToAllImages() {
    Array.from(mapState.world.querySelectorAll('.nv-image')).forEach(el => {
        const lid = el.dataset.layerId;
        const iid = el.dataset.imageId;
        const layer = findLayer(lid);
        if (!layer) return;
        const img = (layer.images || []).find(i => i.id === iid);
        if (!img) return;
        applyImageTransform(el, img);
    });
}

export function initializeZoomTracking() {
    mapState.lastZoomApplied = -1;
}

export function startPan(e) {
    mapState.panStart = { x: e.clientX - mapState.pan.x, y: e.clientY - mapState.pan.y };
    mapState.stage.classList.add('dragging');
}

export function initializeMapPanEvents() {
    mapState.panStart = null;
    mapState.stage.addEventListener('mousedown', (e) => {
        if (e.button === 1) { // middle button = pan
            e.preventDefault();
            startPan(e);
            return;
        }
        if (tryRulerPoint(e)) return;
        if (trySplinePoint(e)) return;
        if (tryPlacePin(e)) return;
        if (tryPlaceLabel(e)) return;
        if (tryPlaceTerrainPoint(e)) return;
        if (tryPlaceBorderPoint(e)) return;
        if (tryPlaceBuilding(e)) return;
        if (e.target !== mapState.stage && e.target !== mapState.world) return;
        if (e.button !== 0) return;
        // Left-click on empty stage just clears selection. Pan requires middle button.
        clearSelection();
        sendMessage({ type: 'selectionCleared' });
    });
    mapState.stage.addEventListener('mousemove', (e) => {
        bbHoverTrack(e);
        if (mapState.toolMode === 'building' && mapState.buildingDraft) {
            attachBuildingToRoads(mapState.buildingDraft, (e.clientX - mapState.pan.x) / mapState.zoom, (e.clientY - mapState.pan.y) / mapState.zoom, e.shiftKey);
            renderBuildingDraft();
            return;
        }
        // Stair draw preview tracks the cursor between the start and end clicks.
        if (mapState.floorPlanEdit && mapState.floorPlanTool === 'stairs' && mapState.stairDraft) {
            mapState.stairDraft.cursor = { x: (e.clientX - mapState.pan.x) / mapState.zoom, y: (e.clientY - mapState.pan.y) / mapState.zoom };
            rebuildBuildingSvg(mapState.floorPlanEdit.buildingId);
        }
    });
    mapState.stage.addEventListener('auxclick', (e) => { if (e.button === 1) e.preventDefault(); });
    document.addEventListener('contextmenu', (e) => e.preventDefault());
    mapState.stage.addEventListener('contextmenu', (e) => {
        if (e.target === mapState.stage || e.target === mapState.world || e.target === mapState.pinOverlay
            || e.target === mapState.labelOverlay || e.target === mapState.handlesOverlay)
            e.preventDefault();
    });
    window.addEventListener('mousemove', (e) => {
        mapState.lastCursorWorld.x = (e.clientX - mapState.pan.x) / mapState.zoom;
        mapState.lastCursorWorld.y = (e.clientY - mapState.pan.y) / mapState.zoom;
        updateHud();
        if (!mapState.panStart) return;
        mapState.pan.x = e.clientX - mapState.panStart.x;
        mapState.pan.y = e.clientY - mapState.panStart.y;
        applyTransform();
    });
    window.addEventListener('mouseup', () => {
        if (mapState.panStart) {
            mapState.panStart = null;
            mapState.stage.classList.remove('dragging');
            mapState.mapData.initialView = { centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom };
            sendMessage({ type: 'viewChanged', centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom });
        }
    });
}

export function initializeMapWheelEvents() {
    mapState.stage.addEventListener('wheel', (e) => {
        e.preventDefault();
        // Building placement: when the right mouse button is held OR Shift is
        // held, the wheel rotates the placement preview instead of zooming. 15°
        // step with Shift (snap), otherwise 5°.
        if (mapState.toolMode === 'building' && mapState.buildingDraft
                && (mapState.buildingRotatingRMB || e.shiftKey)) {
            const dir = e.deltaY < 0 ? 1 : -1;
            const step = e.shiftKey ? 15 : 5;
            mapState.buildingDraftRotation = (mapState.buildingDraftRotation + dir * step) % 360;
            // Re-attach + re-render at the current cursor world position so the
            // preview reflects the new angle immediately.
            const wx = (e.clientX - mapState.pan.x) / mapState.zoom;
            const wy = (e.clientY - mapState.pan.y) / mapState.zoom;
            attachBuildingToRoads(mapState.buildingDraft, wx, wy, e.shiftKey);
            renderBuildingDraft();
            return;
        }
        const factor = e.deltaY < 0 ? 1.1 : 1 / 1.1;
        const wx = (e.clientX - mapState.pan.x) / mapState.zoom;
        const wy = (e.clientY - mapState.pan.y) / mapState.zoom;
        mapState.zoom = Math.max(0.05, Math.min(10, mapState.zoom * factor));
        mapState.pan.x = e.clientX - wx * mapState.zoom;
        mapState.pan.y = e.clientY - wy * mapState.zoom;
        applyTransform();
        mapState.mapData.initialView = { centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom };
        sendMessage({ type: 'viewChanged', centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom });
    }, { passive: false });
    mapState.stage.addEventListener('mousedown', (e) => {
        if (e.button === 2 && mapState.toolMode === 'building' && mapState.buildingDraft) {
            mapState.buildingRotatingRMB = true;
            e.preventDefault();
        }
    });
    window.addEventListener('mouseup', (e) => {
        if (e.button === 2) mapState.buildingRotatingRMB = false;
    });
    window.addEventListener('blur', () => { mapState.buildingRotatingRMB = false; });
}

export function renderPolygonEdges(points, className) {
    const count = points.length >= 3 ? points.length : points.length - 1;
    for (let i = 0; i < count; i++) {
        const a = points[i], b = points[(i + 1) % points.length];
        const x1 = mapState.pan.x + a.x * mapState.zoom, y1 = mapState.pan.y + a.y * mapState.zoom;
        const x2 = mapState.pan.x + b.x * mapState.zoom, y2 = mapState.pan.y + b.y * mapState.zoom;
        const line = document.createElement('div');
        line.className = className;
        line.style.left = x1 + 'px';
        line.style.top = y1 + 'px';
        line.style.width = Math.hypot(x2 - x1, y2 - y1) + 'px';
        line.style.transform = 'rotate(' + (Math.atan2(y2 - y1, x2 - x1) * 180 / Math.PI) + 'deg)';
        mapState.handlesOverlay.appendChild(line);
    }
}
