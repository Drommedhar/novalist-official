import { mapState } from './map-state.js';
import { sendMessage } from './map-messages.js';
import { applyToolModeClass } from './map-measurement.js';
import { clearSelection } from './map-selection.js';
import { commitSplineDraft } from './spline-draft.js';
import { commitTerrainDraft, distToSegment } from './terrain-editing.js';
import { renderPolygonEdges } from './map-navigation.js';

// editing the committed border's vertices

// Hide everything outside the border polygon via clip-path. #world is
// translated by pan only (its local coords are world*zoom); the pin/label
// overlays are screen-space (pan + world*zoom).
export function applyBorderClip() {
    const b = mapState.mapData.border;
    if (!b || !b.points || b.points.length < 3) {
        mapState.world.style.clipPath = '';
        mapState.pinOverlay.style.clipPath = '';
        mapState.labelOverlay.style.clipPath = '';
        return;
    }
    const wp = b.points.map(p => (p.x * mapState.zoom).toFixed(1) + 'px ' + (p.y * mapState.zoom).toFixed(1) + 'px').join(',');
    const sp = b.points.map(p => (mapState.pan.x + p.x * mapState.zoom).toFixed(1) + 'px ' + (mapState.pan.y + p.y * mapState.zoom).toFixed(1) + 'px').join(',');
    mapState.world.style.clipPath = `polygon(${wp})`;
    mapState.pinOverlay.style.clipPath = `polygon(${sp})`;
    mapState.labelOverlay.style.clipPath = `polygon(${sp})`;
}

export function renderBorderOutline() {
    mapState.borderOutline.innerHTML = '';
    const b = mapState.mapData.border;
    const pts = mapState.borderDraft ? mapState.borderDraft.points : (b && b.points);
    if (!pts || pts.length < 2) return;
    const poly = document.createElementNS(mapState.SVG_NS, 'polygon');
    poly.setAttribute('points', pts.map(p =>
        (mapState.pan.x + p.x * mapState.zoom).toFixed(1) + ',' + (mapState.pan.y + p.y * mapState.zoom).toFixed(1)).join(' '));
    poly.setAttribute('fill', 'none');
    poly.setAttribute('stroke', (b && b.outlineColor) || '#1c1a18');
    poly.setAttribute('stroke-width', (((b && b.outlineWidth) || 4) * mapState.zoom).toFixed(2));
    poly.setAttribute('stroke-linejoin', 'round');
    if (mapState.borderDraft) poly.setAttribute('stroke-dasharray', '8 6');
    // Clickable (for double-click insert) only while editing the committed border.
    poly.style.pointerEvents = mapState.borderEdit ? 'stroke' : 'none';
    if (mapState.borderEdit) {
        poly.addEventListener('dblclick', e => {
            if (mapState.mode !== 'edit') return;
            e.stopPropagation();
            insertBorderVertexAt((e.clientX - mapState.pan.x) / mapState.zoom, (e.clientY - mapState.pan.y) / mapState.zoom);
        });
    }
    mapState.borderOutline.appendChild(poly);
}

export function refreshBorder() {
    applyBorderClip();
    renderBorderOutline();
}

export function tryPlaceBorderPoint(e) {
    if (mapState.toolMode !== 'border' || mapState.mode !== 'edit') return false;
    if (e.button !== 0) return false;
    e.stopPropagation(); e.preventDefault();
    if (!mapState.borderDraft) mapState.borderDraft = { points: [] };
    mapState.borderDraft.points.push({ x: (e.clientX - mapState.pan.x) / mapState.zoom, y: (e.clientY - mapState.pan.y) / mapState.zoom });
    renderBorderOutline();
    renderBorderEditor();
    return true;
}

export function commitBorderDraft() {
    if (!mapState.borderDraft) return;
    if (mapState.borderDraft.points.length < 3) {
        mapState.borderDraft = null; refreshBorder(); renderBorderEditor(); return;
    }
    const prev = mapState.mapData.border;
    mapState.mapData.border = {
        points: mapState.borderDraft.points.map(p => ({ x: p.x, y: p.y })),
        outlineColor: (prev && prev.outlineColor) || '#1c1a18',
        outlineWidth: (prev && prev.outlineWidth) || 4,
    };
    mapState.borderDraft = null;
    refreshBorder();
    renderBorderEditor();
    sendMessage({ type: 'mapChanged' });
    mapState.toolMode = 'select';
    applyToolModeClass();
    sendMessage({ type: 'cancelBorderMode' });
    mapState.renderBottomBar();
}

export function cancelBorderDraft() {
    mapState.borderDraft = null;
    refreshBorder();
    renderBorderEditor();
}

export function borderSelectionPayload() {
    const b = mapState.mapData.border;
    return {
        type: 'borderSelected',
        outlineColor: (b && b.outlineColor) || '#1c1a18',
        outlineWidth: (b && b.outlineWidth) || 4,
    };
}

export function enterBorderEdit() {
    if (!mapState.mapData.border) return;
    if (mapState.borderEdit) return;
    clearSelection();
    if (mapState.splineDraft) commitSplineDraft();
    if (mapState.terrainDraft) commitTerrainDraft();
    mapState.borderEdit = true;
    refreshBorder();
    renderBorderEditor();
    sendMessage(borderSelectionPayload());
    mapState.renderBottomBar();
}

export function exitBorderEdit() {
    if (!mapState.borderEdit) return;
    mapState.borderEdit = false;
    refreshBorder();
    renderBorderEditor();
    sendMessage({ type: 'borderDeselected' });
    mapState.renderBottomBar();
}

// (showBorderToolbar / hideBorderToolbar removed — bottom bar driven by renderBottomBar)

export function renderBorderEditor() {
    Array.from(mapState.handlesOverlay.querySelectorAll('.nv-border-handle, .nv-border-line')).forEach(n => n.remove());
    let points = null, committed = false;
    if (mapState.borderDraft) points = mapState.borderDraft.points;
    else if (mapState.borderEdit && mapState.mapData.border) { points = mapState.mapData.border.points; committed = true; }
    if (!points) return;
    renderPolygonEdges(points, 'nv-border-line');
    points.forEach((p, idx) => {
        const sx = mapState.pan.x + p.x * mapState.zoom, sy = mapState.pan.y + p.y * mapState.zoom;
        const h = document.createElement('div');
        h.className = 'nv-handle nv-border-handle';
        h.style.left = sx + 'px';
        h.style.top = sy + 'px';
        h.addEventListener('mousedown', e => startBorderVertexDrag(e, points, idx, committed));
        h.addEventListener('contextmenu', e => {
            e.preventDefault(); e.stopPropagation();
            if (points.length > 3) {
                points.splice(idx, 1);
                refreshBorder();
                renderBorderEditor();
                if (committed) sendMessage({ type: 'mapChanged' });
            }
        });
        mapState.handlesOverlay.appendChild(h);
    });
}

export function startBorderVertexDrag(ev, points, idx, committed) {
    ev.stopPropagation(); ev.preventDefault();
    function move(e) {
        points[idx].x = (e.clientX - mapState.pan.x) / mapState.zoom;
        points[idx].y = (e.clientY - mapState.pan.y) / mapState.zoom;
        refreshBorder();
        renderBorderEditor();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        if (committed) sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

// Double-clicking the border outline inserts a vertex on the nearest edge.
export function insertBorderVertexAt(wx, wy) {
    const b = mapState.mapData.border;
    if (!b || b.points.length < 3) return;
    const pts = b.points, n = pts.length;
    let bestSeg = 0, bestD = Infinity;
    for (let i = 0; i < n; i++) {
        const a = pts[i], c = pts[(i + 1) % n];
        const d = distToSegment(wx, wy, a.x, a.y, c.x, c.y);
        if (d < bestD) { bestD = d; bestSeg = i; }
    }
    pts.splice(bestSeg + 1, 0, { x: wx, y: wy });
    refreshBorder();
    renderBorderEditor();
    sendMessage({ type: 'mapChanged' });
}

export function initializeBorderEditing() {
    mapState.borderDraft = null;
    mapState.borderEdit = false;
    window.setBorderOutline = (color, width) => {
        if (!mapState.mapData.border) return;
        mapState.mapData.border.outlineColor = color || '#1c1a18';
        mapState.mapData.border.outlineWidth = width > 0 ? width : 4;
        renderBorderOutline();
        sendMessage({ type: 'mapChanged' });
    };
    window.clearBorder = () => {
        mapState.mapData.border = null;
        mapState.borderEdit = false;
        refreshBorder();
        renderBorderEditor();
        sendMessage({ type: 'borderDeselected' });
        sendMessage({ type: 'mapChanged' });
        mapState.renderBottomBar();
    };
}
