import { mapState } from './map-state.js';
import { selectSpline } from './spline-actions.js';
import { findSpline } from './spline-geometry.js';
import { distToSegment } from './terrain-editing.js';
import { insertSplineKnot, showSplineContextMenu } from './spline-knot-editing.js';

export function initializeMapRuntime() {
    mapState.stage = document.getElementById('stage');
    mapState.world = document.getElementById('world');
    mapState.pinOverlay = document.getElementById('pin-overlay');
    mapState.labelOverlay = document.getElementById('label-overlay');
    mapState.borderOutline = document.getElementById('border-outline');
    mapState.handlesOverlay = document.getElementById('image-handles');
    mapState.hud = document.getElementById('hud');
    mapState.scalebar = document.getElementById('scalebar');
    mapState.mapGrid = document.getElementById('map-grid');
    mapState.mapData = { groups: [], pins: [] };
    mapState.mode = 'edit';
    mapState.pan = { x: 0, y: 0 };
    mapState.zoom = 1;
    mapState.selected = null;
    mapState.imageBaseUrl = '';
    mapState.activeLayerId = '';
    mapState.clipEdit = null;
    mapState.toolMode = 'select';
    mapState.SVG_NS = 'http://www.w3.org/2000/svg';
    mapState.splineSvg = document.createElementNS(mapState.SVG_NS, 'svg');
    mapState.splineSvg.setAttribute('id', 'spline-svg');
    mapState.splineSvg.style.cssText = 'position:absolute;left:0;top:0;overflow:visible;pointer-events:none;';
    mapState.splineSvg.addEventListener('mousedown', e => {
        const id = e.target && e.target.dataset && e.target.dataset.splineId;
        if (!id || id === '__draft__' || e.button !== 0) return;
        if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
        e.stopPropagation();
        selectSpline(id);
    });
    mapState.splineSvg.addEventListener('dblclick', e => {
        const id = e.target && e.target.dataset && e.target.dataset.splineId;
        if (!id || id === '__draft__') return;
        if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
        const sp = findSpline(id);
        if (!sp || !sp.points || sp.points.length < 2) return;
        e.stopPropagation(); e.preventDefault();
        const wx = (e.clientX - mapState.pan.x) / mapState.zoom, wy = (e.clientY - mapState.pan.y) / mapState.zoom;
        const N = sp.points.length, segCount = sp.closed ? N : N - 1;
        let bestSeg = 0, bestD = Infinity;
        for (let i = 0; i < segCount; i++) {
            const a = sp.points[i], b = sp.points[(i + 1) % N];
            const d = distToSegment(wx, wy, a.x, a.y, b.x, b.y);
            if (d < bestD) { bestD = d; bestSeg = i; }
        }
        insertSplineKnot(id, bestSeg, wx, wy);
    });
    mapState.splineSvg.addEventListener('contextmenu', e => {
        const id = e.target && e.target.dataset && e.target.dataset.splineId;
        if (!id || id === '__draft__' || mapState.mode !== 'edit') return;
        e.preventDefault(); e.stopPropagation();
        const sp = findSpline(id);
        if (sp) showSplineContextMenu(e, sp);
    });
}
