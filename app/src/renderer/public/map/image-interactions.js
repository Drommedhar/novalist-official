import { mapState } from './map-state.js';
import { clearSelection } from './map-selection.js';
import { findLayer } from './map-layers.js';
import { sendMessage } from './map-messages.js';
import { applyImageTransform } from './image-geometry.js';

export function selectImage(layerId, imageId) {
    if (mapState.selected?.kind === 'image' && mapState.selected.id === imageId && mapState.selected.layerId === layerId) return;
    clearSelection();
    mapState.selected = { kind: 'image', id: imageId, layerId: layerId };
    Array.from(mapState.world.querySelectorAll('.nv-image')).forEach(el => {
        el.classList.toggle('selected', el.dataset.imageId === imageId && el.dataset.layerId === layerId);
    });
    updateImageHandles();
    const layer = findLayer(layerId);
    const img = layer && (layer.images || []).find(i => i.id === imageId);
    if (img) {
        sendMessage({
            type: 'imageSelected',
            imageId: imageId,
            layerId: layerId,
            minZoom: img.minZoom == null ? 0 : img.minZoom,
            maxZoom: img.maxZoom == null ? 0 : img.maxZoom,
        });
    }
    mapState.renderBottomBar();
}

export function updateImageHandles() {
    mapState.handlesOverlay.innerHTML = '';
    if (mapState.mode !== 'edit' || !mapState.selected || mapState.selected.kind !== 'image') return;
    if (mapState.toolMode !== 'select') return; // pin-place / spline tools suppress image handles
    const layer = findLayer(mapState.selected.layerId);
    if (!layer || layer.locked || layer.hidden) return;
    const img = (layer.images || []).find(i => i.id === mapState.selected.id);
    if (!img) return;

    const sx = mapState.pan.x + img.x * mapState.zoom;
    const sy = mapState.pan.y + img.y * mapState.zoom;
    const sw = (img.width || 0) * mapState.zoom;
    const sh = (img.height || 0) * mapState.zoom;
    const rot = img.rotation || 0;

    // Frame (dashed outline)
    const frame = document.createElement('div');
    frame.className = 'nv-handle-frame';
    frame.style.left = sx + 'px';
    frame.style.top = sy + 'px';
    frame.style.width = sw + 'px';
    frame.style.height = sh + 'px';
    if (rot) frame.style.transform = `rotate(${rot}deg)`;
    mapState.handlesOverlay.appendChild(frame);

    // Handle positions in local (unrotated) image space, then rotate around image center.
    const cx = sx + sw / 2, cy = sy + sh / 2;
    const rad = rot * Math.PI / 180;
    const cos = Math.cos(rad), sin = Math.sin(rad);
    const project = (lx, ly) => {
        const dx = lx - cx, dy = ly - cy;
        return [cx + dx * cos - dy * sin, cy + dx * sin + dy * cos];
    };
    const corners = [
        ['nw', sx,           sy],
        ['ne', sx + sw,      sy],
        ['se', sx + sw,      sy + sh],
        ['sw', sx,           sy + sh],
        ['n',  sx + sw / 2,  sy],
        ['e',  sx + sw,      sy + sh / 2],
        ['s',  sx + sw / 2,  sy + sh],
        ['w',  sx,           sy + sh / 2],
    ];
    for (const [dir, hx, hy] of corners) {
        const [px, py] = project(hx, hy);
        const h = document.createElement('div');
        h.className = 'nv-handle ' + dir;
        h.style.left = px + 'px';
        h.style.top = py + 'px';
        h.addEventListener('mousedown', e => startImageResize(e, layer, img, dir));
        mapState.handlesOverlay.appendChild(h);
    }
    // Rotation handle: 28px above top-center in local space, then rotated.
    const [rx, ry] = project(sx + sw / 2, sy - 28);
    const rh = document.createElement('div');
    rh.className = 'nv-handle rotate';
    rh.style.left = rx + 'px';
    rh.style.top = ry + 'px';
    rh.addEventListener('mousedown', e => startImageRotate(e, layer, img));
    mapState.handlesOverlay.appendChild(rh);
}

export function startImageResize(ev, layer, img, dir) {
    ev.stopPropagation();
    ev.preventDefault();
    const start = { mx: ev.clientX, my: ev.clientY, x: img.x, y: img.y, w: img.width, h: img.height };
    const ratio = start.w / Math.max(1, start.h);
    function move(e) {
        const dx = (e.clientX - start.mx) / mapState.zoom;
        const dy = (e.clientY - start.my) / mapState.zoom;
        let nx = start.x, ny = start.y, nw = start.w, nh = start.h;
        if (dir.includes('e')) nw = Math.max(8, start.w + dx);
        if (dir.includes('w')) { nw = Math.max(8, start.w - dx); nx = start.x + (start.w - nw); }
        if (dir.includes('s')) nh = Math.max(8, start.h + dy);
        if (dir.includes('n')) { nh = Math.max(8, start.h - dy); ny = start.y + (start.h - nh); }
        if (e.shiftKey && nw > 0 && nh > 0) {
            if (Math.abs(dx) > Math.abs(dy)) nh = nw / ratio;
            else nw = nh * ratio;
            if (dir.includes('w')) nx = start.x + (start.w - nw);
            if (dir.includes('n')) ny = start.y + (start.h - nh);
        }
        img.x = nx; img.y = ny; img.width = nw; img.height = nh;
        const el = mapState.world.querySelector(`.nv-image[data-image-id="${img.id}"]`);
        if (el) applyImageTransform(el, img);
        updateImageHandles();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

export function startImageRotate(ev, layer, img) {
    ev.stopPropagation(); ev.preventDefault();
    const cx = img.x + (img.width || 0) / 2;
    const cy = img.y + (img.height || 0) / 2;
    const scx = mapState.pan.x + cx * mapState.zoom;
    const scy = mapState.pan.y + cy * mapState.zoom;
    const startA = Math.atan2(ev.clientY - scy, ev.clientX - scx);
    const startRot = img.rotation || 0;
    function move(e) {
        const a = Math.atan2(e.clientY - scy, e.clientX - scx);
        let deg = startRot + (a - startA) * 180 / Math.PI;
        if (e.shiftKey) deg = Math.round(deg / 15) * 15;
        img.rotation = deg;
        const el = mapState.world.querySelector(`.nv-image[data-image-id="${img.id}"]`);
        if (el) applyImageTransform(el, img);
        updateImageHandles();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

export function startImageDrag(ev, layer, img, el) {
    const start = { x: ev.clientX, y: ev.clientY, ix: img.x, iy: img.y };
    function move(e) {
        const dx = (e.clientX - start.x) / mapState.zoom;
        const dy = (e.clientY - start.y) / mapState.zoom;
        img.x = start.ix + dx;
        img.y = start.iy + dy;
        applyImageTransform(el, img);
        updateImageHandles();
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}
