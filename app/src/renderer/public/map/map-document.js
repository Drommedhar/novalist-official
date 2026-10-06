import { mapState } from './map-state.js';
import { closeContextMenu } from './map-context-menu.js';
import { log } from './map-messages.js';
import { walkNodes } from './map-layers.js';
import { renderTerrainShape } from './terrain-editing.js';
import { renderImage } from './image-rendering.js';
import { renderBuilding } from './building-rendering.js';
import { renderSplines } from './spline-rendering.js';
import { renderPin } from './pin-rendering.js';
import { renderLabel } from './label-editing.js';
import { renderFloorAnnotations } from './building-editing.js';
import { applyTransform } from './map-navigation.js';

export function setMapData(json) {
    closeContextMenu();
    try { mapState.mapData = JSON.parse(json) || { layers: [], pins: [] }; }
    catch (e) { mapState.mapData = { layers: [], pins: [] }; log('setMapData parse error: ' + e.message); }
    if (!mapState.mapData.layers) mapState.mapData.layers = [];
    if (!mapState.mapData.pins) mapState.mapData.pins = [];
    if (!mapState.mapData.labels) mapState.mapData.labels = [];
    if (!mapState.mapData.customProfiles) mapState.mapData.customProfiles = [];
    const rect = mapState.stage.getBoundingClientRect();
    mapState.pan.x = mapState.mapData.initialView?.centerX || (rect.width / 2);
    mapState.pan.y = mapState.mapData.initialView?.centerY || (rect.height / 2);
    mapState.zoom = mapState.mapData.initialView?.zoom || 1;
    let nodeCount = 0, imageCount = 0;
    walkNodes(mapState.mapData.layers, n => { nodeCount++; imageCount += (n.images || []).length; });
    log(`setMapData nodes=${nodeCount} images=${imageCount} pins=${(mapState.mapData.pins || []).length} stage=${rect.width}x${rect.height} pan=${mapState.pan.x},${mapState.pan.y} zoom=${mapState.zoom}`);
    render();
}

export function setImageBaseUrl(url) {
    mapState.imageBaseUrl = url || '';
    log('setImageBaseUrl = ' + mapState.imageBaseUrl);
    render();
}

export function setMode(m) {
    mapState.mode = m === 'view' ? 'view' : 'edit';
    document.body.classList.toggle('mode-edit', mapState.mode === 'edit');
    document.body.classList.toggle('mode-view', mapState.mode === 'view');
}

export function render() {
    mapState.world.innerHTML = '';
    mapState.pinOverlay.innerHTML = '';
    mapState.labelOverlay.innerHTML = '';
    // Depth-first walk: terrain shapes then images render in tree order so
    // deeper / later nodes paint on top. updateLayerVisibility() (via
    // applyTransform) handles the hidden / connected-set / zoom-range / isolate
    // cascade afterwards.
    walkNodes(mapState.mapData.layers, node => {
        for (const shape of node.shapes || []) renderTerrainShape(node, shape);
        for (const img of node.images || []) renderImage(node, img);
        for (const b of node.buildings || []) renderBuilding(node, b);
    });
    renderSplines();
    mapState.world.appendChild(mapState.splineSvg); // re-attach after innerHTML wipe; sits above images
    for (const pin of mapState.mapData.pins || []) renderPin(pin);
    for (const lbl of mapState.mapData.labels || []) renderLabel(lbl);
    renderFloorAnnotations(); // active-floor labels / pins for plan-showing buildings
    mapState.lastZoomApplied = -1; // force renderSplines() coords refresh on next applyTransform
    applyTransform();
    // Keep the bottom-bar's gating in sync with the latest data after any rebuild.
    if (typeof mapState.renderBottomBar === 'function') mapState.renderBottomBar();
}
