import { mapState } from './map-state.js';
import { findLayer } from './map-layers.js';
import { clearSelection } from './map-selection.js';
import { commitSplineDraft } from './spline-draft.js';
import { commitTerrainDraft } from './terrain-editing.js';
import { commitBorderDraft } from './border-editing.js';
import { applyClipPath } from './image-geometry.js';
import { sendMessage, log } from './map-messages.js';
import { updateImageHandles } from './image-interactions.js';

// ── Clip-mask editing ───────────────────────────────────────────────────
export function enterClipEdit(layerId, imageId) {
    const layer = findLayer(layerId);
    if (!layer) return;
    const img = (layer.images || []).find(i => i.id === imageId);
    if (!img) return;
    const el = mapState.world.querySelector(`.nv-image[data-image-id="${imageId}"]`);
    if (!el) return;
    const natW = el.naturalWidth || img.width || 100;
    const natH = el.naturalHeight || img.height || 100;
    let points = (img.clipPolygon || []).map(p => ({ x: p.x, y: p.y }));
    if (points.length < 3) {
        // Seed with full-image rectangle so user starts from a sensible default.
        points = [{ x: 0, y: 0 }, { x: natW, y: 0 }, { x: natW, y: natH }, { x: 0, y: natH }];
    }
    clearSelection();
    if (mapState.splineDraft) commitSplineDraft();
    if (mapState.terrainDraft) commitTerrainDraft();
    if (mapState.borderDraft) commitBorderDraft();
    mapState.clipEdit = { layerId, imageId, points };
    mapState.selected = { kind: 'image', id: imageId, layerId };
    renderClipEditor();
    mapState.renderBottomBar();
}

export function exitClipEdit(commit) {
    if (!mapState.clipEdit) return;
    const { layerId, imageId, points } = mapState.clipEdit;
    mapState.clipEdit = null;
    Array.from(mapState.handlesOverlay.querySelectorAll('.nv-clip-handle, .nv-clip-line')).forEach(n => n.remove());
    if (commit) {
        const layer = findLayer(layerId);
        const img = (layer?.images || []).find(i => i.id === imageId);
        if (img) {
            img.clipPolygon = points.length >= 3 ? points.map(p => ({ x: p.x, y: p.y })) : null;
            const el = mapState.world.querySelector(`.nv-image[data-image-id="${img.id}"]`);
            if (el) applyClipPath(el, img);
            sendMessage({ type: 'mapChanged' });
        }
    } else {
        // Re-render so any preview clip-path reverts to saved state.
        const layer = findLayer(layerId);
        const img = (layer?.images || []).find(i => i.id === imageId);
        if (img) {
            const el = mapState.world.querySelector(`.nv-image[data-image-id="${img.id}"]`);
            if (el) applyClipPath(el, img);
        }
    }
    updateImageHandles();
    mapState.renderBottomBar();
}

// Map a natural-pixel coord (within the image) to screen coords, accounting for
// the image's world position, size, rotation, and current pan/zoom.
export function projectImagePoint(natX, natY, img, el) {
    const natW = el.naturalWidth || img.width || 1;
    const natH = el.naturalHeight || img.height || 1;
    const w = img.width || natW;
    const h = img.height || natH;
    const rot = (img.rotation || 0) * Math.PI / 180;
    const cos = Math.cos(rot), sin = Math.sin(rot);
    const localX = (natX / natW) * w - w / 2;
    const localY = (natY / natH) * h - h / 2;
    const rx = localX * cos - localY * sin;
    const ry = localX * sin + localY * cos;
    const wx = img.x + w / 2 + rx;
    const wy = img.y + h / 2 + ry;
    return [mapState.pan.x + wx * mapState.zoom, mapState.pan.y + wy * mapState.zoom];
}

// Inverse of projectImagePoint: screen → natural-pixel within the image.
export function unprojectImagePoint(sx, sy, img, el) {
    const natW = el.naturalWidth || img.width || 1;
    const natH = el.naturalHeight || img.height || 1;
    const w = img.width || natW;
    const h = img.height || natH;
    const rot = (img.rotation || 0) * Math.PI / 180;
    const cos = Math.cos(rot), sin = Math.sin(rot);
    const wx = (sx - mapState.pan.x) / mapState.zoom;
    const wy = (sy - mapState.pan.y) / mapState.zoom;
    const dx = wx - (img.x + w / 2);
    const dy = wy - (img.y + h / 2);
    const ux = dx * cos + dy * sin;
    const uy = -dx * sin + dy * cos;
    const px = (ux + w / 2) / w * natW;
    const py = (uy + h / 2) / h * natH;
    return [Math.max(0, Math.min(natW, px)), Math.max(0, Math.min(natH, py))];
}

export function renderClipEditor() {
    Array.from(mapState.handlesOverlay.querySelectorAll('.nv-clip-handle, .nv-clip-line')).forEach(n => n.remove());
    if (!mapState.clipEdit) return;
    const layer = findLayer(mapState.clipEdit.layerId);
    const img = (layer?.images || []).find(i => i.id === mapState.clipEdit.imageId);
    if (!img) return;
    const el = mapState.world.querySelector(`.nv-image[data-image-id="${img.id}"]`);
    if (!el) return;

    const pts = mapState.clipEdit.points;
    // Live-preview the working polygon on the image.
    applyClipPath(el, { clipPolygon: pts });

    // Polygon edges as rotated dashed lines.
    for (let i = 0; i < pts.length; i++) {
        const next = pts[(i + 1) % pts.length];
        const [x1, y1] = projectImagePoint(pts[i].x, pts[i].y, img, el);
        const [x2, y2] = projectImagePoint(next.x, next.y, img, el);
        const line = document.createElement('div');
        line.className = 'nv-clip-line';
        const len = Math.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2);
        const ang = Math.atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
        line.style.left = x1 + 'px';
        line.style.top = y1 + 'px';
        line.style.width = len + 'px';
        line.style.transform = `rotate(${ang}deg)`;
        mapState.handlesOverlay.appendChild(line);
    }
    // Point handles, draggable + right-click to delete.
    pts.forEach((p, idx) => {
        const [sx, sy] = projectImagePoint(p.x, p.y, img, el);
        const h = document.createElement('div');
        h.className = 'nv-handle nv-clip-handle';
        h.style.left = sx + 'px';
        h.style.top = sy + 'px';
        h.dataset.idx = idx;
        h.addEventListener('mousedown', e => startClipPointDrag(e, idx, img, el));
        h.addEventListener('contextmenu', e => {
            e.preventDefault();
            e.stopPropagation();
            if (mapState.clipEdit.points.length > 3) {
                mapState.clipEdit.points.splice(idx, 1);
                renderClipEditor();
            }
        });
        mapState.handlesOverlay.appendChild(h);
    });
}

export function startClipPointDrag(ev, idx, img, el) {
    ev.stopPropagation();
    ev.preventDefault();
    function move(e) {
        const [px, py] = unprojectImagePoint(e.clientX, e.clientY, img, el);
        mapState.clipEdit.points[idx] = { x: px, y: py };
        renderClipEditor();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

export function initializeImageClipping() {
    window.toggleClipEditOnSelected = () => {
        if (mapState.clipEdit) { exitClipEdit(false); return; }
        if (!mapState.selected || mapState.selected.kind !== 'image') { log('toggleClipEdit: no image selected'); return; }
        enterClipEdit(mapState.selected.layerId, mapState.selected.id);
    };
}
