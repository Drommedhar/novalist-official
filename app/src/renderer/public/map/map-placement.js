import { mapState } from './map-state.js';
import { rulerClick } from './map-measurement.js';
import { sendMessage } from './map-messages.js';
import { renderLabel, beginLabelEdit } from './label-editing.js';
import { startPan } from './map-navigation.js';
import { trySplinePoint } from './spline-draft.js';
import { tryPlaceTerrainPoint } from './terrain-editing.js';
import { tryPlaceBorderPoint } from './border-editing.js';
import { tryPlaceBuilding } from './building-editing.js';

// Place-pin mode short-circuits ALL element interactions: any left-click drops a pin,
// even on top of images or other pins. Returns true if the click was consumed.
/**
 * Ruler: two clicks and a distance.
 *
 * Available while reading as well as while editing - "how far is that" is a
 * question about the world, not an edit to it.
 */
export function tryRulerPoint(e) {
    if (mapState.toolMode !== 'ruler') return false;
    if (e.button !== 0) return false;
    e.stopPropagation();
    e.preventDefault();
    rulerClick((e.clientX - mapState.pan.x) / mapState.zoom, (e.clientY - mapState.pan.y) / mapState.zoom);
    return true;
}

export function tryPlacePin(e) {
    if (mapState.toolMode !== 'add-pin' || mapState.mode !== 'edit') return false;
    if (e.button !== 0) return false;
    e.stopPropagation();
    e.preventDefault();
    const wx = (e.clientX - mapState.pan.x) / mapState.zoom;
    const wy = (e.clientY - mapState.pan.y) / mapState.zoom;
    sendMessage({ type: 'placePinAt', x: wx, y: wy });
    return true;
}

// Label tool: any left-click drops a new (empty) text label right where you
// clicked and immediately puts it into inline-edit mode so you can type. The
// tool is one-shot — it returns to the select tool after placing.
export function tryPlaceLabel(e) {
    if (mapState.toolMode !== 'add-label' || mapState.mode !== 'edit') return false;
    if (e.button !== 0) return false;
    e.stopPropagation();
    e.preventDefault();
    const wx = (e.clientX - mapState.pan.x) / mapState.zoom;
    const wy = (e.clientY - mapState.pan.y) / mapState.zoom;
    const lbl = { id: 'label-' + Date.now(), x: wx, y: wy,
                  text: '', fontSize: 18, fontFamily: '', align: 'center', color: '#ffffff',
                  layerId: mapState.activeLayerId || '' };
    mapState.mapData.labels = mapState.mapData.labels || [];
    mapState.mapData.labels.push(lbl);
    const el = renderLabel(lbl);
    mapState.toolMode = 'select';
    mapState.stage.style.cursor = 'default';
    sendMessage({ type: 'cancelLabelPlace' }); // host clears IsLabelPlaceMode
    beginLabelEdit(lbl, el);
    return true;
}

export function repositionPins() {
    const hosts = mapState.pinOverlay.querySelectorAll('.nv-pin-host');
    hosts.forEach(host => {
        const x = parseFloat(host.dataset.worldX) || 0;
        const y = parseFloat(host.dataset.worldY) || 0;
        host.style.left = (mapState.pan.x + x * mapState.zoom) + 'px';
        host.style.top  = (mapState.pan.y + y * mapState.zoom) + 'px';
    });
}

export function repositionLabels() {
    mapState.labelOverlay.querySelectorAll('.nv-label').forEach(el => {
        const x = parseFloat(el.dataset.worldX) || 0;
        const y = parseFloat(el.dataset.worldY) || 0;
        const fs = parseFloat(el.dataset.fontSize) || 18;
        el.style.left = (mapState.pan.x + x * mapState.zoom) + 'px';
        el.style.top  = (mapState.pan.y + y * mapState.zoom) + 'px';
        el.style.fontSize = (fs * mapState.zoom) + 'px';
    });
}

export function handleMapPlacement(e) {
    if (e.button === 1) {
        e.preventDefault();
        e.stopPropagation();
        startPan(e);
        return true;
    }
    return trySplinePoint(e) || tryPlacePin(e) || tryPlaceLabel(e)
        || tryPlaceTerrainPoint(e) || tryPlaceBorderPoint(e) || tryPlaceBuilding(e);
}
