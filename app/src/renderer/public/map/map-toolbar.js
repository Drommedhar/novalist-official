import { mapState } from './map-state.js';
import { bbHint, bbKbd, bbButton, bbSelect, bbCheckbox, bbPopover, bbColor, bbLayerSelect, bbSlider, bbNumber } from './map-controls.js';
import { renderClipEditor, exitClipEdit } from './image-clipping.js';
import { setFloorPlanTool } from './floor-plan-editing.js';
import { exitFloorPlanEdit, enterFloorPlanEdit } from './floor-plan-view.js';
import { findSpline, splineProfile, findSplineOwner } from './spline-geometry.js';
import { exitSplineEdit } from './spline-actions.js';
import { findShape, terrainColor, findShapeOwner, exitShapeEdit, commitTerrainDraft, cancelTerrainDraft } from './terrain-editing.js';
import { exitBorderEdit, commitBorderDraft, cancelBorderDraft } from './border-editing.js';
import { findBuilding, findBuildingOwner } from './building-editing.js';
import { commitSplineDraft, cancelSplineDraft } from './spline-draft.js';
import { renderSelectedBar } from './selection-toolbar.js';

export function render3DBar() {
    mapState.bb.left.textContent = mapState.mapStrings.bb3dLabel || '3D view';
    mapState.bb.center.appendChild(bbHint(mapState.mapStrings.bb3dHint || 'Click the world to look around.'));
    mapState.bb.center.appendChild(bbKbd(mapState.mapStrings.kbd3dMove || 'W A S D move - Q E up/down - Esc release the pointer'));
}

export function renderIdleBar() {
    mapState.bb.left.textContent = mapState.mapStrings.bbIdle || 'Map editor';
    mapState.bb.center.appendChild(bbHint(mapState.mapStrings.bbIdleHint || 'Click an element to edit. Use the ribbon to add new ones.'));
}

export function renderClipBar() {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.clip;
    mapState.bb.left.textContent = mapState.mapStrings.bbClipLabel || 'Edit clip mask';
    mapState.bb.center.appendChild(bbHint(mapState.mapStrings.clipHint));
    mapState.bb.center.appendChild(bbKbd(mapState.mapStrings.kbdEscCancelEnterCommit || 'Esc cancel · Enter commit'));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.clipClear,  () => { if (mapState.clipEdit) { mapState.clipEdit.points = []; renderClipEditor(); } }, { danger: true }));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.clipCancel, () => exitClipEdit(false)));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.clipDone,   () => exitClipEdit(true), { primary: true }));
}

export function renderFloorPlanBar() {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.floorplan;
    mapState.bb.left.textContent = mapState.mapStrings.bbFloorPlanLabel || 'Floor plan';
    const tools = [['wall', 'fpWall'], ['door', 'fpDoor'], ['window', 'fpWindow'],
                   ['stairs', 'fpStairs'], ['flabel', 'fpLabel'], ['fpin', 'fpPin']];
    for (const [tool, key] of tools) {
        mapState.bb.center.appendChild(bbButton(mapState.mapStrings[key], () => setFloorPlanTool(tool),
                                       { active: mapState.floorPlanTool === tool }));
    }
    mapState.bb.center.appendChild(bbHint(mapState.mapStrings.fpHint));
    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdEscCancelEnterCommit || 'Esc cancel · Enter commit'));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.splineEditDone, exitFloorPlanEdit, { primary: true }));
}

export function renderDraftBar(kind, commit, cancel) {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT[kind];
    mapState.bb.left.textContent = (mapState.mapStrings['bb' + kind[0].toUpperCase() + kind.slice(1) + 'Draft']) || (kind + ' draft');
    mapState.bb.center.appendChild(bbHint(mapState.mapStrings[kind + 'EditHint'] || mapState.mapStrings.bbClickToAddPoints || 'Click to add points.'));
    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdEscCancelEnterCommit || 'Esc cancel · Enter commit'));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.cancel || 'Cancel', cancel));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.splineEditDone, commit, { primary: true }));
}

export function renderBuildingDraftBar() {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.building;
    mapState.bb.left.textContent = mapState.mapStrings.bbBuildingPlace || 'Place buildings';
    mapState.bb.center.appendChild(bbHint(mapState.mapStrings.bbBuildingHint || 'Click to place. Shift = free angle. Right-drag + wheel = rotate.'));
    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdBuildingPlace || 'R re-roll · Esc exit'));
}

export function renderSplineEditBar() {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.spline;
    mapState.bb.left.textContent = mapState.mapStrings.bbSplineLabel || 'Spline';
    const sp = mapState.splineEdit && findSpline(mapState.splineEdit.splineId);
    if (!sp) return;
    const isRoad = sp.kind === 'road';
    const hasOverrides = !!(sp.casingColor || sp.fillColor || sp.markingColor);
    const prof = splineProfile(sp.kind, sp.preset);

    mapState.bb.center.appendChild(bbSelect(`${sp.kind}:${sp.preset}`, mapState.BB_SPLINE_PRESETS,
        v => { const i = v.indexOf(':'); window.setSplinePreset(sp.id, v.slice(0, i), v.slice(i + 1)); },
        mapState.mapStrings.splineType));
    mapState.bb.center.appendChild(bbCheckbox(mapState.mapStrings.splineClosed, !!sp.closed,
        v => window.setSplineClosed(sp.id, v)));
    if (isRoad) {
        mapState.bb.center.appendChild(bbSelect(sp.markingStyle || '', mapState.BB_MARKING_STYLES,
            v => window.setSplineMarkingStyle(sp.id, v), mapState.mapStrings.splineMarking));
    }
    if (isRoad) {
        mapState.bb.center.appendChild(bbPopover(mapState.mapStrings.bbColors || 'Colors', pop => {
            pop.appendChild(bbColor(sp.casingColor  || prof.casing.color,
                hex => window.setSplineCasingColor(sp.id, hex), mapState.mapStrings.colorCasing));
            pop.appendChild(bbColor(sp.fillColor    || prof.bands[0].color,
                hex => window.setSplineFillColor(sp.id, hex), mapState.mapStrings.colorFill));
            pop.appendChild(bbColor(sp.markingColor || '#ffffff',
                hex => window.setSplineMarkingColor(sp.id, hex), mapState.mapStrings.colorMarking));
            if (hasOverrides) pop.appendChild(bbButton(mapState.mapStrings.colorReset, () => window.resetSplineColors(sp.id)));
        }));
    } else {
        mapState.bb.center.appendChild(bbColor(sp.fillColor || prof.bands[0].color,
            hex => window.setSplineFillColor(sp.id, hex), mapState.mapStrings.colorFill));
        if (hasOverrides) mapState.bb.center.appendChild(bbButton(mapState.mapStrings.colorReset, () => window.resetSplineColors(sp.id)));
    }
    if (mapState.splineEdit.selKnot != null && mapState.splineEdit.selKnot >= 0) {
        mapState.bb.center.appendChild(bbPopover(`${mapState.mapStrings.bbKnot || 'Knot'} ${mapState.splineEdit.selKnot + 1}`,
            pop => renderKnotInspector(pop, sp, mapState.splineEdit.selKnot)));
    }
    const ls = bbLayerSelect('spline', sp.id, findSplineOwner(sp.id)?.id || '');
    if (ls) mapState.bb.center.appendChild(ls);

    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdFinishDelete || 'Esc / Enter finish · Delete remove'));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.shapeForward,  () => window.moveSplineZ(sp.id, +1)));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.shapeBackward, () => window.moveSplineZ(sp.id, -1)));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.splineDelete,  () => window.deleteSpline(sp.id), { danger: true }));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.splineEditDone, exitSplineEdit, { primary: true }));
}

export function renderKnotInspector(pop, sp, idx) {
    const p = sp.points[idx] || {};
    pop.appendChild(bbSelect(p.typeOverride || '', mapState.BB_KNOT_TYPES,
        v => window.setKnotType(sp.id, idx, v || ''), mapState.mapStrings.splineType));
    if ((sp.points || []).length >= 2) {
        pop.appendChild(bbSlider(p.blendFactor == null ? 1 : p.blendFactor, 0, 1, 0.05,
            v => window.setKnotBlend(sp.id, idx, v), mapState.mapStrings.splineBlend));
    }
    if ((sp.points || []).length >= 3) {
        pop.appendChild(bbSlider(p.sharpness || 0, 0, 1, 0.05,
            v => window.setKnotSharpness(sp.id, idx, v), mapState.mapStrings.knotSharpness));
    }
    if (sp.kind === 'road') {
        pop.appendChild(bbSelect(p.markingStyle || '', mapState.BB_MARKING_STYLES,
            v => window.setKnotMarkingStyle(sp.id, idx, v), mapState.mapStrings.splineMarking));
    }
}

export function renderTerrainEditBar() {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.terrain;
    mapState.bb.left.textContent = mapState.mapStrings.bbTerrainLabel || 'Terrain shape';
    const sh = mapState.terrainEdit && findShape(mapState.terrainEdit.shapeId);
    if (!sh) return;
    mapState.bb.center.appendChild(bbSelect(sh.type || 'grass', mapState.BB_TERRAIN_TYPES,
        v => window.setShapeType(sh.id, v), mapState.mapStrings.shapeType));
    mapState.bb.center.appendChild(bbColor(sh.color || terrainColor(sh.type),
        hex => window.setShapeColor(sh.id, hex), mapState.mapStrings.shapeColor));
    if ((sh.points || []).length >= 3) {
        mapState.bb.center.appendChild(bbCheckbox(mapState.mapStrings.shapeSmooth, sh.smooth !== false,
            v => window.setShapeSmooth(sh.id, v)));
    }
    if (mapState.SHAPE_BLEND_TYPES.has(sh.type)) {
        mapState.bb.center.appendChild(bbSlider(sh.blendStrength || 0, 0, 120, 4,
            v => window.setShapeBlend(sh.id, v), mapState.mapStrings.shapeBlend));
    }
    const ls = bbLayerSelect('shape', sh.id, findShapeOwner(sh.id)?.id);
    if (ls) mapState.bb.center.appendChild(ls);
    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdFinish || 'Esc / Enter finish'));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.shapeForward,  () => window.moveShapeZ(sh.id, +1)));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.shapeBackward, () => window.moveShapeZ(sh.id, -1)));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.shapeDelete, () => window.deleteShape(sh.id), { danger: true }));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.splineEditDone, exitShapeEdit, { primary: true }));
}

export function renderBorderEditBar() {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.border;
    mapState.bb.left.textContent = mapState.mapStrings.bbBorderLabel || 'Border';
    const b = mapState.mapData.border;
    if (b) {
        mapState.bb.center.appendChild(bbColor(b.outlineColor || '#1c1a18',
            hex => window.setBorderOutline(hex, b.outlineWidth || 4),
            mapState.mapStrings.borderOutlineColor));
        mapState.bb.center.appendChild(bbNumber(b.outlineWidth || 4, 0, 40, 1,
            v => window.setBorderOutline(b.outlineColor || '#1c1a18', v),
            mapState.mapStrings.borderOutlineWidth));
    }
    mapState.bb.center.appendChild(bbHint(mapState.mapStrings.borderEditHint || ''));
    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdFinish || 'Esc / Enter finish'));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.borderClear || 'Clear', () => window.clearBorder(), { danger: true }));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.splineEditDone, exitBorderEdit, { primary: true }));
}

export function renderBuildingEditBar() {
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.building;
    mapState.bb.left.textContent = mapState.mapStrings.bbBuildingLabel || 'Building';
    const b = mapState.buildingEdit && findBuilding(mapState.buildingEdit.buildingId);
    if (!b) return;
    const hasRoof   = !mapState.NO_ROOF_TYPES.has(b.type);
    const hasFloors = !mapState.NO_FLOORS_TYPES.has(b.type);
    const roofKind  = (b.roof && b.roof.kind) || 'gable';
    const flatOnly  = mapState.FLAT_ROOF_TYPES.has(b.type);

    mapState.bb.center.appendChild(bbSelect(b.type || 'singleFamily', mapState.BB_BUILDING_TYPES,
        v => window.setBuildingType(b.id, v), mapState.mapStrings.buildingType));
    if (hasRoof && !flatOnly) {
        mapState.bb.center.appendChild(bbSelect(roofKind, mapState.BB_ROOF_KINDS,
            v => window.setBuildingRoof(b.id, v, (b.roof && b.roof.pitch) != null ? b.roof.pitch : 0.5),
            mapState.mapStrings.buildingRoof));
    }
    if (hasFloors) {
        mapState.bb.center.appendChild(bbNumber(b.floorCount || 0, 0, 20, 1,
            v => window.setBuildingFloors(b.id, v), mapState.mapStrings.buildingFloors));
    }
    const popItems = [];
    if (hasRoof && roofKind !== 'flat' && !flatOnly) {
        popItems.push(pop => pop.appendChild(bbSlider((b.roof && b.roof.pitch) != null ? b.roof.pitch : 0.5, 0, 1, 0.05,
            v => window.setBuildingRoof(b.id, roofKind, v), mapState.mapStrings.roofPitch)));
    }
    if (hasFloors && (b.floorCount || 0) >= 1) {
        popItems.push(pop => pop.appendChild(bbNumber(b.planMinZoom != null ? b.planMinZoom : 4, 1, 20, 0.5,
            v => window.setBuildingPlanZoom(b.id, v), mapState.mapStrings.buildingPlanZoom)));
    }
    popItems.push(pop => {
        pop.appendChild(bbButton(mapState.mapStrings.shapeForward,  () => window.moveBuildingZ(b.id, +1)));
        pop.appendChild(bbButton(mapState.mapStrings.shapeBackward, () => window.moveBuildingZ(b.id, -1)));
    });
    mapState.bb.center.appendChild(bbPopover(mapState.mapStrings.bbMore || 'More…',
        pop => popItems.forEach(fn => fn(pop))));
    const ls = bbLayerSelect('building', b.id, findBuildingOwner(b.id)?.id);
    if (ls) mapState.bb.center.appendChild(ls);

    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdEscDeselect || 'Esc deselect'));
    if ((b.floorCount || 0) >= 1) {
        mapState.bb.right.appendChild(bbButton(mapState.mapStrings.bbEditFloorPlan || 'Edit floor plan',
            () => enterFloorPlanEdit(b.id)));
    }
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.buildingDelete,
        () => window.deleteBuilding(b.id), { danger: true }));
}

export function initializeMapToolbar() {
    mapState.BB_BUILDING_TYPES = [['singleFamily','Single family'], ['rowHome','Row home'],
        ['school','School'], ['police','Police'], ['fireStation','Fire station'],
        ['hall','Hall'], ['playground','Playground'], ['trainStation','Train station']];
    mapState.BB_ROOF_KINDS = [['gable','Gable'], ['hip','Hip'], ['flat','Flat']];
    mapState.BB_TERRAIN_TYPES = [['grass','Grass'], ['forest','Forest'], ['concrete','Concrete'],
        ['sand','Sand'], ['hills','Hills'], ['mountain','Mountain'], ['water','Water']];
    mapState.BB_SPLINE_PRESETS = [
        ['road:motorway','Motorway'], ['road:primary','Primary'], ['road:secondary','Secondary'],
        ['road:residential','Residential'], ['road:service','Service'], ['road:pedestrian','Pedestrian'],
        ['road:trail','Trail'], ['road:track','Track'],
        ['river:brook','Brook'], ['river:stream','Stream'], ['river:river','River'],
        ['river:canal','Canal'], ['river:estuary','Estuary'],
    ];
    mapState.BB_MARKING_STYLES = [['','Default'], ['none','None'], ['single','Single'],
        ['dashed','Dashed'], ['double','Double'], ['solid-dashed','Solid + dashed']];
    mapState.BB_KNOT_TYPES = [['','Inherit'],
        ['motorway','Motorway'], ['primary','Primary'], ['secondary','Secondary'],
        ['residential','Residential'], ['service','Service'], ['pedestrian','Pedestrian'],
        ['trail','Trail'], ['track','Track'],
        ['brook','Brook'], ['stream','Stream'], ['river','River'], ['canal','Canal'], ['estuary','Estuary']];
    mapState.NO_ROOF_TYPES = new Set(['playground']);
    mapState.FLAT_ROOF_TYPES = new Set(['trainStation']);
    mapState.NO_FLOORS_TYPES = new Set(['playground']);
    mapState.SHAPE_BLEND_TYPES = new Set(['grass', 'forest', 'hills', 'mountain', 'sand']);
    mapState.renderBottomBar = function () {
        if (!mapState.bb.el) return;
        mapState.bb.left.textContent = '';
        mapState.bb.center.replaceChildren();
        mapState.bb.right.replaceChildren();
        mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT.idle;
        // 3D first: none of the states below exist up there, and the bar was left
        // telling the writer to click an element and use the ribbon while they were
        // standing in a field. It is also the only place the flight controls are
        // written down - clicking takes the pointer, and a cursor that vanishes
        // with nothing to explain it reads as the app breaking.
        if (window.Map3D && window.Map3D.isActive()) return render3DBar();
        // Priority: explicit modal sub-states first, then selections, then drafts, then idle.
        if (mapState.clipEdit)        return renderClipBar();
        if (mapState.floorPlanEdit)   return renderFloorPlanBar();
        if (mapState.splineDraft)     return renderDraftBar('spline', commitSplineDraft, cancelSplineDraft);
        if (mapState.terrainDraft)    return renderDraftBar('terrain', commitTerrainDraft, cancelTerrainDraft);
        if (mapState.borderDraft)     return renderDraftBar('border', commitBorderDraft, cancelBorderDraft);
        if (mapState.buildingDraft && mapState.toolMode === 'building') return renderBuildingDraftBar();
        if (mapState.splineEdit)      return renderSplineEditBar();
        if (mapState.terrainEdit)     return renderTerrainEditBar();
        if (mapState.borderEdit)      return renderBorderEditBar();
        if (mapState.buildingEdit)    return renderBuildingEditBar();
        if (mapState.selected)        return renderSelectedBar();
        return renderIdleBar();
    };
}
