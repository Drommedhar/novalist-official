import { mapState } from './map-state.js';
import { walkNodes, findLayer } from './map-layers.js';
import { render } from './map-document.js';
import { firstLeafNode } from './map-viewport-api.js';
import { sendMessage, log } from './map-messages.js';
import { clearSelection } from './map-selection.js';
import { renderPolygonEdges } from './map-navigation.js';
import { showSimpleContextMenu } from './map-context-menu.js';

export function terrainColor(type) { return mapState.TERRAIN_PRESETS[type] || mapState.TERRAIN_PRESETS.grass; }

export function findShape(shapeId) {
    let f = null;
    walkNodes(mapState.mapData.layers, n => { if (!f) f = (n.shapes || []).find(s => s.id === shapeId); });
    return f || null;
}

export function findShapeOwner(shapeId) {
    let o = null;
    walkNodes(mapState.mapData.layers, n => { if (!o && (n.shapes || []).some(s => s.id === shapeId)) o = n; });
    return o;
}

export function distToSegment(px, py, ax, ay, bx, by) {
    const dx = bx - ax, dy = by - ay;
    const len2 = dx * dx + dy * dy;
    let t = len2 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0;
    t = Math.max(0, Math.min(1, t));
    return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
}

// SVG path 'd' for a shape's closed polygon, authored in WORLD units.
export function shapePathD(points, smooth) {
    if (!points || points.length < 2) return '';
    if (!smooth || points.length < 3)
        return 'M' + points.map(p => p.x.toFixed(2) + ',' + p.y.toFixed(2)).join(' L') + ' Z';
    // Closed Catmull-Rom → cubic Bézier segments.
    const n = points.length;
    let d = 'M' + points[0].x.toFixed(2) + ',' + points[0].y.toFixed(2);
    for (let i = 0; i < n; i++) {
        const p0 = points[(i - 1 + n) % n], p1 = points[i];
        const p2 = points[(i + 1) % n], p3 = points[(i + 2) % n];
        const c1x = p1.x + (p2.x - p0.x) / 6, c1y = p1.y + (p2.y - p0.y) / 6;
        const c2x = p2.x - (p3.x - p1.x) / 6, c2y = p2.y - (p3.y - p1.y) / 6;
        d += ' C' + c1x.toFixed(2) + ',' + c1y.toFixed(2) + ' '
                  + c2x.toFixed(2) + ',' + c2y.toFixed(2) + ' '
                  + p2.x.toFixed(2) + ',' + p2.y.toFixed(2);
    }
    return d + ' Z';
}

// Build (but don't append) the <svg> for one shape. Geometry is world-unit;
// the current zoom is applied as a CSS scale by applyZoomToAllTerrain().
export function buildShapeSvg(node, shape) {
    const svg = document.createElementNS(mapState.SVG_NS, 'svg');
    svg.setAttribute('class', 'nv-terrain');
    svg.dataset.layerId = node.id || '';
    svg.dataset.shapeId = shape.id;
    svg.style.transform = `scale(${mapState.zoom})`;
    const path = document.createElementNS(mapState.SVG_NS, 'path');
    path.setAttribute('d', shapePathD(shape.points, shape.smooth !== false));
    path.setAttribute('fill', shape.color || terrainColor(shape.type));
    const blur = shape.blendStrength || 0;
    if (blur > 0) {
        const fid = 'feather-' + shape.id;
        const defs = document.createElementNS(mapState.SVG_NS, 'defs');
        const filter = document.createElementNS(mapState.SVG_NS, 'filter');
        filter.setAttribute('id', fid);
        filter.setAttribute('x', '-50%'); filter.setAttribute('y', '-50%');
        filter.setAttribute('width', '200%'); filter.setAttribute('height', '200%');
        const fe = document.createElementNS(mapState.SVG_NS, 'feGaussianBlur');
        fe.setAttribute('in', 'SourceGraphic');
        fe.setAttribute('stdDeviation', (blur / 2).toFixed(2));
        filter.appendChild(fe);
        defs.appendChild(filter);
        svg.appendChild(defs);
        path.setAttribute('filter', `url(#${fid})`);
    }
    if (shape.id !== '__terrainDraft__') {
        path.style.cursor = 'pointer';
        path.addEventListener('mousedown', e => {
            if (e.button !== 0) return; // middle/right bubble (pan / context menu)
            if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
            e.stopPropagation();
            selectShape(shape.id);
        });
        path.addEventListener('dblclick', e => {
            if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
            e.stopPropagation(); e.preventDefault();
            insertTerrainVertexAt(shape.id, (e.clientX - mapState.pan.x) / mapState.zoom, (e.clientY - mapState.pan.y) / mapState.zoom);
        });
        path.addEventListener('contextmenu', e => {
            if (mapState.mode !== 'edit') return;
            e.preventDefault(); e.stopPropagation();
            showShapeContextMenu(e, shape);
        });
    }
    svg.appendChild(path);
    return svg;
}

export function renderTerrainShape(node, shape) {
    if (!shape.points || shape.points.length < 3) return;
    mapState.world.appendChild(buildShapeSvg(node, shape));
}

export function rebuildShapeSvgById(shapeId) {
    const owner = findShapeOwner(shapeId), sh = findShape(shapeId);
    if (!owner || !sh) return;
    const old = mapState.world.querySelector(`.nv-terrain[data-shape-id="${shapeId}"]`);
    const fresh = buildShapeSvg(owner, sh);
    if (old) old.replaceWith(fresh); else mapState.world.appendChild(fresh);
}

// Terrain tool: each left-click drops a polygon vertex, consuming the click so
// images / splines / pins underneath are not touched (like the spline tool).
export function tryPlaceTerrainPoint(e) {
    if (mapState.toolMode !== 'terrain' || mapState.mode !== 'edit') return false;
    if (e.button !== 0) return false;
    e.stopPropagation(); e.preventDefault();
    const wx = (e.clientX - mapState.pan.x) / mapState.zoom;
    const wy = (e.clientY - mapState.pan.y) / mapState.zoom;
    if (!mapState.terrainDraft) mapState.terrainDraft = { type: mapState.terrainDraftType, layerId: mapState.activeLayerId, points: [] };
    mapState.terrainDraft.points.push({ x: wx, y: wy });
    renderTerrainDraft();
    renderTerrainEditor();
    return true;
}

export function renderTerrainDraft() {
    const old = mapState.world.querySelector('.nv-terrain[data-shape-id="__terrainDraft__"]');
    if (old) old.remove();
    if (!mapState.terrainDraft || mapState.terrainDraft.points.length < 2) return;
    const svg = buildShapeSvg({ id: '__terrainDraft__' }, {
        id: '__terrainDraft__', type: mapState.terrainDraft.type,
        color: terrainColor(mapState.terrainDraft.type), smooth: true,
        blendStrength: 0, points: mapState.terrainDraft.points,
    });
    svg.style.opacity = '0.6';
    mapState.world.appendChild(svg);
}

export function commitTerrainDraft() {
    if (!mapState.terrainDraft) return;
    if (mapState.terrainDraft.points.length < 3) {
        mapState.terrainDraft = null; render(); renderTerrainEditor(); return;
    }
    const layer = findLayer(mapState.activeLayerId) || findLayer(mapState.terrainDraft.layerId) || firstLeafNode();
    if (layer) {
        layer.shapes = layer.shapes || [];
        layer.shapes.push({
            id: 'shape-' + Date.now(),
            type: mapState.terrainDraft.type,
            color: terrainColor(mapState.terrainDraft.type),
            smooth: true,
            blendStrength: 0,
            points: mapState.terrainDraft.points.map(p => ({ x: p.x, y: p.y })),
        });
    }
    mapState.terrainDraft = null;
    render();
    renderTerrainEditor();
    sendMessage({ type: 'mapChanged' });
    // One shape per tool activation — exit the tool, like the spline tool does.
    mapState.toolMode = 'select';
    mapState.stage.style.cursor = 'default';
    sendMessage({ type: 'cancelTerrainMode' });
}

export function cancelTerrainDraft() {
    mapState.terrainDraft = null;
    const old = mapState.world.querySelector('.nv-terrain[data-shape-id="__terrainDraft__"]');
    if (old) old.remove();
    renderTerrainEditor();
}

export function shapeSelectionPayload(sh) {
    return {
        type: 'shapeSelected', shapeId: sh.id,
        shapeType: sh.type || 'grass',
        color: sh.color || terrainColor(sh.type),
        smooth: sh.smooth !== false,
        blend: sh.blendStrength || 0,
    };
}

export function emitShapeSelection() {
    if (!mapState.terrainEdit) return;
    const sh = findShape(mapState.terrainEdit.shapeId);
    if (sh) sendMessage(shapeSelectionPayload(sh));
}

export function selectShape(shapeId) {
    if (mapState.terrainEdit && mapState.terrainEdit.shapeId === shapeId) return;
    clearSelection();
    mapState.terrainEdit = { shapeId, selVert: -1 };
    renderTerrainEditor();
    emitShapeSelection();
    mapState.renderBottomBar();
}

export function exitShapeEdit() {
    if (!mapState.terrainEdit) return;
    mapState.terrainEdit = null;
    renderTerrainEditor();
    sendMessage({ type: 'shapeDeselected' });
    mapState.renderBottomBar();
}

// (showTerrainToolbar / hideTerrainToolbar removed — bottom bar driven by renderBottomBar)

// Draws vertex handles + edge guide lines for the terrain draft or the shape
// being edited.
export function renderTerrainEditor() {
    Array.from(mapState.handlesOverlay.querySelectorAll('.nv-terrain-handle, .nv-terrain-line')).forEach(n => n.remove());
    let points = null, shapeId = null;
    if (mapState.terrainDraft) { points = mapState.terrainDraft.points; }
    else if (mapState.terrainEdit) {
        const sh = findShape(mapState.terrainEdit.shapeId);
        if (sh) { points = sh.points; shapeId = sh.id; }
    }
    if (!points) return;
    renderPolygonEdges(points, 'nv-terrain-line');
    const selVert = mapState.terrainEdit && mapState.terrainEdit.selVert != null ? mapState.terrainEdit.selVert : -1;
    points.forEach((p, idx) => {
        const sx = mapState.pan.x + p.x * mapState.zoom, sy = mapState.pan.y + p.y * mapState.zoom;
        const h = document.createElement('div');
        h.className = 'nv-handle nv-terrain-handle' + (idx === selVert ? ' nv-terrain-handle-sel' : '');
        h.style.left = sx + 'px';
        h.style.top = sy + 'px';
        h.dataset.idx = idx;
        h.addEventListener('mousedown', e => {
            if (shapeId && mapState.terrainEdit) mapState.terrainEdit.selVert = idx;
            startTerrainVertexDrag(e, points, idx, shapeId);
        });
        h.addEventListener('contextmenu', e => {
            e.preventDefault(); e.stopPropagation();
            if (points.length > 3) {
                points.splice(idx, 1);
                if (shapeId) { rebuildShapeSvgById(shapeId); sendMessage({ type: 'mapChanged' }); }
                else renderTerrainDraft();
                renderTerrainEditor();
            }
        });
        mapState.handlesOverlay.appendChild(h);
    });
}

export function startTerrainVertexDrag(ev, points, idx, shapeId) {
    ev.stopPropagation(); ev.preventDefault();
    function move(e) {
        points[idx].x = (e.clientX - mapState.pan.x) / mapState.zoom;
        points[idx].y = (e.clientY - mapState.pan.y) / mapState.zoom;
        if (shapeId) rebuildShapeSvgById(shapeId);
        else renderTerrainDraft();
        renderTerrainEditor();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        if (shapeId) sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

// Double-clicking a shape inserts a vertex on the nearest polygon edge.
export function insertTerrainVertexAt(shapeId, wx, wy) {
    const sh = findShape(shapeId);
    if (!sh || sh.points.length < 3) return;
    const pts = sh.points, n = pts.length;
    let bestSeg = 0, bestD = Infinity;
    for (let i = 0; i < n; i++) {
        const a = pts[i], b = pts[(i + 1) % n];
        const d = distToSegment(wx, wy, a.x, a.y, b.x, b.y);
        if (d < bestD) { bestD = d; bestSeg = i; }
    }
    pts.splice(bestSeg + 1, 0, { x: wx, y: wy });
    selectShape(shapeId);
    if (mapState.terrainEdit) mapState.terrainEdit.selVert = bestSeg + 1;
    rebuildShapeSvgById(shapeId);
    renderTerrainEditor();
    emitShapeSelection();
    sendMessage({ type: 'mapChanged' });
}

export function showShapeContextMenu(ev, sh) {
    showSimpleContextMenu(ev, [
        [mapState.mapStrings.shapeDelete, () => window.deleteShape(sh.id)],
    ]);
}

export function initializeTerrainEditing() {
    mapState.terrainDraft = null;
    mapState.terrainEdit = null;
    mapState.terrainDraftType = 'grass';
    window.setTerrainDraftType = (type) => {
        mapState.terrainDraftType = mapState.TERRAIN_PRESETS[type] ? type : 'grass';
        log('terrainDraftType = ' + mapState.terrainDraftType);
    };
    window.setShapeType = (id, type) => {
        const sh = findShape(id);
        if (!sh) return;
        sh.type = mapState.TERRAIN_PRESETS[type] ? type : 'grass';
        sh.color = terrainColor(sh.type); // type re-seeds the colour
        rebuildShapeSvgById(id);
        if (mapState.terrainEdit && mapState.terrainEdit.shapeId === id) emitShapeSelection();
        sendMessage({ type: 'mapChanged' });
    };
    window.setShapeColor = (id, hex) => {
        const sh = findShape(id);
        if (!sh) return;
        sh.color = hex || terrainColor(sh.type);
        rebuildShapeSvgById(id);
        sendMessage({ type: 'mapChanged' });
    };
    window.setShapeSmooth = (id, smooth) => {
        const sh = findShape(id);
        if (!sh) return;
        sh.smooth = !!smooth;
        rebuildShapeSvgById(id);
        sendMessage({ type: 'mapChanged' });
    };
    window.setShapeBlend = (id, strength) => {
        const sh = findShape(id);
        if (!sh) return;
        sh.blendStrength = Math.max(0, strength);
        rebuildShapeSvgById(id);
        sendMessage({ type: 'mapChanged' });
    };
    window.moveShapeZ = (id, dir) => {
        const owner = findShapeOwner(id);
        if (!owner || !owner.shapes) return;
        const i = owner.shapes.findIndex(s => s.id === id);
        if (i < 0) return;
        const j = i + (dir > 0 ? 1 : -1);
        if (j < 0 || j >= owner.shapes.length) return;
        const tmp = owner.shapes[i];
        owner.shapes[i] = owner.shapes[j];
        owner.shapes[j] = tmp;
        render();
        sendMessage({ type: 'mapChanged' });
    };
    window.deleteShape = (id) => {
        const owner = findShapeOwner(id);
        if (!owner) return;
        owner.shapes = (owner.shapes || []).filter(s => s.id !== id);
        if (mapState.terrainEdit && mapState.terrainEdit.shapeId === id) {
            mapState.terrainEdit = null;
            renderTerrainEditor();
            sendMessage({ type: 'shapeDeselected' });
        }
        render();
        sendMessage({ type: 'mapChanged' });
        mapState.renderBottomBar();
    };
}
