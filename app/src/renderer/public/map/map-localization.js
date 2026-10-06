import { mapState } from './map-state.js';

export function initializeMapLocalization() {
    mapState.ctxMenuLabels = { move: 'Move to layer…', clip: 'Edit clip mask', delete: 'Delete' };
    window.setContextMenuLabels = (m, c, d) => { mapState.ctxMenuLabels = { move: m || mapState.ctxMenuLabels.move, clip: c || mapState.ctxMenuLabels.clip, delete: d || mapState.ctxMenuLabels.delete }; };
    mapState.mapStrings = {
        knotDelete: 'Delete knot', knotClearOverride: 'Clear type override',
        knotTypePrefix: 'Type: ', labelEditText: 'Edit text', ctxDelete: 'Delete',
        clipHint: 'Drag points · Double-click image to add · Right-click point to remove · Esc to cancel',
        clipDone: 'Done', clipClear: 'Clear', clipCancel: 'Cancel',
        splineEditHint: 'Drag knots to move · drag outer dot for width · right-click knot to remove · Esc / Enter to finish',
        splineEditDone: 'Done',
        widthHandleTip: 'Drag to resize width here',
        ctxEdit: 'Edit…', moveToLayer: 'Move to layer…',
        knotAddBefore: 'Add knot before', knotAddAfter: 'Add knot after',
        knotClearDirection: 'Clear direction override', splineDelete: 'Delete spline',
        rotHandleTip: 'Drag to set the spline direction through this knot',
        shapeDelete: 'Delete shape',
        terrainEditHint: 'Drag vertices to move · double-click the shape to add a vertex · right-click a vertex to remove · Esc / Done to finish',
        buildingDelete: 'Delete building',
        bldRotateTip: 'Drag to rotate the building (Shift = 15° steps)',
        fpWall: 'Wall', fpDoor: 'Door', fpWindow: 'Window', fpStairs: 'Stairs',
        fpLabel: 'Label', fpPin: 'Pin',
        fpFlipSide: 'Flip swing side',
        fpHint: 'Wall: click points to chain walls (Enter/Esc ends). Door/Window: click a wall or the outline. Stairs: click start then end. Right-click an element to edit or remove it.',
        fpNoFloorsWarn: 'Building has no floors. Add a floor first.',
        cancel: 'Cancel',
        borderEditHint: 'Drag vertices to move · right-click vertex to remove · double-click edge to insert.',
        bbIdle: 'Map editor',
        bbIdleHint: 'Click an element to edit. Use the ribbon to add new ones.',
        bbClipLabel: 'Edit clip mask',
        bbFloorPlanLabel: 'Floor plan',
        bbSplineLabel: 'Spline', bbTerrainLabel: 'Terrain shape',
        bbBorderLabel: 'Border', bbBuildingLabel: 'Building',
        bbPinLabel: 'Pin', bbLabelSelLabel: 'Text label', bbImageLabel: 'Image',
        bbSplineDraft: 'Drawing spline', bbTerrainDraft: 'Drawing terrain', bbBorderDraft: 'Drawing border',
        bbBuildingPlace: 'Place buildings',
        bbBuildingHint: 'Click to place. Shift = free angle. Right-drag + wheel = rotate.',
        bbBuildingEditHint: 'Drag handle to rotate. Use side panel for properties.',
        bbEditFloorPlan: 'Edit floor plan',
        bbSelectedHint: 'Drag to move. Right-click for options. Delete to remove.',
        bbClickToAddPoints: 'Click to add points.',
        kbdEscCancelEnterCommit: 'Esc cancel · Enter commit',
        kbdBuildingPlace: 'R re-roll · Esc exit',
        kbdFinishDelete: 'Esc / Enter finish · Delete remove',
        kbdFinish: 'Esc / Enter finish',
        kbdEscDeselect: 'Esc deselect',
        kbdDeleteRemoveEscDeselect: 'Delete remove · Esc deselect',
        hudMap: 'Map', hudZoom: 'zoom', hudLayer: 'Layer',
        hudSplineDraft: 'Spline draft', hudTerrainDraft: 'Terrain draft',
        hudBorderDraft: 'Border draft', hudWallDraft: 'Wall draft',
        hudKnots: 'knots', hudVerts: 'verts', hudPts: 'pts',
    };
    window.setMapStrings = (json) => {
        try {
            const obj = JSON.parse(json) || {};
            Object.assign(mapState.mapStrings, obj);
            // Optional embedded JSON arrays for bottom-bar enum selects.
            if (typeof obj.buildingTypesJson === 'string') { try { mapState.BB_BUILDING_TYPES = JSON.parse(obj.buildingTypesJson) || mapState.BB_BUILDING_TYPES; } catch (error) { console.warn("map: operation failed", error instanceof Error ? error.name : typeof error); } }
            if (typeof obj.roofKindsJson    === 'string') { try { mapState.BB_ROOF_KINDS     = JSON.parse(obj.roofKindsJson)    || mapState.BB_ROOF_KINDS;     } catch (error) { console.warn("map: operation failed", error instanceof Error ? error.name : typeof error); } }
            if (typeof obj.terrainTypesJson === 'string') { try { mapState.BB_TERRAIN_TYPES  = JSON.parse(obj.terrainTypesJson) || mapState.BB_TERRAIN_TYPES;  } catch (error) { console.warn("map: operation failed", error instanceof Error ? error.name : typeof error); } }
            if (typeof obj.splinePresetsJson === 'string'){ try { mapState.BB_SPLINE_PRESETS = JSON.parse(obj.splinePresetsJson)|| mapState.BB_SPLINE_PRESETS; } catch (error) { console.warn("map: operation failed", error instanceof Error ? error.name : typeof error); } }
            if (typeof obj.markingStylesJson === 'string'){ try { mapState.BB_MARKING_STYLES = JSON.parse(obj.markingStylesJson)|| mapState.BB_MARKING_STYLES; } catch (error) { console.warn("map: operation failed", error instanceof Error ? error.name : typeof error); } }
            if (typeof obj.knotTypesJson    === 'string') { try { mapState.BB_KNOT_TYPES     = JSON.parse(obj.knotTypesJson)    || mapState.BB_KNOT_TYPES;     } catch (error) { console.warn("map: operation failed", error instanceof Error ? error.name : typeof error); } }
        } catch (error) { console.warn("map: operation failed", error instanceof Error ? error.name : typeof error); }
        mapState.renderBottomBar();
    };
}
