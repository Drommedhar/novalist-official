import { mapState } from './map-state.js';

// 'kind:id'

export function bbHoverIdentify(target) {
    if (!target || !target.closest) return null;
    let n;
    if ((n = target.closest('[data-spline-id]')) && n.dataset.splineId !== '__draft__')
        return { kind: 'spline', id: n.dataset.splineId };
    if ((n = target.closest('.nv-terrain[data-shape-id]')) && n.dataset.shapeId !== '__terrainDraft__')
        return { kind: 'shape', id: n.dataset.shapeId };
    if ((n = target.closest('.nv-building[data-building-id]')) && n.dataset.buildingId !== '__buildingDraft__')
        return { kind: 'building', id: n.dataset.buildingId };
    if ((n = target.closest('.nv-pin-host[data-pin-id]')))
        return { kind: 'pin', id: n.dataset.pinId };
    if ((n = target.closest('.nv-label[data-label-id]')))
        return { kind: 'label', id: n.dataset.labelId };
    if ((n = target.closest('.nv-image[data-image-id]')))
        return { kind: 'image', id: n.dataset.imageId };
    return null;
}

export function bbApplyHover(kind, id) {
    bbClearHoverClass();
    if (kind === 'spline') {
        document.querySelectorAll(`#spline-svg path[data-spline-id="${id}"]`)
                .forEach(n => n.classList.add('nv-hovered'));
        return;
    }
    let sel = null;
    if (kind === 'shape')    sel = `.nv-terrain[data-shape-id="${id}"]`;
    if (kind === 'building') sel = `.nv-building[data-building-id="${id}"]`;
    if (kind === 'pin')      sel = `.nv-pin-host[data-pin-id="${id}"]`;
    if (kind === 'label')    sel = `.nv-label[data-label-id="${id}"]`;
    if (kind === 'image')    sel = `.nv-image[data-image-id="${id}"]`;
    if (sel) {
        const el = document.querySelector(sel);
        if (el) el.classList.add('nv-hovered');
    }
}

export function bbClearHoverClass() {
    document.querySelectorAll('.nv-hovered').forEach(n => n.classList.remove('nv-hovered'));
}

export function bbHoverReset() {
    clearTimeout(mapState.bbHoverTimer);
    mapState.bbHoverTimer = null;
    mapState.bbHoverCurrent = null;
    bbClearHoverClass();
}

export function bbHoverTrack(e) {
    if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') {
        if (mapState.bbHoverCurrent || mapState.bbHoverTimer) bbHoverReset();
        return;
    }
    const found = bbHoverIdentify(e.target);
    const key = found ? `${found.kind}:${found.id}` : null;
    if (key === mapState.bbHoverCurrent) return;
    clearTimeout(mapState.bbHoverTimer);
    bbClearHoverClass();
    mapState.bbHoverCurrent = key;
    if (found) {
        mapState.bbHoverTimer = setTimeout(() => bbApplyHover(found.kind, found.id), mapState.BB_HOVER_DELAY_MS);
    } else {
        mapState.bbHoverTimer = null;
    }
}

export function initializeMapHover() {
    mapState.BB_HOVER_DELAY_MS = 50;
    mapState.bbHoverTimer = null;
    mapState.bbHoverCurrent = null;
    window.addEventListener('blur', bbHoverReset);
    mapState.stage.addEventListener('mouseleave', bbHoverReset);
}
