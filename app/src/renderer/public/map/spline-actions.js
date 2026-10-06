import { mapState } from './map-state.js';
import { clearSelection } from './map-selection.js';
import { renderSplineEditor } from './spline-editor.js';
import { emitSplineSelection } from './spline-draft.js';
import { sendMessage } from './map-messages.js';
import { findSpline, findSplineOwner } from './spline-geometry.js';
import { render } from './map-document.js';
import { renderSplines } from './spline-rendering.js';

export function selectSpline(splineId) {
    if (mapState.splineEdit && mapState.splineEdit.splineId === splineId) return;
    clearSelection();
    mapState.splineEdit = { splineId, selKnot: 0 };
    renderSplineEditor();
    emitSplineSelection();
    mapState.renderBottomBar();
}

export function exitSplineEdit() {
    if (!mapState.splineEdit) return;
    mapState.splineEdit = null;
    renderSplineEditor();
    sendMessage({ type: 'splineDeselected' });
    mapState.renderBottomBar();
}

export function publishSplinePropertiesApi() {
    window.setSplinePreset = (splineId, kind, preset) => {
        const sp = findSpline(splineId);
        if (!sp) return;
        sp.kind = kind || sp.kind;
        sp.preset = preset || sp.preset;
        render();
        sendMessage({ type: 'mapChanged' });
    };
    window.setSplineClosed = (splineId, closed) => {
        const sp = findSpline(splineId);
        if (!sp) return;
        if (closed) sp.closed = true; else delete sp.closed;
        renderSplines();
        if (mapState.splineEdit && mapState.splineEdit.splineId === splineId) renderSplineEditor();
        sendMessage({ type: 'mapChanged' });
    };
    window.setKnotBlend = (splineId, knotIdx, factor) => {
        const sp = findSpline(splineId);
        if (!sp || !sp.points[knotIdx]) return;
        sp.points[knotIdx].blendFactor = Math.max(0, Math.min(1, factor));
        renderSplines();
        sendMessage({ type: 'mapChanged' });
    };
    window.setKnotType = (splineId, knotIdx, preset) => {
        const sp = findSpline(splineId);
        if (!sp || !sp.points[knotIdx]) return;
        if (preset) sp.points[knotIdx].typeOverride = preset;
        else delete sp.points[knotIdx].typeOverride;
        renderSplines();
        if (mapState.splineEdit && mapState.splineEdit.splineId === splineId) {
            renderSplineEditor();
            emitSplineSelection();
        }
        sendMessage({ type: 'mapChanged' });
    };
    window.selectSplineKnot = (splineId, knotIdx) => {
        if (!mapState.splineEdit || mapState.splineEdit.splineId !== splineId) return;
        mapState.splineEdit.selKnot = knotIdx;
        renderSplineEditor();
        emitSplineSelection();
    };
    window.deleteSpline = (splineId) => {
        const owner = findSplineOwner(splineId);
        if (!owner) return;
        owner.splines = (owner.splines || []).filter(s => s.id !== splineId);
        if (mapState.splineEdit && mapState.splineEdit.splineId === splineId) {
            mapState.splineEdit = null;
            renderSplineEditor();
            sendMessage({ type: 'splineDeselected' });
        }
        render();
        sendMessage({ type: 'mapChanged' });
        mapState.renderBottomBar();
    };
    window.moveSplineZ = (splineId, dir) => {
        const owner = findSplineOwner(splineId);
        if (!owner || !owner.splines) return;
        const i = owner.splines.findIndex(s => s.id === splineId);
        if (i < 0) return;
        const j = i + (dir > 0 ? 1 : -1);
        if (j < 0 || j >= owner.splines.length) return;
        const tmp = owner.splines[i];
        owner.splines[i] = owner.splines[j];
        owner.splines[j] = tmp;
        render();
        sendMessage({ type: 'mapChanged' });
    };
}

export function publishKnotSharpnessApi() {
    window.setKnotSharpness = (splineId, knotIdx, val) => {
        const sp = findSpline(splineId);
        if (!sp || !sp.points[knotIdx]) return;
        sp.points[knotIdx].sharpness = Math.max(0, Math.min(1, val));
        renderSplines();
        renderSplineEditor();
        sendMessage({ type: 'mapChanged' });
    };
}

export function publishSplineEditToggle() {
    window.toggleSplineEditOnSelected = () => {
        if (mapState.splineEdit) { exitSplineEdit(); return; }
    };
}
