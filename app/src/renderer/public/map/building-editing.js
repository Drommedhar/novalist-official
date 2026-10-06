import { mapState } from './map-state.js';
import { walkNodes, computeNodeState, findLayer } from './map-layers.js';
import { buildingShowsPlan, buildBuildingSvg, rebuildBuildingSvg } from './building-rendering.js';
import { renderLabel } from './label-editing.js';
import { renderPin } from './pin-rendering.js';
import { mulberry32, generateFootprint, attachBuildingToRoads, buildingWorldPoints, polyCentroid, rotateBuildingGeometry } from './building-geometry.js';
import { firstLeafNode } from './map-viewport-api.js';
import { render } from './map-document.js';
import { sendMessage } from './map-messages.js';
import { clearSelection } from './map-selection.js';
import { renderFloorSelectors } from './floor-plan-view.js';

export function findBuilding(id) {
    let f = null;
    walkNodes(mapState.mapData.layers, n => { if (!f) f = (n.buildings || []).find(b => b.id === id); });
    return f || null;
}

export function findBuildingOwner(id) {
    let o = null;
    walkNodes(mapState.mapData.layers, n => { if (!o && (n.buildings || []).some(b => b.id === id)) o = n; });
    return o;
}

// Labels / pins can be top-level OR scoped to a building floor. These resolve
// an id from either place so the shared editors work on both.
export function findLabelById(id) {
    let f = (mapState.mapData.labels || []).find(l => l.id === id);
    if (f) return f;
    walkNodes(mapState.mapData.layers, n => {
        for (const b of n.buildings || [])
            for (const fl of b.floors || [])
                if (!f) f = (fl.labels || []).find(l => l.id === id);
    });
    return f || null;
}

export function findPinById(id) {
    let f = (mapState.mapData.pins || []).find(p => p.id === id);
    if (f) return f;
    walkNodes(mapState.mapData.layers, n => {
        for (const b of n.buildings || [])
            for (const fl of b.floors || [])
                if (!f) f = (fl.pins || []).find(p => p.id === id);
    });
    return f || null;
}

// Re-render every plan-showing building's active-floor labels + pins into the
// shared overlays (tagged data-floor-bld so they can be cleared / refreshed).
export function renderFloorAnnotations() {
    // Keep a label that is mid-edit on screen — removing it would blur the
    // contentEditable and commit (then delete) an empty label.
    const keepId = mapState.labelEditing ? mapState.labelEditing.id : null;
    mapState.labelOverlay.querySelectorAll('.nv-label[data-floor-bld]').forEach(n => {
        if (n.dataset.labelId !== keepId) n.remove();
    });
    mapState.pinOverlay.querySelectorAll('.nv-pin-host[data-floor-bld]').forEach(n => n.remove());
    const state = computeNodeState();
    walkNodes(mapState.mapData.layers, node => {
        const st = state.get(node.id);
        if (st && !st.visible) return;
        for (const b of node.buildings || []) {
            if (!buildingShowsPlan(b)) continue;
            if (mapState.isolatedId && !(mapState.isolatedKind === 'building' && mapState.isolatedId === b.id)) continue;
            const fl = (b.floors || [])[b.activeFloor || 0];
            if (!fl) continue;
            for (const lbl of fl.labels || []) {
                if (lbl.id === keepId) continue; // already on screen, being edited
                const el = renderLabel(lbl);
                if (el) el.dataset.floorBld = b.id;
            }
            for (const pin of fl.pins || []) {
                renderPin(pin);
                const host = mapState.pinOverlay.querySelector(`.nv-pin-host[data-pin-id="${pin.id}"]`);
                if (host) host.dataset.floorBld = b.id;
            }
        }
    });
}

// Roll a fresh random footprint for the placement preview, keeping the cursor
// position. Bound to the re-roll hotkey + the type picker. Resets the manual
// rotation so each new draft starts road-aligned (or 0°).
export function rollBuildingDraft() {
    const rng = mulberry32((Math.random() * 0xffffffff) >>> 0);
    const prev = mapState.buildingDraft || {};
    let fp = generateFootprint(mapState.buildingDraftType, rng);
    if (mapState.buildingScale !== 1)
        fp = fp.map(p => ({ x: p.x * mapState.buildingScale, y: p.y * mapState.buildingScale }));
    mapState.buildingDraft = {
        type: mapState.buildingDraftType,
        footprint: fp,
        rotation: 0,
        x: prev.x || 0, y: prev.y || 0,
    };
    mapState.buildingDraftRotation = 0;
    renderBuildingDraft();
}

export function renderBuildingDraft() {
    const old = mapState.world.querySelector('.nv-building[data-building-id="__buildingDraft__"]');
    if (old) old.remove();
    if (!mapState.buildingDraft) return;
    const svg = buildBuildingSvg({ id: '__buildingDraft__' }, {
        id: '__buildingDraft__', type: mapState.buildingDraft.type,
        footprint: mapState.buildingDraft.footprint, rotation: mapState.buildingDraft.rotation,
        x: mapState.buildingDraft.x, y: mapState.buildingDraft.y, roof: { kind: 'gable' },
    });
    svg.style.opacity = '0.6';
    mapState.world.appendChild(svg);
}

// Placement: every left-click drops a building; the tool stays active and rolls
// the next preview (multi-place). Esc exits.
export function tryPlaceBuilding(e) {
    if (mapState.toolMode !== 'building' || mapState.mode !== 'edit') return false;
    if (e.button !== 0) return false;
    e.stopPropagation(); e.preventDefault();
    if (!mapState.buildingDraft) rollBuildingDraft();
    attachBuildingToRoads(mapState.buildingDraft, (e.clientX - mapState.pan.x) / mapState.zoom, (e.clientY - mapState.pan.y) / mapState.zoom, e.shiftKey);
    const layer = findLayer(mapState.activeLayerId) || firstLeafNode();
    if (layer) {
        layer.buildings = layer.buildings || [];
        layer.buildings.push({
            id: 'building-' + Date.now(),
            type: mapState.buildingDraft.type,
            // bake rotation + translation into the stored footprint (rotation 0).
            footprint: buildingWorldPoints(mapState.buildingDraft),
            rotation: 0,
            roof: { kind: mapState.buildingDraft.type === 'hall' || mapState.buildingDraft.type === 'trainStation' ? 'flat' : 'gable', pitch: 0.5 },
            floorCount: mapState.buildingDraft.type === 'playground' ? 0 : 1,
            activeFloor: 0,
            planMinZoom: 4,
            floors: [],
        });
    }
    render();
    sendMessage({ type: 'mapChanged' });
    rollBuildingDraft(); // ready the next placement
    return true;
}

export function buildingSelectionPayload(b) {
    return {
        type: 'buildingSelected', buildingId: b.id,
        buildingType: b.type || 'singleFamily',
        roofKind: (b.roof && b.roof.kind) || 'gable',
        roofPitch: (b.roof && b.roof.pitch) != null ? b.roof.pitch : 0.5,
        floorCount: b.floorCount || 0,
        planMinZoom: b.planMinZoom != null ? b.planMinZoom : 4,
    };
}

export function selectBuilding(id) {
    if (mapState.buildingEdit && mapState.buildingEdit.buildingId === id) return;
    clearSelection();
    mapState.buildingEdit = { buildingId: id };
    rebuildBuildingSvg(id);
    renderBuildingEditor();
    const b = findBuilding(id);
    if (b) sendMessage(buildingSelectionPayload(b));
    mapState.renderBottomBar();
}

export function exitBuildingEdit() {
    if (!mapState.buildingEdit) return;
    const prev = mapState.buildingEdit;
    mapState.buildingEdit = null;
    if (prev) rebuildBuildingSvg(prev.buildingId);
    renderBuildingEditor();
    sendMessage({ type: 'buildingDeselected' });
    mapState.renderBottomBar();
}

// Rotation handle for the selected building. Sits above its topmost footprint
// point. Hidden while floor-plan editing that building.
export function renderBuildingEditor() {
    Array.from(mapState.handlesOverlay.querySelectorAll('.nv-bld-rot-handle')).forEach(n => n.remove());
    if (!mapState.buildingEdit) return;
    if (mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === mapState.buildingEdit.buildingId) return;
    const b = findBuilding(mapState.buildingEdit.buildingId);
    if (!b || !b.footprint || b.footprint.length < 3) return;
    let topY = 1e9, topX = 0;
    for (const p of b.footprint) if (p.y < topY) { topY = p.y; topX = p.x; }
    const h = document.createElement('div');
    h.className = 'nv-handle nv-bld-rot-handle';
    h.style.left = (mapState.pan.x + topX * mapState.zoom) + 'px';
    h.style.top = (mapState.pan.y + topY * mapState.zoom - 26) + 'px';
    h.title = mapState.mapStrings.bldRotateTip || '';
    h.addEventListener('mousedown', e => startBuildingRotate(e, b));
    mapState.handlesOverlay.appendChild(h);
}

export function startBuildingRotate(ev, b) {
    ev.stopPropagation(); ev.preventDefault();
    const c = polyCentroid(b.footprint);
    const cx = mapState.pan.x + c.x * mapState.zoom, cy = mapState.pan.y + c.y * mapState.zoom;
    let last = Math.atan2(ev.clientY - cy, ev.clientX - cx);
    function move(e) {
        let a = Math.atan2(e.clientY - cy, e.clientX - cx);
        let delta = a - last;
        if (e.shiftKey) {
            delta = Math.round(delta / (Math.PI / 12)) * (Math.PI / 12); // 15° steps
            if (delta === 0) return;
        }
        last += delta;
        rotateBuildingGeometry(b, c, delta);
        rebuildBuildingSvg(b.id);
        renderFloorSelectors();
        renderFloorAnnotations();
        renderBuildingEditor();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

export function initializeBuildingEditing() {
    mapState.buildingDraft = null;
    mapState.buildingEdit = null;
    mapState.buildingDraftType = 'singleFamily';
    mapState.buildingScale = 1;
    mapState.buildingDraftRotation = 0;
    mapState.buildingRotatingRMB = false;
    mapState.floorPlanEdit = null;
    mapState.floorPlanTool = 'select';
    mapState.wallDraft = null;
    mapState.stairDraft = null;
    mapState.floorPlanPrevIso = null;
    window.setBuildingScale = (s) => {
        mapState.buildingScale = s > 0 ? s : 1;
        if (mapState.toolMode === 'building') rollBuildingDraft();
    };
    window.setBuildingDraftType = (type) => {
        mapState.buildingDraftType = mapState.BUILDING_TYPES[type] ? type : 'singleFamily';
        if (mapState.toolMode === 'building') rollBuildingDraft();
    };
}
