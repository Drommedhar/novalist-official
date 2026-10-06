import { mapState } from './map-state.js';
import { findSpline, knotTangents } from './spline-geometry.js';
import { emitSplineSelection } from './spline-draft.js';
import { startSplinePointDrag, showKnotContextMenu, startSplineWidthDrag, startSplineKnotRotate } from './spline-knot-editing.js';

// Draws point + width handles for the active draft or the spline being edited.
export function renderSplineEditor() {
    Array.from(mapState.handlesOverlay.querySelectorAll('.nv-spline-handle, .nv-spline-width-handle, .nv-spline-rot-handle, .nv-spline-line')).forEach(n => n.remove());
    let points = null, splineId = null, splineKind = 'road', isClosed = false;
    if (mapState.splineDraft) { points = mapState.splineDraft.points; splineKind = mapState.splineDraft.kind; }
    else if (mapState.splineEdit) {
        const sp = findSpline(mapState.splineEdit.splineId);
        if (sp) { points = sp.points; splineId = sp.id; splineKind = sp.kind; isClosed = !!sp.closed; }
    }
    if (!points) return;

    // Knot-to-knot guide line. Closed splines also draw the wrap segment.
    const guideSegs = (isClosed && points.length > 2) ? points.length : points.length - 1;
    for (let i = 0; i < guideSegs; i++) {
        const a = points[i], b = points[(i + 1) % points.length];
        const x1 = mapState.pan.x + a.x * mapState.zoom, y1 = mapState.pan.y + a.y * mapState.zoom;
        const x2 = mapState.pan.x + b.x * mapState.zoom, y2 = mapState.pan.y + b.y * mapState.zoom;
        const line = document.createElement('div');
        line.className = 'nv-spline-line';
        const len = Math.hypot(x2 - x1, y2 - y1);
        const ang = Math.atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
        line.style.left = x1 + 'px';
        line.style.top = y1 + 'px';
        line.style.width = len + 'px';
        line.style.transform = `rotate(${ang}deg)`;
        mapState.handlesOverlay.appendChild(line);
    }
    const selKnot = mapState.splineEdit ? (mapState.splineEdit.selKnot || 0) : -1;
    points.forEach((p, idx) => {
        const sx = mapState.pan.x + p.x * mapState.zoom, sy = mapState.pan.y + p.y * mapState.zoom;
        // Move handle.
        const h = document.createElement('div');
        h.className = 'nv-handle nv-spline-handle' + (idx === selKnot ? ' nv-spline-handle-sel' : '');
        h.style.left = sx + 'px';
        h.style.top = sy + 'px';
        h.dataset.idx = idx;
        h.addEventListener('mousedown', e => {
            // Clicking a knot on a committed spline focuses it for the properties panel.
            if (splineId && mapState.splineEdit && mapState.splineEdit.selKnot !== idx) {
                mapState.splineEdit.selKnot = idx;
                emitSplineSelection();
            }
            startSplinePointDrag(e, points, idx, splineId);
        });
        h.addEventListener('contextmenu', e => {
            e.preventDefault(); e.stopPropagation();
            showKnotContextMenu(e, points, idx, splineId, splineKind);
        });
        mapState.handlesOverlay.appendChild(h);
        // Width handle: offset perpendicular by half-width.
        const wh = document.createElement('div');
        wh.className = 'nv-handle nv-spline-width-handle';
        wh.style.left = sx + 'px';
        wh.style.top = (sy - (p.width / 2) * mapState.zoom) + 'px';
        wh.dataset.idx = idx;
        wh.title = mapState.mapStrings.widthHandleTip;
        wh.addEventListener('mousedown', e => startSplineWidthDrag(e, points, idx, sx, sy, splineId));
        mapState.handlesOverlay.appendChild(wh);
        // Rotation handle: only on the focused knot. Sits along the knot's
        // current tangent direction; drag to override the spline's direction
        // through this knot (works for endpoints too).
        if (idx === selKnot) {
            const tans = knotTangents(points, isClosed);
            let dx = tans[idx].x, dy = tans[idx].y;
            if (Math.hypot(dx, dy) < 1e-3) {
                const nb = points[(idx + 1) % points.length] || p;
                dx = nb.x - p.x; dy = nb.y - p.y;
            }
            const dl = Math.hypot(dx, dy) || 1;
            const rh = document.createElement('div');
            rh.className = 'nv-handle nv-spline-rot-handle';
            rh.style.left = (sx + (dx / dl) * 30) + 'px';
            rh.style.top = (sy + (dy / dl) * 30) + 'px';
            rh.title = mapState.mapStrings.rotHandleTip;
            rh.addEventListener('mousedown', e => startSplineKnotRotate(e, points, idx, sx, sy, splineId));
            mapState.handlesOverlay.appendChild(rh);
        }
    });
}
