import { mapState } from './map-state.js';
import { findSpline, findSplineOwner } from './spline-geometry.js';
import { findPinById, findLabelById, findBuilding, findBuildingOwner } from './building-editing.js';
import { findShape, findShapeOwner } from './terrain-editing.js';
import { render } from './map-document.js';
import { sendMessage } from './map-messages.js';

export function walkNodes(nodes, cb, parents) {
    parents = parents || [];
    for (const n of nodes || []) {
        cb(n, parents);
        if (n.children && n.children.length) walkNodes(n.children, cb, parents.concat(n));
    }
}

export function findLayer(layerId) {
    let found = null;
    walkNodes(mapState.mapData.layers, n => { if (n.id === layerId) found = n; });
    return found;
}

export function findImageNode(imageId) {
    let found = null;
    walkNodes(mapState.mapData.layers, n => {
        if (!found && (n.images || []).some(i => i.id === imageId)) found = n;
    });
    return found;
}

// Effective per-node state: visibility + opacity, cascaded from ancestors.
// Returns Map<nodeId, { visible:bool, opacity:number, locked:bool }>.
export function computeNodeState() {
    const state = new Map();
    function recurse(nodes, parentVisible, parentOpacity, parentLocked) {
        for (const n of nodes || []) {
            let visible = parentVisible && !n.hidden;
            if (n.minZoom != null && mapState.zoom < n.minZoom) visible = false;
            if (n.maxZoom != null && mapState.zoom > n.maxZoom) visible = false;
            const opacity = parentOpacity * (n.opacity ?? 1);
            const locked = parentLocked || !!n.locked;
            state.set(n.id, { visible, opacity, locked });
            // Connected set: only the active child is visible.
            const isConnected = n.isConnectedSet && n.children && n.children.length;
            const activeChildId = isConnected
                ? (n.defaultMemberLayerId || (n.children[0] && n.children[0].id))
                : null;
            for (const c of n.children || []) {
                const childParentVisible = visible && (!isConnected || c.id === activeChildId);
                recurse([c], childParentVisible, opacity, locked);
            }
        }
    }
    recurse(mapState.mapData.layers, true, 1, false);
    return state;
}

// Resolve a map element object by kind + id (for zoom-range / isolate checks).
export function elementById(kind, id) {
    if (kind === 'spline') return findSpline(id);
    if (kind === 'pin') return findPinById(id);
    if (kind === 'label') return findLabelById(id);
    if (kind === 'shape') return findShape(id);
    if (kind === 'building') return findBuilding(id);
    return null;
}

export function updateLayerVisibility() {
    const state = computeNodeState();
    Array.from(mapState.world.querySelectorAll('.nv-image')).forEach(el => {
        const lid = el.dataset.layerId;
        const st = state.get(lid);
        let show;
        if (mapState.isolatedId) {
            // Isolate: show ONLY the isolated element, nothing else.
            show = mapState.isolatedKind === 'image' && el.dataset.imageId === mapState.isolatedId;
            el.style.display = show ? '' : 'none';
            if (show) el.style.opacity = 1;
            return;
        }
        show = st ? st.visible : true;
        if (show) {
            const node = findLayer(lid);
            const img = node && (node.images || []).find(i => i.id === el.dataset.imageId);
            if (img) {
                if (img.minZoom != null && mapState.zoom < img.minZoom) show = false;
                if (img.maxZoom != null && mapState.zoom > img.maxZoom) show = false;
            }
        }
        el.style.display = show ? '' : 'none';
        if (show && st) el.style.opacity = st.opacity;
    });
    // Spline parts, pins and labels follow their owning layer's visibility +
    // opacity cascade plus their own per-element zoom range. An element with no
    // layerId (legacy / unassigned) ignores the layer cascade. While anything
    // is isolated, only the isolated element renders.
    function cascade(els, kind, idAttr) {
        for (const el of els) {
            // Floor-scoped labels / pins are owned by renderFloorAnnotations —
            // it only creates them when their building/floor should show, so the
            // layer + isolate cascade must leave them alone.
            if (el.dataset.floorBld) continue;
            const elId = el.dataset[idAttr];
            if (mapState.isolatedId) {
                const match = mapState.isolatedKind === kind && elId === mapState.isolatedId;
                el.style.display = match ? '' : 'none';
                if (match) el.style.opacity = 1;
                continue;
            }
            const lid = el.dataset.layerId;
            let show = true, op = '';
            if (lid) {
                const st = state.get(lid);
                show = st ? st.visible : true;
                op = (show && st) ? st.opacity : '';
            }
            if (show) {
                const obj = elementById(kind, elId);
                if (obj) {
                    if (obj.minZoom != null && obj.minZoom > 0 && mapState.zoom < obj.minZoom) show = false;
                    if (obj.maxZoom != null && obj.maxZoom > 0 && mapState.zoom > obj.maxZoom) show = false;
                }
            }
            el.style.display = show ? '' : 'none';
            // Only set an inline opacity when the layer is actually dimmed.
            // Setting opacity (even 1) on every per-segment spline quad makes
            // the browser composite each quad on its own → the anti-aliased
            // edges between adjacent quads stop blending and show as faint
            // seam lines. Leaving it unset lets the whole SVG raster in one pass.
            if (show) el.style.opacity = (op !== '' && op < 1) ? op : '';
        }
    }
    cascade(mapState.splineSvg.querySelectorAll('.nv-spline-part'), 'spline', 'splineId');
    cascade(mapState.pinOverlay.querySelectorAll('.nv-pin-host'), 'pin', 'pinId');
    cascade(mapState.labelOverlay.querySelectorAll('.nv-label'), 'label', 'labelId');
    cascade(mapState.world.querySelectorAll('.nv-terrain'), 'shape', 'shapeId');
    cascade(mapState.world.querySelectorAll('.nv-building'), 'building', 'buildingId');
}

export function initializeLayerIsolation() {
    mapState.isolatedKind = null;
    mapState.isolatedId = null;
}

export function publishElementLayerApi() {
    window.moveMapElementToLayer = (kind, id, targetLayerId) => {
        const target = findLayer(targetLayerId);
        if (!target) return;
        if (kind === 'pin') {
            const pin = (mapState.mapData.pins || []).find(p => p.id === id);
            if (pin) pin.layerId = targetLayerId;
        } else if (kind === 'label') {
            const lbl = (mapState.mapData.labels || []).find(l => l.id === id);
            if (lbl) lbl.layerId = targetLayerId;
        } else if (kind === 'spline') {
            const owner = findSplineOwner(id);
            if (owner && owner !== target) {
                const sp = (owner.splines || []).find(s => s.id === id);
                if (sp) {
                    owner.splines = owner.splines.filter(s => s.id !== id);
                    target.splines = target.splines || [];
                    target.splines.push(sp);
                }
            }
        } else if (kind === 'shape') {
            const owner = findShapeOwner(id);
            if (owner && owner !== target) {
                const sh = (owner.shapes || []).find(s => s.id === id);
                if (sh) {
                    owner.shapes = owner.shapes.filter(s => s.id !== id);
                    target.shapes = target.shapes || [];
                    target.shapes.push(sh);
                }
            }
        } else if (kind === 'building') {
            const owner = findBuildingOwner(id);
            if (owner && owner !== target) {
                const bld = (owner.buildings || []).find(b => b.id === id);
                if (bld) {
                    owner.buildings = owner.buildings.filter(b => b.id !== id);
                    target.buildings = target.buildings || [];
                    target.buildings.push(bld);
                }
            }
        }
        render();
        sendMessage({ type: 'mapChanged' });
    };
}
