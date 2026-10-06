import { mapState } from './map-state.js';
import { render } from './map-document.js';
import { sendMessage } from './map-messages.js';
import { findPinById } from './building-editing.js';
import { selectPin } from './label-editing.js';
import { buildPinMarker } from './pin-rendering.js';
import { renderSelectedBar } from './selection-toolbar.js';
import { walkNodes } from './map-layers.js';

export function _createPin(wx, wy, label, entityType, entityId, color) {
    mapState.mapData.pins = mapState.mapData.pins || [];
    const id = 'pin-' + Date.now();
    mapState.mapData.pins.push({
        id, x: wx, y: wy, style: 'dot',
        label: label || '',
        entityType: entityType || '',
        entityId: entityId || '',
        color: color || null,
        layerId: mapState.activeLayerId || '',
    });
    render();
    sendMessage({ type: 'mapChanged' });
    // Auto-select so the bottom-bar editor shows immediately.
    const pin = findPinById(id);
    const host = mapState.pinOverlay.querySelector(`.nv-pin-host[data-pin-id="${id}"]`);
    if (pin) selectPin(pin, host);
    return id;
}

export function publishPinCreationApi() {
    window.addPinAtPoint = (wx, wy, label, entityType, entityId, color) =>
        _createPin(wx, wy, label, entityType, entityId, color);
    window.addPinAtCenter = (label, entityType, entityId, color) => {
        const stageRect = mapState.stage.getBoundingClientRect();
        const cx = (stageRect.width / 2 - mapState.pan.x) / mapState.zoom;
        const cy = (stageRect.height / 2 - mapState.pan.y) / mapState.zoom;
        return _createPin(cx, cy, label, entityType, entityId, color);
    };
}

/**
 * Swaps a drawn pin's marker for the one its current style and colour call for.
 * Rebuilt rather than restyled because a dot and an icon are different
 * elements, and a colour change on one has to reach the other.
 */
export function replacePinMarker(pin) {
    const host = mapState.pinOverlay.querySelector(`.nv-pin-host[data-pin-id="${pin.id}"]`);
    if (!host) return;
    const existing = host.querySelector('.nv-pin-marker');
    const marker = buildPinMarker(pin);
    if (existing) host.replaceChild(marker, existing);
    else host.appendChild(marker);
}

export function publishPinPropertiesApi() {
    window.updatePinColor = (pinId, color) => {
        const pin = findPinById(pinId);
        if (!pin) return;
        pin.color = color || null;
        replacePinMarker(pin);
        sendMessage({ type: 'mapChanged' });
    };
    window.setPinIcon = (pinId, iconName) => {
        const pin = findPinById(pinId);
        if (!pin) return;
        const name = iconName || '';
        pin.style = name ? 'svg' : 'dot';
        pin.iconPath = name || null;
        replacePinMarker(pin);
        sendMessage({ type: 'mapChanged' });
    };
    window.getPinIcons = () => Object.keys(mapState.PIN_ICONS);
    window.updatePin = (pinId, label, entityType, entityId, color) => {
        const pin = findPinById(pinId);
        if (!pin) return;
        pin.label = label || '';
        pin.entityType = entityType || '';
        pin.entityId = entityId || '';
        pin.color = color || null;
        render();
        sendMessage({ type: 'mapChanged' });
    };
    window.setPinLabel = (pinId, label) => {
        const pin = findPinById(pinId);
        if (!pin) return;
        pin.label = label || '';
        render();
        sendMessage({ type: 'mapChanged' });
    };
    mapState.otherMaps = [];
    window.setOtherMaps = (list) => {
        mapState.otherMaps = Array.isArray(list) ? list.filter(m => m && m.id && m.id !== mapState.mapData.id) : [];
        // `selected` is null whenever nothing is picked, which is every ordinary
        // load - so this threw on the way in, and because the host calls it in the
        // middle of loading a map it took the rest of that load with it: the
        // resize nudge and the fit-to-view never ran, and whether a map appeared
        // came down to whether the stage happened to be sized already.
        if (mapState.selected && mapState.selected.kind === 'pin') renderSelectedBar();
    };
    window.setPinTargetMap = (pinId, targetMapId) => {
        const pin = findPinById(pinId);
        if (!pin) return;
        if (targetMapId) pin.targetMapId = targetMapId;
        else delete pin.targetMapId;
        sendMessage({ type: 'mapChanged' });
    };
    window.setPinEntity = (pinId, entityId, entityType) => {
        const pin = findPinById(pinId);
        if (!pin) return;
        pin.entityId = entityId || '';
        pin.entityType = entityType || '';
        render();
        sendMessage({ type: 'mapChanged' });
    };
    window.setPinColor = window.updatePinColor;
}

// Remove a pin by id from the top-level list or any building floor.
export function removePinById(pinId) {
    const before = (mapState.mapData.pins || []).length;
    mapState.mapData.pins = (mapState.mapData.pins || []).filter(p => p.id !== pinId);
    if (mapState.mapData.pins.length !== before) return true;
    let removed = false;
    walkNodes(mapState.mapData.layers, n => {
        for (const b of n.buildings || [])
            for (const fl of b.floors || [])
                if (fl.pins) {
                    const n0 = fl.pins.length;
                    fl.pins = fl.pins.filter(p => p.id !== pinId);
                    if (fl.pins.length !== n0) removed = true;
                }
    });
    return removed;
}

export function publishPinDeletionApi() {
    window.deletePin = (pinId) => {
        removePinById(pinId);
        if (mapState.selected?.kind === 'pin' && mapState.selected.id === pinId) mapState.selected = null;
        render();
        sendMessage({ type: 'mapChanged' });
    };
}
