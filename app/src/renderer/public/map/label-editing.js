import { mapState } from './map-state.js';
import { startPan } from './map-navigation.js';
import { trySplinePoint } from './spline-draft.js';
import { tryPlacePin, tryPlaceLabel } from './map-placement.js';
import { tryPlaceTerrainPoint } from './terrain-editing.js';
import { tryPlaceBorderPoint } from './border-editing.js';
import { tryPlaceBuilding, findLabelById } from './building-editing.js';
import { clearSelection } from './map-selection.js';
import { sendMessage } from './map-messages.js';
import { showSimpleContextMenu } from './map-context-menu.js';
import { walkNodes } from './map-layers.js';
import { render } from './map-document.js';

// { id } while a label is being typed into

export function applyLabelStyle(el, lbl) {
    el.style.fontFamily = lbl.fontFamily || '';
    el.style.textAlign = lbl.align || 'center';
    el.style.color = lbl.color || '#ffffff';
}

export function renderLabel(lbl) {
    const el = document.createElement('div');
    el.className = 'nv-label' + (mapState.selected?.kind === 'label' && mapState.selected.id === lbl.id ? ' selected' : '');
    el.dataset.labelId = lbl.id;
    el.dataset.worldX = lbl.x;
    el.dataset.worldY = lbl.y;
    el.dataset.fontSize = lbl.fontSize || 18;
    el.dataset.layerId = lbl.layerId || '';
    el.textContent = lbl.text || '';
    el.style.left = (mapState.pan.x + lbl.x * mapState.zoom) + 'px';
    el.style.top  = (mapState.pan.y + lbl.y * mapState.zoom) + 'px';
    el.style.fontSize = ((lbl.fontSize || 18) * mapState.zoom) + 'px';
    applyLabelStyle(el, lbl);
    el.addEventListener('mousedown', (e) => {
        if (el.isContentEditable) { e.stopPropagation(); return; } // typing — leave caret alone
        if (e.button === 1) { e.preventDefault(); e.stopPropagation(); startPan(e); return; }
        if (trySplinePoint(e)) return;
        if (tryPlacePin(e)) return;
        if (tryPlaceLabel(e)) return;
        if (tryPlaceTerrainPoint(e)) return;
        if (tryPlaceBorderPoint(e)) return;
        if (tryPlaceBuilding(e)) return;
        e.stopPropagation();
        if (mapState.mode !== 'edit' || e.button === 2) return;
        if (mapState.toolMode !== 'select') return;
        selectLabel(lbl);
        startLabelDrag(e, lbl, el);
    });
    el.addEventListener('dblclick', (e) => {
        if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
        e.stopPropagation(); e.preventDefault();
        beginLabelEdit(lbl, el);
    });
    el.addEventListener('contextmenu', (e) => {
        e.preventDefault(); e.stopPropagation();
        if (mapState.mode !== 'edit' || el.isContentEditable) return;
        showLabelContextMenu(e, lbl);
    });
    mapState.labelOverlay.appendChild(el);
    return el;
}

export function selectLabel(lbl) {
    if (mapState.selected?.kind === 'label' && mapState.selected.id === lbl.id) return;
    clearSelection();
    mapState.selected = { kind: 'label', id: lbl.id };
    const el = mapState.labelOverlay.querySelector(`.nv-label[data-label-id="${lbl.id}"]`);
    if (el) el.classList.add('selected');
    sendMessage({ type: 'labelSelected', labelId: lbl.id,
        fontSize: lbl.fontSize || 18, fontFamily: lbl.fontFamily || '',
        align: lbl.align || 'center', color: lbl.color || '#ffffff' });
    mapState.renderBottomBar();
}

export function selectPin(pin, host) {
    if (mapState.selected?.kind === 'pin' && mapState.selected.id === pin.id) return;
    clearSelection();
    mapState.selected = { kind: 'pin', id: pin.id };
    if (host) host.classList.add('selected');
    sendMessage({ type: 'pinSelected', pinId: pin.id, color: pin.color || '' });
    mapState.renderBottomBar();
}

export function initializeLabelEditing() {
    mapState.labelEditing = null;
}

export function startLabelDrag(ev, lbl, el) {
    const start = { x: ev.clientX, y: ev.clientY, px: lbl.x, py: lbl.y };
    function move(e) {
        lbl.x = start.px + (e.clientX - start.x) / mapState.zoom;
        lbl.y = start.py + (e.clientY - start.y) / mapState.zoom;
        el.dataset.worldX = lbl.x;
        el.dataset.worldY = lbl.y;
        el.style.left = (mapState.pan.x + lbl.x * mapState.zoom) + 'px';
        el.style.top  = (mapState.pan.y + lbl.y * mapState.zoom) + 'px';
    }
    function up() {
        window.removeEventListener('mousemove', move);
        window.removeEventListener('mouseup', up);
        sendMessage({ type: 'mapChanged' });
    }
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
}

// Inline editing: the label div becomes contentEditable. Enter commits,
// Ctrl/Shift+Enter inserts a line break, Esc cancels. An empty label on
// commit is deleted.
export function beginLabelEdit(lbl, el) {
    el = el || mapState.labelOverlay.querySelector(`.nv-label[data-label-id="${lbl.id}"]`);
    if (!el || mapState.labelEditing) return;
    mapState.labelEditing = { id: lbl.id };
    const prevText = lbl.text || '';
    let cancelled = false;
    selectLabel(lbl);
    el.contentEditable = 'plaintext-only';
    el.spellcheck = false;
    el.classList.add('editing');
    el.focus();
    const range = document.createRange();
    range.selectNodeContents(el);
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
    function key(e) {
        e.stopPropagation();
        if (e.key === 'Enter' && !e.ctrlKey && !e.shiftKey) {
            e.preventDefault();
            el.blur();
        } else if (e.key === 'Escape') {
            e.preventDefault();
            cancelled = true;
            el.textContent = prevText;
            el.blur();
        }
        // Ctrl/Shift+Enter falls through → browser inserts a newline
    }
    function done() {
        el.removeEventListener('keydown', key);
        el.removeEventListener('blur', done);
        el.contentEditable = 'false';
        el.classList.remove('editing');
        const editId = mapState.labelEditing.id;
        mapState.labelEditing = null;
        const target = findLabelById(editId);
        if (!target) return;
        if (!cancelled) target.text = (el.innerText || '').replace(/\n$/, '');
        if (!target.text.trim()) { deleteLabel(editId); return; }
        el.textContent = target.text;
        if (!cancelled) sendMessage({ type: 'mapChanged' });
    }
    el.addEventListener('keydown', key);
    el.addEventListener('blur', done);
}

export function showLabelContextMenu(ev, lbl) {
    showSimpleContextMenu(ev, [
        [mapState.mapStrings.labelEditText, () => beginLabelEdit(lbl)],
        null,
        [mapState.mapStrings.ctxDelete, () => deleteLabel(lbl.id)],
    ]);
}

// Remove a label by id from the top-level list or any building floor.
export function removeLabelById(labelId) {
    const before = (mapState.mapData.labels || []).length;
    mapState.mapData.labels = (mapState.mapData.labels || []).filter(l => l.id !== labelId);
    if (mapState.mapData.labels.length !== before) return true;
    let removed = false;
    walkNodes(mapState.mapData.layers, n => {
        for (const b of n.buildings || [])
            for (const fl of b.floors || [])
                if (fl.labels) {
                    const n0 = fl.labels.length;
                    fl.labels = fl.labels.filter(l => l.id !== labelId);
                    if (fl.labels.length !== n0) removed = true;
                }
    });
    return removed;
}

export function deleteLabel(labelId) {
    removeLabelById(labelId);
    if (mapState.selected?.kind === 'label' && mapState.selected.id === labelId) {
        mapState.selected = null;
        sendMessage({ type: 'labelDeselected' });
    }
    render();
    sendMessage({ type: 'mapChanged' });
}

export function publishLabelAppearanceApi() {
    window.setLabelFontSize = (labelId, size) => {
        const lbl = findLabelById(labelId);
        if (!lbl) return;
        lbl.fontSize = Math.max(4, Math.min(400, size));
        const el = mapState.labelOverlay.querySelector(`.nv-label[data-label-id="${labelId}"]`);
        if (el) { el.dataset.fontSize = lbl.fontSize; el.style.fontSize = (lbl.fontSize * mapState.zoom) + 'px'; }
        sendMessage({ type: 'mapChanged' });
    };
    window.setLabelFontFamily = (labelId, family) => {
        const lbl = findLabelById(labelId);
        if (!lbl) return;
        lbl.fontFamily = family || '';
        const el = mapState.labelOverlay.querySelector(`.nv-label[data-label-id="${labelId}"]`);
        if (el) applyLabelStyle(el, lbl);
        sendMessage({ type: 'mapChanged' });
    };
    window.setLabelAlign = (labelId, align) => {
        const lbl = findLabelById(labelId);
        if (!lbl) return;
        lbl.align = align || 'center';
        const el = mapState.labelOverlay.querySelector(`.nv-label[data-label-id="${labelId}"]`);
        if (el) applyLabelStyle(el, lbl);
        sendMessage({ type: 'mapChanged' });
    };
    window.setLabelColor = (labelId, color) => {
        const lbl = findLabelById(labelId);
        if (!lbl) return;
        lbl.color = color || '#ffffff';
        const el = mapState.labelOverlay.querySelector(`.nv-label[data-label-id="${labelId}"]`);
        if (el) applyLabelStyle(el, lbl);
        sendMessage({ type: 'mapChanged' });
    };
    window.deleteLabel = deleteLabel;
}
