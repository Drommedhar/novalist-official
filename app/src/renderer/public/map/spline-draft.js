import { mapState } from './map-state.js';
import { splineProfile, findSpline } from './spline-geometry.js';
import { renderSplines } from './spline-rendering.js';
import { renderSplineEditor } from './spline-editor.js';
import { findLayer } from './map-layers.js';
import { firstLeafNode } from './map-viewport-api.js';
import { render } from './map-document.js';
import { sendMessage, log } from './map-messages.js';

export function splineDefaultWidth() {
    const prof = splineProfile(mapState.splineDraftType.kind, mapState.splineDraftType.preset);
    return prof.defaultWidth || 24;
}

// Place-spline mode: each left-click drops a knot. Returns true if consumed.
export function trySplinePoint(e) {
    if (mapState.toolMode !== 'spline' || mapState.mode !== 'edit') return false;
    if (e.button !== 0) return false;
    e.stopPropagation(); e.preventDefault();
    const wx = (e.clientX - mapState.pan.x) / mapState.zoom;
    const wy = (e.clientY - mapState.pan.y) / mapState.zoom;
    if (!mapState.splineDraft) {
        mapState.splineDraft = {
            kind: mapState.splineDraftType.kind, preset: mapState.splineDraftType.preset,
            layerId: mapState.activeLayerId, points: [],
        };
    }
    mapState.splineDraft.points.push({ x: wx, y: wy, width: splineDefaultWidth() });
    renderSplines();        // live preview (renderSplines draws the draft too)
    renderSplineEditor();
    return true;
}

export function commitSplineDraft() {
    if (!mapState.splineDraft) return;
    if (mapState.splineDraft.points.length < 2) { mapState.splineDraft = null; renderSplines(); renderSplineEditor(); return; }
    // Bind to the currently-active layer (resolved at commit time, not draft start,
    // so changing the active layer mid-draw still lands the spline correctly).
    const layer = findLayer(mapState.activeLayerId) || findLayer(mapState.splineDraft.layerId) || firstLeafNode();
    if (layer) {
        layer.splines = layer.splines || [];
        layer.splines.push({
            id: 'spline-' + Date.now(),
            kind: mapState.splineDraft.kind,
            preset: mapState.splineDraft.preset,
            closed: false,
            points: mapState.splineDraft.points.map(p => ({ x: p.x, y: p.y, width: p.width })),
        });
    }
    mapState.splineDraft = null;
    render();
    sendMessage({ type: 'mapChanged' });
    renderSplineEditor();
    // Committing a spline also exits the spline tool (one spline per activation).
    mapState.toolMode = 'select';
    mapState.stage.style.cursor = 'default';
    sendMessage({ type: 'cancelSplineMode' });
}

export function cancelSplineDraft() {
    mapState.splineDraft = null;
    renderSplines();
    renderSplineEditor();
}

export function splineSelectionPayload(sp, selKnot) {
    const k = sp.points[selKnot] || {};
    const prof = splineProfile(sp.kind, sp.preset);
    const centerMk = (prof.markings || []).find(m => m.offset === 0)
                   || (prof.markings || [])[0];
    return {
        type: 'splineSelected', splineId: sp.id, kind: sp.kind, preset: sp.preset,
        closed: !!sp.closed,
        markingStyle: sp.markingStyle || '',
        // Effective part colours (override if set, else the preset's) — the
        // properties-panel colour pickers always need a concrete colour.
        casingColor: sp.casingColor || prof.casing.color,
        fillColor: sp.fillColor || prof.bands[0].color,
        markingColor: sp.markingColor || (centerMk ? centerMk.color : '#ffffff'),
        knotCount: (sp.points || []).length,
        selectedKnot: selKnot,
        knotType: k.typeOverride || '',
        knotBlend: k.blendFactor == null ? 1 : k.blendFactor,
        knotMarking: k.markingStyle || '',
        knotSharpness: k.sharpness || 0,
    };
}

export function emitSplineSelection() {
    if (!mapState.splineEdit) return;
    const sp = findSpline(mapState.splineEdit.splineId);
    if (sp) sendMessage(splineSelectionPayload(sp, mapState.splineEdit.selKnot || 0));
}

export function initializeSplineDraft() {
    mapState.splineDraft = null;
    mapState.splineEdit = null;
    mapState.splineDraftType = { kind: 'road', preset: 'residential' };
    window.setSplineDraftType = (kind, preset) => {
        mapState.splineDraftType = { kind: kind || 'road', preset: preset || 'residential' };
        log('splineDraftType = ' + mapState.splineDraftType.kind + '/' + mapState.splineDraftType.preset);
    };
}
