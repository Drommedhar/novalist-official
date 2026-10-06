import { mapState } from './map-state.js';
import { rebuildBuildingSvg } from './building-rendering.js';
import { sendMessage } from './map-messages.js';
import { selectBuilding, findBuilding, renderFloorAnnotations } from './building-editing.js';
import { buildingFloorOutline } from './building-geometry.js';
import { beginLabelEdit } from './label-editing.js';
import { enterFloorPlanEdit } from './floor-plan-view.js';

export function onWallMouseDown(e, b, wi, w) {
    if (e.button !== 0) return;
    if (mapState.mode !== 'edit') return;
    const editingThis = mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === b.id;
    if (editingThis && (mapState.floorPlanTool === 'door' || mapState.floorPlanTool === 'window')) {
        e.stopPropagation();
        const wx = (e.clientX - mapState.pan.x) / mapState.zoom, wy = (e.clientY - mapState.pan.y) / mapState.zoom;
        const dx = w.x2 - w.x1, dy = w.y2 - w.y1, L2 = dx * dx + dy * dy || 1;
        let t = ((wx - w.x1) * dx + (wy - w.y1) * dy) / L2;
        t = Math.max(0, Math.min(1, t));
        const fi = b.activeFloor || 0;
        b.floors = b.floors || [];
        while (b.floors.length <= fi) b.floors.push({});
        b.floors[fi].openings = b.floors[fi].openings || [];
        b.floors[fi].openings.push({ wallIndex: wi, t: t, width: 8, kind: mapState.floorPlanTool });
        rebuildBuildingSvg(b.id);
        sendMessage({ type: 'mapChanged' });
        return;
    }
    // Any other floor-plan tool (wall / stairs / label / pin / select) — a wall
    // click behaves like a floor click so tools still work near walls.
    if (editingThis) { onBuildingPlanMouseDown(e, b); return; }
    if (mapState.toolMode === 'select') { e.stopPropagation(); selectBuilding(b.id); }
}

export function deleteWall(id, fi, wi) {
    const b = findBuilding(id);
    const fl = b && (b.floors || [])[fi];
    if (!fl || !fl.walls) return;
    fl.walls.splice(wi, 1);
    fl.openings = (fl.openings || []).filter(o => o.wallIndex !== wi)
        .map(o => o.wallIndex > wi ? Object.assign({}, o, { wallIndex: o.wallIndex - 1 }) : o);
    rebuildBuildingSvg(id);
    sendMessage({ type: 'mapChanged' });
}

export function deleteOpening(id, fi, oi) {
    const b = findBuilding(id);
    const fl = b && (b.floors || [])[fi];
    if (!fl || !fl.openings) return;
    fl.openings.splice(oi, 1);
    rebuildBuildingSvg(id);
    sendMessage({ type: 'mapChanged' });
}

export function deleteStair(id, fi, si) {
    const b = findBuilding(id);
    const fl = b && (b.floors || [])[fi];
    if (!fl || !fl.stairs) return;
    fl.stairs.splice(si, 1);
    rebuildBuildingSvg(id);
    sendMessage({ type: 'mapChanged' });
}

// Commit a drawn staircase from stairDraft.start to (ex,ey).
export function commitStairDraft(b, ex, ey) {
    if (!mapState.stairDraft) return;
    const s = mapState.stairDraft.start;
    const dx = ex - s.x, dy = ey - s.y, len = Math.hypot(dx, dy);
    mapState.stairDraft = null;
    if (len < 2) { rebuildBuildingSvg(b.id); return; } // too short → cancel
    const fi = b.activeFloor || 0;
    b.floors = b.floors || [];
    while (b.floors.length <= fi) b.floors.push({});
    b.floors[fi].stairs = b.floors[fi].stairs || [];
    // Rotation: the stair rect's long axis is local +Y, so rotation = runAngle - 90.
    b.floors[fi].stairs.push({
        x: (s.x + ex) / 2, y: (s.y + ey) / 2,
        width: 10, length: len,
        rotation: Math.atan2(dy, dx) * 180 / Math.PI - 90,
        direction: 'up',
    });
    rebuildBuildingSvg(b.id);
    sendMessage({ type: 'mapChanged' });
}

export function flipOpening(id, fi, oi) {
    const b = findBuilding(id);
    const fl = b && (b.floors || [])[fi];
    if (!fl || !fl.openings || !fl.openings[oi]) return;
    fl.openings[oi].flip = !fl.openings[oi].flip;
    rebuildBuildingSvg(id);
    sendMessage({ type: 'mapChanged' });
}

// Resolve an opening's host segment: an interior wall (wallIndex >= 0) or an
// edge of the floor's outer outline (wallIndex < 0 → edge -wallIndex-1).
export function openingSegment(b, fi, op) {
    const fl = (b.floors || [])[fi] || {};
    if (op.wallIndex >= 0) {
        const w = (fl.walls || [])[op.wallIndex];
        return w ? { x1: w.x1, y1: w.y1, x2: w.x2, y2: w.y2, thickness: w.thickness || 3 } : null;
    }
    const outline = buildingFloorOutline(b, fi);
    if (outline.length < 3) return null;
    const ei = (-op.wallIndex - 1) % outline.length;
    const a = outline[ei], c = outline[(ei + 1) % outline.length];
    return (a && c) ? { x1: a.x, y1: a.y, x2: c.x, y2: c.y, thickness: 1.6 } : null;
}

export function nearestOutlineEdge(outline, wx, wy) {
    let bestI = 0, bestD = Infinity, bestT = 0.5;
    const n = outline.length;
    for (let i = 0; i < n; i++) {
        const a = outline[i], c = outline[(i + 1) % n];
        const dx = c.x - a.x, dy = c.y - a.y, L2 = dx * dx + dy * dy || 1;
        let t = ((wx - a.x) * dx + (wy - a.y) * dy) / L2;
        t = Math.max(0, Math.min(1, t));
        const d = Math.hypot(wx - (a.x + t * dx), wy - (a.y + t * dy));
        if (d < bestD) { bestD = d; bestI = i; bestT = t; }
    }
    return { edge: bestI, t: bestT };
}

export function onBuildingPlanMouseDown(e, b) {
    if (e.button !== 0) return; // middle/right bubble
    if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
    e.stopPropagation();
    // preventDefault stops the browser's default mousedown focus shift — without
    // it, focusing a freshly-placed label here is immediately overridden (the
    // poly steals focus) and the empty label blurs + auto-deletes.
    e.preventDefault();
    if (mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === b.id) {
        const wx = (e.clientX - mapState.pan.x) / mapState.zoom, wy = (e.clientY - mapState.pan.y) / mapState.zoom;
        if (mapState.floorPlanTool === 'wall') {
            if (!mapState.wallDraft) mapState.wallDraft = { points: [] };
            mapState.wallDraft.points.push({ x: wx, y: wy });
            rebuildBuildingSvg(b.id);
            return;
        }
        if (mapState.floorPlanTool === 'stairs') {
            // Two-click draw: first click = start, second = end (defines run
            // direction + length).
            if (!mapState.stairDraft) {
                mapState.stairDraft = { start: { x: wx, y: wy }, cursor: { x: wx, y: wy } };
                rebuildBuildingSvg(b.id);
                return;
            }
            commitStairDraft(b, wx, wy);
            return;
        }
        // Door / window clicked on the floor body → anchor to the nearest
        // outer-outline edge (so outer walls get openings too).
        if (mapState.floorPlanTool === 'door' || mapState.floorPlanTool === 'window') {
            const fi = b.activeFloor || 0;
            const outline = buildingFloorOutline(b, fi);
            if (outline.length >= 3) {
                const ne = nearestOutlineEdge(outline, wx, wy);
                b.floors = b.floors || [];
                while (b.floors.length <= fi) b.floors.push({});
                b.floors[fi].openings = b.floors[fi].openings || [];
                b.floors[fi].openings.push({ wallIndex: -ne.edge - 1, t: ne.t, width: 8, kind: mapState.floorPlanTool });
                rebuildBuildingSvg(b.id);
                sendMessage({ type: 'mapChanged' });
            }
            return;
        }
        // Floor-scoped label / pin — shown only while this floor is active.
        if (mapState.floorPlanTool === 'flabel') {
            const fi = b.activeFloor || 0;
            b.floors = b.floors || [];
            while (b.floors.length <= fi) b.floors.push({});
            b.floors[fi].labels = b.floors[fi].labels || [];
            const lbl = { id: 'label-' + Date.now(), x: wx, y: wy, text: '',
                          fontSize: 8, fontFamily: '', align: 'center', color: '#1c1a18' };
            b.floors[fi].labels.push(lbl);
            renderFloorAnnotations();
            // No mapChanged here — beginLabelEdit's commit sends it. A premature
            // mapChanged round-trips a render() that wipes the editable label.
            beginLabelEdit(lbl, mapState.labelOverlay.querySelector(`.nv-label[data-label-id="${lbl.id}"]`));
            return;
        }
        if (mapState.floorPlanTool === 'fpin') {
            const fi = b.activeFloor || 0;
            b.floors = b.floors || [];
            while (b.floors.length <= fi) b.floors.push({});
            b.floors[fi].pins = b.floors[fi].pins || [];
            b.floors[fi].pins.push({ id: 'pin-' + Date.now(), x: wx, y: wy, style: 'dot',
                                     label: '', entityType: '', entityId: '', color: null });
            renderFloorAnnotations();
            sendMessage({ type: 'mapChanged' });
            return;
        }
    }
    selectBuilding(b.id);
}

// Commit the in-progress wall chain onto the active floor.
export function commitWallDraft() {
    if (!mapState.floorPlanEdit || !mapState.wallDraft || mapState.wallDraft.points.length < 2) { mapState.wallDraft = null; return; }
    const b = findBuilding(mapState.floorPlanEdit.buildingId);
    if (b) {
        const fi = b.activeFloor || 0;
        b.floors = b.floors || [];
        while (b.floors.length <= fi) b.floors.push({});
        const fl = b.floors[fi];
        fl.walls = fl.walls || [];
        for (let i = 0; i < mapState.wallDraft.points.length - 1; i++) {
            const a = mapState.wallDraft.points[i], c = mapState.wallDraft.points[i + 1];
            fl.walls.push({ x1: a.x, y1: a.y, x2: c.x, y2: c.y, thickness: 3 });
        }
        mapState.wallDraft = null;
        rebuildBuildingSvg(b.id);
        sendMessage({ type: 'mapChanged' });
        return;
    }
    mapState.wallDraft = null;
}

// (showFloorPlanToolbar / updateFloorPlanToolbar / hideFloorPlanToolbar removed
//  — bottom bar driven by renderBottomBar)
export function setFloorPlanTool(tool) {
    if (mapState.floorPlanTool === 'wall' && tool !== 'wall') commitWallDraft();
    mapState.floorPlanTool = (mapState.floorPlanTool === tool) ? 'select' : tool;
    if (mapState.floorPlanTool !== 'wall') commitWallDraft();
    mapState.stairDraft = null;
    mapState.renderBottomBar();
}

export function initializeFloorPlanEditing() {
    window.editBuildingPlan = (id) => enterFloorPlanEdit(id);
}
