import { mapState } from './map-state.js';
import { walkNodes, findLayer } from './map-layers.js';
import { applyClipPath } from './image-geometry.js';
import { sendMessage, log } from './map-messages.js';
import { render } from './map-document.js';

export function findImageById(imageId) {
    let f = null;
    walkNodes(mapState.mapData.layers, n => { if (!f) f = (n.images || []).find(i => i.id === imageId); });
    return f || null;
}

export function initializeImageApi() {
    window.setImageClip = (imageId, polygon) => {
        let img = null;
        walkNodes(mapState.mapData.layers, n => {
            if (img) return;
            const f = (n.images || []).find(i => i.id === imageId);
            if (f) img = f;
        });
        if (!img) return;
        if (polygon && polygon.length >= 3) img.clipPolygon = polygon.map(p => ({ x: p.x, y: p.y }));
        else img.clipPolygon = null;
        const el = mapState.world.querySelector(`.nv-image[data-image-id="${imageId}"]`);
        if (el) applyClipPath(el, img);
        sendMessage({ type: 'mapChanged' });
    };
    window.moveImageToLayer = (sourceLayerId, imageId, targetLayerId) => {
        const src = findLayer(sourceLayerId);
        const dst = findLayer(targetLayerId);
        if (!src || !dst) { log('moveImageToLayer: layer not found'); return; }
        const idx = (src.images || []).findIndex(i => i.id === imageId);
        if (idx < 0) return;
        const [img] = src.images.splice(idx, 1);
        dst.images = dst.images || [];
        dst.images.push(img);
        mapState.selected = { kind: 'image', id: imageId, layerId: targetLayerId };
        render();
        sendMessage({ type: 'mapChanged' });
    };
}
