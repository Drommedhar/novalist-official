import { mapState } from './map-state.js';
import { findBuilding, renderFloorAnnotations, renderBuildingEditor } from './building-editing.js';
import { commitWallDraft } from './floor-plan-editing.js';
import { rebuildBuildingSvg, buildingShowsPlan } from './building-rendering.js';
import { sendMessage } from './map-messages.js';
import { walkNodes, updateLayerVisibility } from './map-layers.js';
import { commitSplineDraft } from './spline-draft.js';
import { commitTerrainDraft } from './terrain-editing.js';
import { commitBorderDraft } from './border-editing.js';

// ── Floor plans ─────────────────────────────────────────────────────────
export function setBuildingActiveFloor(id, floor) {
    const b = findBuilding(id);
    if (!b) return;
    if (mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === id) {
        if (mapState.wallDraft) commitWallDraft();
        mapState.stairDraft = null;
    }
    b.activeFloor = Math.max(0, Math.min((b.floorCount || 1) - 1, floor));
    rebuildBuildingSvg(id);
    renderFloorAnnotations(); // swap the visible floor's labels / pins
    renderFloorSelectors();
    sendMessage({ type: 'mapChanged' });
}

// Inline per-building floor selectors (screen-space, on top of the stage).
// Shown for every building whose floor plan is currently visible.
export function renderFloorSelectors() {
    Array.from(mapState.stage.querySelectorAll('.nv-floor-selector')).forEach(n => n.remove());
    // Reserve bottom-bar height so the selector never falls behind the permanent bar.
    const bbHeight = (mapState.bb && mapState.bb.el) ? mapState.bb.el.offsetHeight : 0;
    const viewBottom = window.innerHeight - bbHeight - 8;
    walkNodes(mapState.mapData.layers, node => {
        for (const b of node.buildings || []) {
            if ((b.floorCount || 0) < 1 || !buildingShowsPlan(b)) continue;
            const el = mapState.world.querySelector(`.nv-building[data-building-id="${b.id}"]`);
            if (el && el.style.display === 'none') continue;
            let top = (b.footprint || [])[0];
            if (!top) continue;
            for (const p of b.footprint) if (p.y < top.y) top = p;
            const sel = document.createElement('div');
            sel.className = 'nv-floor-selector';
            const sx = mapState.pan.x + top.x * mapState.zoom;
            let sy = mapState.pan.y + top.y * mapState.zoom - 16;
            if (sy > viewBottom) sy = viewBottom;
            sel.style.left = sx + 'px';
            sel.style.top = sy + 'px';
            const up = document.createElement('button'); up.textContent = '▲';
            const lbl = document.createElement('div'); lbl.className = 'lbl';
            lbl.textContent = ((b.activeFloor || 0) + 1) + '/' + b.floorCount;
            const down = document.createElement('button'); down.textContent = '▼';
            up.addEventListener('click', e => { e.stopPropagation(); setBuildingActiveFloor(b.id, (b.activeFloor || 0) + 1); });
            down.addEventListener('click', e => { e.stopPropagation(); setBuildingActiveFloor(b.id, (b.activeFloor || 0) - 1); });
            sel.addEventListener('mousedown', e => e.stopPropagation());
            sel.appendChild(up); sel.appendChild(lbl); sel.appendChild(down);
            mapState.stage.appendChild(sel);
        }
    });
}

// Rebuild buildings whose roof ↔ plan state changed at the new zoom.
export function refreshBuildingsForZoom() {
    walkNodes(mapState.mapData.layers, node => {
        for (const b of node.buildings || []) {
            const el = mapState.world.querySelector(`.nv-building[data-building-id="${b.id}"]`);
            if (!el) continue;
            const want = (buildingShowsPlan(b)) ? '1' : '0';
            if (el.dataset.planShown !== want) rebuildBuildingSvg(b.id);
        }
    });
}

export function enterFloorPlanEdit(id) {
    const b = findBuilding(id);
    if (!b) return;
    if ((b.floorCount || 0) < 1) {
        sendMessage({ type: 'toast', level: 'warn',
                      text: mapState.mapStrings.fpNoFloorsWarn || 'Building has no floors. Add a floor first.' });
        return;
    }
    // Idempotent: same building = no-op; different building = exit cleanly first.
    if (mapState.floorPlanEdit) {
        if (mapState.floorPlanEdit.buildingId === id) return;
        exitFloorPlanEdit();
    }
    // Commit any draft geometry so it doesn't vanish behind the isolation.
    if (mapState.splineDraft) commitSplineDraft();
    if (mapState.terrainDraft) commitTerrainDraft();
    if (mapState.borderDraft) commitBorderDraft();
    mapState.floorPlanEdit = { buildingId: id };
    mapState.floorPlanTool = 'select';
    mapState.wallDraft = null;
    // Isolate the building being edited — hide every other layer / element so
    // nothing gets in the way. Stash the previous isolate state to restore.
    mapState.floorPlanPrevIso = { kind: mapState.isolatedKind, id: mapState.isolatedId };
    mapState.isolatedKind = 'building';
    mapState.isolatedId = id;
    rebuildBuildingSvg(id);
    updateLayerVisibility();
    renderFloorSelectors();
    renderBuildingEditor(); // hide the rotation handle while floor-plan editing
    sendMessage({ type: 'floorPlanEditEntered', buildingId: id });
    mapState.renderBottomBar();
}

export function exitFloorPlanEdit() {
    if (!mapState.floorPlanEdit) return;
    commitWallDraft();
    const prev = mapState.floorPlanEdit;
    mapState.floorPlanEdit = null;
    mapState.floorPlanTool = 'select';
    mapState.wallDraft = null;
    mapState.stairDraft = null;
    if (mapState.floorPlanPrevIso) {
        mapState.isolatedKind = mapState.floorPlanPrevIso.kind;
        mapState.isolatedId = mapState.floorPlanPrevIso.id;
        mapState.floorPlanPrevIso = null;
    } else { mapState.isolatedKind = null; mapState.isolatedId = null; }
    if (prev) rebuildBuildingSvg(prev.buildingId);
    updateLayerVisibility();
    renderFloorSelectors();
    renderBuildingEditor(); // restore the rotation handle
    sendMessage({ type: 'floorPlanEditExited' });
    mapState.renderBottomBar();
}

export function initializeFloorPlanView() {
    window.setBuildingActiveFloor = setBuildingActiveFloor;
}
