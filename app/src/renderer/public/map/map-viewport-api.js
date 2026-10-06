import { mapState } from './map-state.js';
import { computeNodeState, walkNodes, findLayer } from './map-layers.js';
import { setMapData, setImageBaseUrl, setMode, render } from './map-document.js';
import { log, sendMessage } from './map-messages.js';
import { applyTransform } from './map-navigation.js';
import { findPinById, rollBuildingDraft } from './building-editing.js';
import { updateHud, applyToolModeClass } from './map-measurement.js';
import { commitSplineDraft } from './spline-draft.js';
import { commitTerrainDraft } from './terrain-editing.js';
import { enterBorderEdit, commitBorderDraft } from './border-editing.js';
import { clearSelection } from './map-selection.js';
import { closeContextMenu } from './map-context-menu.js';

// Compute world-space bounding box of all currently-visible images.
export function visibleImageBounds() {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    const state = computeNodeState();
    walkNodes(mapState.mapData.layers, node => {
        const st = state.get(node.id);
        if (st && !st.visible) return;
        for (const img of node.images || []) {
            const x = img.x || 0, y = img.y || 0;
            const w = img.width || 0, h = img.height || 0;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w;
            if (y + h > maxY) maxY = y + h;
        }
    });
    if (minX === Infinity) return null;
    return { minX, minY, maxX, maxY };
}

export function firstLeafNode() {
    let leaf = null;
    walkNodes(mapState.mapData.layers, n => {
        if (!leaf && (!n.children || n.children.length === 0)) leaf = n;
    });
    return leaf;
}

export function publishMapViewportApi() {
    window.setMapData = setMapData;
    window.setImageBaseUrl = setImageBaseUrl;
    window.setMode = setMode;
    window.getMapData = () => JSON.stringify(mapState.mapData);
    window.setActiveLayer = (layerId) => { mapState.activeLayerId = layerId || ''; log('setActiveLayer = ' + mapState.activeLayerId); };
    window.zoomToFit = () => {
        const b = visibleImageBounds();
        const rect = mapState.stage.getBoundingClientRect();
        if (!b) { // nothing to fit — reset
            mapState.pan.x = rect.width / 2; mapState.pan.y = rect.height / 2; mapState.zoom = 1;
        } else {
            const bw = Math.max(1, b.maxX - b.minX);
            const bh = Math.max(1, b.maxY - b.minY);
            const margin = 0.92;
            mapState.zoom = Math.max(0.05, Math.min(10, Math.min(rect.width / bw, rect.height / bh) * margin));
            const cx = (b.minX + b.maxX) / 2;
            const cy = (b.minY + b.maxY) / 2;
            mapState.pan.x = rect.width / 2 - cx * mapState.zoom;
            mapState.pan.y = rect.height / 2 - cy * mapState.zoom;
        }
        mapState.lastZoomApplied = -1;
        applyTransform();
        mapState.mapData.initialView = { centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom };
        sendMessage({ type: 'viewChanged', centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom });
    };
    window.resetView = () => {
        const rect = mapState.stage.getBoundingClientRect();
        mapState.pan.x = rect.width / 2;
        mapState.pan.y = rect.height / 2;
        mapState.zoom = 1;
        mapState.lastZoomApplied = -1;
        applyTransform();
        mapState.mapData.initialView = { centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom };
        sendMessage({ type: 'viewChanged', centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom });
    };
    window.focusOnPin = (pinId) => {
        const pin = findPinById(pinId);
        if (!pin) return;
        const rect = mapState.stage.getBoundingClientRect();
        const targetZoom = Math.max(1, Math.min(4, mapState.zoom < 1.5 ? 1.8 : mapState.zoom));
        mapState.zoom = targetZoom;
        mapState.pan.x = rect.width  / 2 - pin.x * mapState.zoom;
        mapState.pan.y = rect.height / 2 - pin.y * mapState.zoom;
        mapState.lastZoomApplied = -1;
        applyTransform();
        sendMessage({ type: 'viewChanged', centerX: mapState.pan.x, centerY: mapState.pan.y, zoom: mapState.zoom });
        const host = mapState.pinOverlay.querySelector(`.nv-pin-host[data-pin-id="${pinId}"]`);
        if (host) {
            host.classList.remove('nv-pin-flash');
            void host.offsetWidth; // force reflow so animation can restart
            host.classList.add('nv-pin-flash');
        }
    };
}

export function publishMapImageAndScaleApi() {
    window.addImageToMap = (relPath, w, h) => {
        log(`addImageToMap relPath="${relPath}" w=${w} h=${h} activeLayer=${mapState.activeLayerId}`);
        if (!mapState.mapData.layers) mapState.mapData.layers = [];
        // Resolve target node: prefer the active layer, else first leaf, else create one.
        let node = mapState.activeLayerId ? findLayer(mapState.activeLayerId) : null;
        if (!node) node = firstLeafNode();
        if (!node) {
            node = { id: 'layer-1', name: 'Base', opacity: 1, locked: false, hidden: false, expanded: true, images: [], children: [] };
            mapState.mapData.layers.push(node);
        }
        if (!node.images) node.images = [];
        log(`addImageToMap target node=${node.id} (hidden=${node.hidden} minZoom=${node.minZoom} maxZoom=${node.maxZoom})`);
        const stageRect = mapState.stage.getBoundingClientRect();
        const cx = (stageRect.width / 2 - mapState.pan.x) / mapState.zoom;
        const cy = (stageRect.height / 2 - mapState.pan.y) / mapState.zoom;
        const ww = w || 256;
        const hh = h || 256;
        node.images.push({
            id: 'image-' + Date.now(),
            path: relPath,
            x: cx - ww / 2,
            y: cy - hh / 2,
            width: ww,
            height: hh,
            rotation: 0,
        });
        render();
        sendMessage({ type: 'mapChanged' });
    };
    window.setMapScale = (scale) => {
        if (!scale || !(scale.unitsPer > 0)) {
            delete mapState.mapData.scale;
        } else {
            mapState.mapData.scale = {
                unitsPer: scale.unitsPer,
                unit: scale.unit || '',
                gridSpacing: scale.gridSpacing > 0 ? scale.gridSpacing : 0
            };
        }
        updateHud();
        sendMessage({ type: 'mapChanged' });
    };
    window.getMapScale = () => mapState.mapData.scale || null;
}

export function publishMapToolApi() {
    window.setToolMode = (m) => {
        const want = (m === 'add-pin' || m === 'add-label' || m === 'spline'
            || m === 'terrain' || m === 'border' || m === 'building'
            || m === 'ruler') ? m : 'select';
        // Leaving the ruler drops a half-measured distance rather than keeping a
        // start point that would attach itself to an unrelated later click.
        if (want !== 'ruler') mapState.rulerFrom = null;
        // 'border' with a border already present = edit its vertices, not draw a new one.
        if (want === 'border' && mapState.mapData.border) {
            mapState.toolMode = 'select';
            mapState.stage.style.cursor = 'default';
            if (mapState.splineDraft) commitSplineDraft();
            if (mapState.terrainDraft) commitTerrainDraft();
            enterBorderEdit();
            log('toolMode = select (border edit)');
            return;
        }
        mapState.toolMode = want;
        applyToolModeClass();
        if (mapState.toolMode !== 'select') {
            clearSelection();
        }
        if (mapState.toolMode !== 'spline' && mapState.splineDraft) commitSplineDraft();
        if (mapState.toolMode !== 'terrain' && mapState.terrainDraft) commitTerrainDraft();
        if (mapState.toolMode !== 'border' && mapState.borderDraft) commitBorderDraft();
        if (mapState.toolMode === 'building') {
            rollBuildingDraft();
        } else if (mapState.buildingDraft) {
            mapState.buildingDraft = null;
            const dr = mapState.world.querySelector('.nv-building[data-building-id="__buildingDraft__"]');
            if (dr) dr.remove();
        }
        closeContextMenu();
        mapState.renderBottomBar();
        log('toolMode = ' + mapState.toolMode);
    };
}
