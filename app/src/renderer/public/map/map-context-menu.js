import { mapState } from './map-state.js';
import { findLayer } from './map-layers.js';
import { render } from './map-document.js';
import { sendMessage } from './map-messages.js';
import { removePopup, showPopup } from './popup-motion.js';

export function openContextMenu(menuEl) {
    closeContextMenu();
    document.body.appendChild(menuEl);
    showPopup(menuEl);
    mapState.activeContextMenu = menuEl;
    window.addEventListener('wheel', closeContextMenu, { passive: true });
    window.addEventListener('blur', closeContextMenu);
    window.addEventListener('keydown', onCtxKeyDown, true);
    // Defer one tick so the right-click's own mousedown doesn't close us immediately.
    setTimeout(() => {
        if (mapState.activeContextMenu !== menuEl) return;
        window.addEventListener('mousedown', onCtxMouseDown, true);
    }, 0);
}

export function onCtxMouseDown(e) {
    if (mapState.activeContextMenu && mapState.activeContextMenu.contains(e.target)) return;
    closeContextMenu();
}

export function onCtxKeyDown(e) {
    if (e.key === 'Escape') { e.stopPropagation(); closeContextMenu(); }
}

export function closeContextMenu() {
    window.removeEventListener('mousedown', onCtxMouseDown, true);
    window.removeEventListener('wheel', closeContextMenu);
    window.removeEventListener('blur', closeContextMenu);
    window.removeEventListener('keydown', onCtxKeyDown, true);
    if (mapState.activeContextMenu) {
        removePopup(mapState.activeContextMenu);
        mapState.activeContextMenu = null;
    }
    // Defensive: clean up any stray menu that bypassed openContextMenu.
    for (const stray of document.querySelectorAll('.nv-ctx-menu:not([inert])')) removePopup(stray);
}

export function deleteImage(layerId, imageId) {
    const layer = findLayer(layerId);
    if (!layer) return;
    layer.images = (layer.images || []).filter(i => i.id !== imageId);
    if (mapState.selected?.kind === 'image' && mapState.selected.id === imageId) mapState.selected = null;
    render();
    sendMessage({ type: 'mapChanged' });
}

export function initializeMapContextMenu() {
    mapState.activeContextMenu = null;
}

// Generic JS context menu. `items` is an array of [label, action] pairs;
// a null entry renders a separator.
export function showSimpleContextMenu(ev, items) {
    closeContextMenu();
    const menu = document.createElement('div');
    menu.className = 'nv-ctx-menu';
    menu.style.left = ev.clientX + 'px';
    menu.style.top = ev.clientY + 'px';
    for (const entry of items) {
        if (!entry) {
            const sep = document.createElement('div');
            sep.style.cssText = 'height:1px;background:#555;margin:4px 0;';
            menu.appendChild(sep);
            continue;
        }
        const [label, action] = entry;
        const it = document.createElement('div');
        it.textContent = label;
        it.addEventListener('click', () => { closeContextMenu(); action(); });
        menu.appendChild(it);
    }
    openContextMenu(menu);
}
