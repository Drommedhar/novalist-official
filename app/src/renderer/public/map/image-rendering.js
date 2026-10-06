import { mapState } from './map-state.js';
import { log } from './map-messages.js';
import { applyImageTransform } from './image-geometry.js';
import { updateImageHandles, selectImage, startImageDrag } from './image-interactions.js';
import { handleMapPlacement } from './map-placement.js';
import { clearSelection } from './map-selection.js';
import { unprojectImagePoint, renderClipEditor, enterClipEdit } from './image-clipping.js';
import { closeContextMenu, deleteImage, openContextMenu } from './map-context-menu.js';

export function renderImage(layer, img) {
    const el = document.createElement('img');
    el.className = 'nv-image';
    el.draggable = false;
    // Encode each path segment so spaces / non-ASCII chars resolve correctly.
    const encoded = String(img.path || '')
        .split('/')
        .map(s => encodeURIComponent(s))
        .join('/');
    el.src = mapState.imageBaseUrl + encoded;
    log(`renderImage path="${img.path}" url="${el.src}" pos=${img.x},${img.y} size=${img.width}x${img.height}`);
    el.addEventListener('load', () => {
        log(`image loaded ok: ${img.path} (natural ${el.naturalWidth}x${el.naturalHeight})`);
        applyImageTransform(el, img);
        updateImageHandles();
    });
    el.addEventListener('error', () => log(`IMAGE LOAD FAILED: ${el.src}`));
    el.style.opacity = layer.opacity ?? 1;
    el.dataset.imageId = img.id;
    el.dataset.layerId = layer.id;
    applyImageTransform(el, img);
    el.addEventListener('mousedown', (e) => {
        // Middle button = pan, always.
        if (handleMapPlacement(e)) return;
        if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
        // Locked layer = left-click just clears selection; pan via middle button only.
        if (layer.locked) {
            if (e.button === 0) {
                e.stopPropagation();
                clearSelection();
            }
            return;
        }
        if (e.button === 2) return;
        e.stopPropagation();
        selectImage(layer.id, img.id);
        startImageDrag(e, layer, img, el);
    });
    el.addEventListener('contextmenu', (e) => {
        if (mapState.mode !== 'edit') return;
        e.preventDefault();
        e.stopPropagation();
        if (mapState.clipEdit && mapState.clipEdit.imageId === img.id) return; // right-click is point-delete during clip edit
        selectImage(layer.id, img.id);
        showImageContextMenu(e, layer, img);
    });
    el.addEventListener('dblclick', (e) => {
        if (!mapState.clipEdit || mapState.clipEdit.imageId !== img.id) return;
        e.stopPropagation();
        const [px, py] = unprojectImagePoint(e.clientX, e.clientY, img, el);
        mapState.clipEdit.points.push({ x: px, y: py });
        renderClipEditor();
    });
    mapState.world.appendChild(el);
}

export function showImageContextMenu(ev, layer, img) {
    closeContextMenu();
    const menu = document.createElement('div');
    menu.className = 'nv-ctx-menu';
    menu.style.left = ev.clientX + 'px';
    menu.style.top = ev.clientY + 'px';
    function item(label, action) {
        const it = document.createElement('div');
        it.textContent = label;
        it.addEventListener('mousedown', e => { e.stopPropagation(); });
        it.addEventListener('click', () => { closeContextMenu(); action(); });
        return it;
    }
    const labels = mapState.ctxMenuLabels;
    menu.appendChild(item(labels.clip, () => enterClipEdit(layer.id, img.id)));
    menu.appendChild(item(labels.delete, () => deleteImage(layer.id, img.id)));
    openContextMenu(menu);
}
