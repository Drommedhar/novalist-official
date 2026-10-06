import { mapState } from './map-state.js';
import { findSpline } from './spline-geometry.js';
import { selectSpline } from './spline-actions.js';
import { renderSplines } from './spline-rendering.js';
import { renderSplineEditor } from './spline-editor.js';
import { emitSplineSelection } from './spline-draft.js';
import { sendMessage } from './map-messages.js';
import { showSimpleContextMenu, closeContextMenu, openContextMenu } from './map-context-menu.js';
import { walkNodes } from './map-layers.js';

// Insert a knot into segment `seg` (between knots seg and seg+1) at a world point.
export function insertSplineKnot(splineId, seg, wx, wy) {
    const sp = findSpline(splineId);
    if (!sp || !sp.points) return;
    const n = sp.points.length;
    const a = sp.points[seg] || sp.points[0];
    const b = sp.points[(seg + 1) % n] || a;
    const w = ((a.width || 24) + (b.width || 24)) / 2;
    sp.points.splice(seg + 1, 0, { x: wx, y: wy, width: w });
    selectSpline(splineId);
    if (mapState.splineEdit) mapState.splineEdit.selKnot = seg + 1;
    renderSplines();
    renderSplineEditor();
    emitSplineSelection();
    sendMessage({ type: 'mapChanged' });
}

// Add a knot adjacent to knot `idx`. Mid-spline → midpoint of the neighbouring
// segment; at an end → extrapolated outward, so the spline grows.
export function addAdjacentKnot(points, idx, after) {
    const n = points.length;
    const here = points[idx];
    if (after) {
        if (idx < n - 1) {
            const nx = points[idx + 1];
            points.splice(idx + 1, 0, { x: (here.x + nx.x) / 2, y: (here.y + nx.y) / 2,
                width: ((here.width || 24) + (nx.width || 24)) / 2 });
        } else {
            const prev = points[idx - 1] || here;
            points.push({ x: here.x + (here.x - prev.x), y: here.y + (here.y - prev.y),
                width: here.width || 24 });
        }
    } else {
        if (idx > 0) {
            const pv = points[idx - 1];
            points.splice(idx, 0, { x: (here.x + pv.x) / 2, y: (here.y + pv.y) / 2,
                width: ((here.width || 24) + (pv.width || 24)) / 2 });
        } else {
            const nx = points[idx + 1] || here;
            points.unshift({ x: here.x - (nx.x - here.x), y: here.y - (nx.y - here.y),
                width: here.width || 24 });
        }
    }
}

export function showSplineContextMenu(ev, sp) {
    showSimpleContextMenu(ev, [
        [mapState.mapStrings.splineDelete, () => window.deleteSpline(sp.id)],
    ]);
}

export function startSplineKnotRotate(ev, points, idx, sx, sy, splineId) {
    ev.stopPropagation(); ev.preventDefault();
    function move(e) {
        let a = Math.atan2(e.clientY - sy, e.clientX - sx);
        if (e.shiftKey) a = Math.round(a / (Math.PI / 12)) * (Math.PI / 12); // 15° snap
        points[idx].angle = a;
        renderSplines();
        renderSplineEditor();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        if (splineId) sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

// Right-click a knot: delete it, or set / clear a per-knot type override so the
// spline can blend into a different road/river type along its length.
export function showKnotContextMenu(ev, points, idx, splineId, splineKind) {
    closeContextMenu();
    const menu = document.createElement('div');
    menu.className = 'nv-ctx-menu';
    menu.style.left = ev.clientX + 'px';
    menu.style.top = ev.clientY + 'px';
    function commit() {
        if (splineId) { renderSplines(); sendMessage({ type: 'mapChanged' }); }
        else renderSplines();
        renderSplineEditor();
    }
    function item(label, action, disabled) {
        const it = document.createElement('div');
        it.textContent = label;
        if (disabled) it.style.opacity = '0.4';
        it.addEventListener('mousedown', e => e.stopPropagation());
        it.addEventListener('click', () => {
            closeContextMenu();
            if (!disabled) action();
        });
        return it;
    }
    menu.appendChild(item(mapState.mapStrings.knotAddBefore, () => {
        addAdjacentKnot(points, idx, false); commit();
    }));
    menu.appendChild(item(mapState.mapStrings.knotAddAfter, () => {
        addAdjacentKnot(points, idx, true); commit();
    }));
    menu.appendChild(item(mapState.mapStrings.knotDelete, () => {
        if (points.length > 2) { points.splice(idx, 1); commit(); }
    }, points.length <= 2));
    const sep = document.createElement('div');
    sep.style.cssText = 'height:1px;background:#555;margin:4px 0;';
    menu.appendChild(sep);
    menu.appendChild(item(mapState.mapStrings.knotClearDirection, () => {
        delete points[idx].angle;
        commit();
    }, points[idx].angle == null));
    const sep2 = document.createElement('div');
    sep2.style.cssText = 'height:1px;background:#555;margin:4px 0;';
    menu.appendChild(sep2);
    const presets = mapState.SPLINE_PROFILES[splineKind] || mapState.SPLINE_PROFILES.road;
    for (const key of Object.keys(presets)) {
        const active = points[idx].typeOverride === key;
        menu.appendChild(item((active ? '● ' : '   ') + mapState.mapStrings.knotTypePrefix + key, () => {
            points[idx].typeOverride = key;
            commit();
        }));
    }
    menu.appendChild(item(mapState.mapStrings.knotClearOverride, () => {
        delete points[idx].typeOverride;
        commit();
    }, !points[idx].typeOverride));
    openContextMenu(menu);
}

export function startSplinePointDrag(ev, points, idx, splineId) {
    ev.stopPropagation(); ev.preventDefault();
    // Junction snapping: when dragging an end knot of a committed open spline,
    // snap onto the nearest knot of any *other* spline (end or interior — so
    // T-junctions work, not just end-to-end). Hold Shift to drag freely.
    const sp = splineId ? findSpline(splineId) : null;
    const canSnap = !!sp && !sp.closed && (idx === 0 || idx === points.length - 1);
    const snapTargets = [];
    if (canSnap) {
        walkNodes(mapState.mapData.layers, node => {
            for (const s of node.splines || []) {
                if (s.id === splineId || !s.points) continue;
                for (const pt of s.points) snapTargets.push(pt);
            }
        });
    }
    // Visualise the candidate snap points for the duration of the drag.
    // renderSplineEditor() only clears its own handle classes, so these dots
    // survive the per-frame re-render and are torn down in up().
    const snapDots = [];
    for (const tgt of snapTargets) {
        const dot = document.createElement('div');
        dot.className = 'nv-snap-point';
        dot.style.cssText =
            'position:absolute;width:10px;height:10px;margin:-5px 0 0 -5px;' +
            'border-radius:50%;background:rgba(80,170,255,0.35);' +
            'border:1.5px solid #4aa3ff;pointer-events:none;z-index:9;';
        dot.style.left = (mapState.pan.x + tgt.x * mapState.zoom) + 'px';
        dot.style.top = (mapState.pan.y + tgt.y * mapState.zoom) + 'px';
        mapState.handlesOverlay.appendChild(dot);
        snapDots.push({ tgt, dot });
    }
    function move(e) {
        let wx = (e.clientX - mapState.pan.x) / mapState.zoom;
        let wy = (e.clientY - mapState.pan.y) / mapState.zoom;
        let best = null;
        if (canSnap && !e.shiftKey && snapTargets.length) {
            const thresh = 14 / mapState.zoom;       // screen-constant catch radius
            let bestD = thresh;
            for (const tgt of snapTargets) {
                const d = Math.hypot(tgt.x - wx, tgt.y - wy);
                if (d < bestD) { bestD = d; best = tgt; }
            }
            if (best) { wx = best.x; wy = best.y; }
        }
        // Highlight the dot we'd snap to.
        for (const { tgt, dot } of snapDots) {
            const on = tgt === best;
            dot.style.background = on ? '#4aa3ff' : 'rgba(80,170,255,0.35)';
            dot.style.transform = on ? 'scale(1.5)' : 'scale(1)';
        }
        points[idx].x = wx;
        points[idx].y = wy;
        renderSplines();
        renderSplineEditor();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        for (const { dot } of snapDots) dot.remove();
        if (splineId) sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

export function startSplineWidthDrag(ev, points, idx, sx, sy, splineId) {
    ev.stopPropagation(); ev.preventDefault();
    function move(e) {
        // Distance from the knot in screen space → world-unit full width.
        const dist = Math.hypot(e.clientX - sx, e.clientY - sy);
        points[idx].width = Math.max(2, (dist * 2) / mapState.zoom);
        renderSplines();
        renderSplineEditor();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        if (splineId) sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}
