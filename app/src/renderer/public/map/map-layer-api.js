import { mapState } from './map-state.js';
import { findImageNode, updateLayerVisibility, elementById } from './map-layers.js';
import { sendMessage } from './map-messages.js';

export function initializeMapLayerApi() {
    window.updateImageZoomRange = (imageId, minZoom, maxZoom) => {
        const node = findImageNode(imageId);
        if (!node) return;
        const img = node.images.find(i => i.id === imageId);
        if (!img) return;
        img.minZoom = (minZoom && minZoom > 0) ? minZoom : null;
        img.maxZoom = (maxZoom && maxZoom > 0) ? maxZoom : null;
        updateLayerVisibility();
        sendMessage({ type: 'mapChanged' });
    };
    window.setIsolatedElement = (kind, id) => {
        if (id) { mapState.isolatedKind = kind || 'image'; mapState.isolatedId = id; }
        else { mapState.isolatedKind = null; mapState.isolatedId = null; }
        updateLayerVisibility();
    };
    window.setIsolatedImage = (imageId) => window.setIsolatedElement('image', imageId);
    window.setElementZoomRange = (kind, id, minZoom, maxZoom) => {
        const obj = elementById(kind, id);
        if (!obj) return;
        obj.minZoom = minZoom > 0 ? minZoom : null;
        obj.maxZoom = maxZoom > 0 ? maxZoom : null;
        updateLayerVisibility();
        sendMessage({ type: 'mapChanged' });
    };
}
