import { mapState } from './map-state.js';
import { handleMapPlacement } from './map-placement.js';
import { sendMessage } from './map-messages.js';
import { selectPin } from './label-editing.js';
import { showSimpleContextMenu } from './map-context-menu.js';

/**
 * The marker element for a pin: a shape when one was chosen, the dot otherwise.
 *
 * An unknown icon name falls back to the dot rather than drawing nothing. A pin
 * that disappears because a name was mistyped is worse than one that looks
 * plain.
 */
export function buildPinMarker(pin) {
    const geometry = pin.style === 'svg' && pin.iconPath ? mapState.PIN_ICONS[pin.iconPath] : null;
    if (!geometry) {
        const dot = document.createElement('div');
        dot.className = 'nv-pin-marker';
        if (pin.color) dot.style.background = pin.color;
        return dot;
    }

    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'nv-pin-marker nv-pin-icon');
    svg.setAttribute('viewBox', '0 0 24 24');
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', geometry);
    // Stroked and not filled: an outline stays legible over painted terrain,
    // where a solid glyph turns into a blob at low zoom.
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke', pin.color || '#f9c46a');
    path.setAttribute('stroke-width', '2');
    path.setAttribute('stroke-linecap', 'round');
    path.setAttribute('stroke-linejoin', 'round');
    svg.appendChild(path);
    return svg;
}

export function renderPin(pin) {
    const host = document.createElement('div');
    host.className = 'nv-pin-host' + (mapState.selected?.kind === 'pin' && mapState.selected.id === pin.id ? ' selected' : '');
    host.dataset.pinId = pin.id;
    host.dataset.worldX = pin.x;
    host.dataset.worldY = pin.y;
    host.dataset.layerId = pin.layerId || '';
    // Initial screen position (repositionPins() keeps it in sync afterwards).
    host.style.left = (mapState.pan.x + pin.x * mapState.zoom) + 'px';
    host.style.top = (mapState.pan.y + pin.y * mapState.zoom) + 'px';

    if (pin.label) {
        const lbl = document.createElement('div');
        lbl.className = 'nv-pin-label';
        lbl.textContent = pin.label;
        host.appendChild(lbl);
    }
    host.appendChild(buildPinMarker(pin));

    host.addEventListener('mousedown', (e) => {
        if (handleMapPlacement(e)) return;
        e.stopPropagation();
        if (mapState.mode === 'view') {
            sendMessage({
                type: 'pinClick', pinId: pin.id,
                entityId: pin.entityId || '', entityType: pin.entityType || '',
                // A pin resolved to a Codex entry and nothing else, so a world
                // map could mark a city and never lead to the city's own map.
                targetMapId: pin.targetMapId || ''
            });
            return;
        }
        if (e.button === 2) return; // right-click handled below
        if (mapState.toolMode !== 'select') return;
        selectPin(pin, host);
        startPinDrag(e, pin, host);
    });
    host.addEventListener('contextmenu', (e) => {
        e.preventDefault(); e.stopPropagation();
        if (mapState.mode !== 'edit') return;
        showSimpleContextMenu(e, [
            [mapState.mapStrings.ctxDelete, () => window.deletePin(pin.id)],
        ]);
    });
    mapState.pinOverlay.appendChild(host);
}

export function startPinDrag(ev, pin, host) {
    const start = { x: ev.clientX, y: ev.clientY, px: pin.x, py: pin.y };
    function move(e) {
        const dx = (e.clientX - start.x) / mapState.zoom;
        const dy = (e.clientY - start.y) / mapState.zoom;
        pin.x = start.px + dx;
        pin.y = start.py + dy;
        host.dataset.worldX = pin.x;
        host.dataset.worldY = pin.y;
        host.style.left = (mapState.pan.x + pin.x * mapState.zoom) + 'px';
        host.style.top  = (mapState.pan.y + pin.y * mapState.zoom) + 'px';
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

export function initializePinRendering() {
    mapState.PIN_ICONS = {
        city: 'M3 21V9l5-3 5 3v12M13 21V13l4-2 4 2v8M6 13h2M6 17h2M16 16h2',
        town: 'M4 21V11l6-4 6 4v10M9 21v-5h4v5',
        village: 'M6 20v-6l4-3 4 3v6M4 14l6-5 6 5',
        castle: 'M4 21V8l2-2 2 2 2-2 2 2 2-2 2 2 2-2 2 2v13M10 21v-6h4v6',
        tower: 'M9 21V6l3-3 3 3v15M9 10h6M9 15h6',
        ruin: 'M4 21V10l3 2V8l4 3V9l3 2M17 21v-7l3 2v6M4 21h16',
        temple: 'M3 21h18M5 21V10M9 21V10M15 21V10M19 21V10M2 10h20L12 3z',
        mountain: 'M2 20h20L14 6l-4 7-2-3z',
        hills: 'M2 20c3-6 5-6 8 0M10 20c3-7 5-7 8 0M2 20h20',
        forest: 'M12 3l5 8h-3l4 6H6l4-6H7zM12 17v4',
        lake: 'M3 14c3-3 6 3 9 0s6-3 9 0M3 18c3-3 6 3 9 0s6-3 9 0',
        port: 'M12 3v16M8 7h8M5 13a7 7 0 0 0 14 0M12 19a7 7 0 0 1-7-6',
        bridge: 'M2 12h20M4 12v6M20 12v6M8 12a4 4 0 0 1 8 0',
        mine: 'M3 20l9-9M8 4l4 4M8 4l-4 4 4 4 4-4M12 11l8 9',
        camp: 'M12 4L4 20h16zM12 4v16',
        crossroads: 'M12 2v20M2 12h20',
        cave: 'M4 21v-7a8 8 0 0 1 16 0v7M9 21v-4a3 3 0 0 1 6 0v4',
        battle: 'M4 4l10 10M6 20l4-4M4 18l2 2M20 4L10 14M18 20l-4-4M20 18l-2 2'
    };
}
