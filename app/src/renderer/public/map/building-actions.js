import { mapState } from './map-state.js';
import { showSimpleContextMenu } from './map-context-menu.js';
import { findBuilding, buildingSelectionPayload, findBuildingOwner, renderBuildingEditor } from './building-editing.js';
import { rebuildBuildingSvg } from './building-rendering.js';
import { sendMessage } from './map-messages.js';
import { exitFloorPlanEdit, renderFloorSelectors } from './floor-plan-view.js';
import { render } from './map-document.js';

export function showBuildingContextMenu(ev, b) {
    showSimpleContextMenu(ev, [
        [mapState.mapStrings.buildingDelete, () => window.deleteBuilding(b.id)],
    ]);
}

export function publishBuildingPropertiesApi() {
    window.setBuildingType = (id, type) => {
        const b = findBuilding(id);
        if (!b || !mapState.BUILDING_TYPES[type]) return;
        b.type = type;
        rebuildBuildingSvg(id);
        if (mapState.buildingEdit && mapState.buildingEdit.buildingId === id) sendMessage(buildingSelectionPayload(b));
        sendMessage({ type: 'mapChanged' });
    };
    window.setBuildingRoof = (id, kind, pitch) => {
        const b = findBuilding(id);
        if (!b) return;
        b.roof = b.roof || {};
        b.roof.kind = kind || 'gable';
        b.roof.pitch = pitch > 0 ? pitch : 0;
        rebuildBuildingSvg(id);
        sendMessage({ type: 'mapChanged' });
    };
    window.setBuildingFloors = (id, count) => {
        const b = findBuilding(id);
        if (!b) return;
        b.floorCount = Math.max(0, Math.round(count));
        b.floors = b.floors || [];
        while (b.floors.length < b.floorCount) b.floors.push({});
        if (b.floors.length > b.floorCount) b.floors.length = b.floorCount;
        if (b.activeFloor >= b.floorCount) b.activeFloor = Math.max(0, b.floorCount - 1);
        if (mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === id && b.floorCount < 1) exitFloorPlanEdit();
        rebuildBuildingSvg(id);
        renderFloorSelectors();
        sendMessage({ type: 'mapChanged' });
    };
    window.setBuildingPlanZoom = (id, z) => {
        const b = findBuilding(id);
        if (!b) return;
        b.planMinZoom = z > 0 ? z : 0;
        rebuildBuildingSvg(id);
        renderFloorSelectors();
        sendMessage({ type: 'mapChanged' });
    };
    window.moveBuildingZ = (id, dir) => {
        const owner = findBuildingOwner(id);
        if (!owner || !owner.buildings) return;
        const i = owner.buildings.findIndex(b => b.id === id);
        if (i < 0) return;
        const j = i + (dir > 0 ? 1 : -1);
        if (j < 0 || j >= owner.buildings.length) return;
        const tmp = owner.buildings[i];
        owner.buildings[i] = owner.buildings[j];
        owner.buildings[j] = tmp;
        render();
        sendMessage({ type: 'mapChanged' });
    };
}

export function publishBuildingDeletionApi() {
    window.deleteBuilding = (id) => {
        const owner = findBuildingOwner(id);
        if (!owner) return;
        owner.buildings = (owner.buildings || []).filter(b => b.id !== id);
        if (mapState.buildingEdit && mapState.buildingEdit.buildingId === id) {
            mapState.buildingEdit = null;
            sendMessage({ type: 'buildingDeselected' });
        }
        if (mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === id) {
            mapState.floorPlanEdit = null; mapState.wallDraft = null; mapState.stairDraft = null;
            if (mapState.floorPlanPrevIso) {
                mapState.isolatedKind = mapState.floorPlanPrevIso.kind;
                mapState.isolatedId = mapState.floorPlanPrevIso.id;
                mapState.floorPlanPrevIso = null;
            } else { mapState.isolatedKind = null; mapState.isolatedId = null; }
            sendMessage({ type: 'floorPlanEditExited' });
        }
        render();
        renderFloorSelectors();
        renderBuildingEditor();
        sendMessage({ type: 'mapChanged' });
        mapState.renderBottomBar();
    };
}
