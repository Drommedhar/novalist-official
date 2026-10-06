import { mapState } from './map-state.js';
import { exitBuildingEdit } from './building-editing.js';
import { exitSplineEdit } from './spline-actions.js';
import { exitShapeEdit } from './terrain-editing.js';
import { exitBorderEdit } from './border-editing.js';
import { exitClipEdit } from './image-clipping.js';
import { sendMessage } from './map-messages.js';
import { updateImageHandles } from './image-interactions.js';
import { findLayer, findImageNode } from './map-layers.js';
import { removePinById } from './pin-api.js';
import { deleteLabel } from './label-editing.js';
import { render } from './map-document.js';

// Single source of truth for "drop whatever is selected". Every selectX() and
// enterX() calls this first so only one element is ever selected at a time and
// only one bottom-bar mode is ever rendered.
export function clearSelection() {
    // floorPlanEdit is a submode of buildingEdit — leave it alone here; it has
    // its own explicit exit gate.
    if (mapState.buildingEdit && !mapState.floorPlanEdit) exitBuildingEdit();
    if (mapState.splineEdit)  exitSplineEdit();
    if (mapState.terrainEdit) exitShapeEdit();
    if (mapState.borderEdit)  exitBorderEdit();
    if (mapState.clipEdit)    exitClipEdit(false);
    if (mapState.selected) {
        const wasLabel = mapState.selected.kind === 'label';
        const wasPin   = mapState.selected.kind === 'pin';
        mapState.selected = null;
        if (wasLabel) sendMessage({ type: 'labelDeselected' });
        if (wasPin)   sendMessage({ type: 'pinDeselected' });
    }
    Array.from(mapState.world.querySelectorAll('.selected')).forEach(n => n.classList.remove('selected'));
    Array.from(mapState.labelOverlay.querySelectorAll('.selected')).forEach(n => n.classList.remove('selected'));
    Array.from(mapState.pinOverlay.querySelectorAll('.selected')).forEach(n => n.classList.remove('selected'));
    updateImageHandles();
    mapState.renderBottomBar();
}

export function initializeMapSelection() {
    window.deleteSelected = () => {
        if (!mapState.selected) return;
        if (mapState.selected.kind === 'image') {
            const node = findLayer(mapState.selected.layerId) || findImageNode(mapState.selected.id);
            if (node) node.images = (node.images || []).filter(i => i.id !== mapState.selected.id);
        } else if (mapState.selected.kind === 'pin') {
            removePinById(mapState.selected.id);
        } else if (mapState.selected.kind === 'label') {
            deleteLabel(mapState.selected.id);
            return; // deleteLabel handles render + mapChanged
        }
        mapState.selected = null;
        render();
        sendMessage({ type: 'mapChanged' });
        mapState.renderBottomBar();
    };
}
