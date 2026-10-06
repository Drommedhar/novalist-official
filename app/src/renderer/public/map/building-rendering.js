import { mapState } from './map-state.js';
import { buildingTypeDef, buildingWorldPoints, buildRoof, shadeRoof } from './building-geometry.js';
import { buildFloorPlanInto } from './floor-plan-rendering.js';
import { selectBuilding, findBuildingOwner, findBuilding } from './building-editing.js';
import { showBuildingContextMenu } from './building-actions.js';

export function buildingShowsPlan(b) {
    if ((b.floorCount || 0) < 1) return false;
    // The building being floor-plan-edited always shows its plan, regardless of
    // zoom — otherwise its floor poly (and the wall/label/pin click targets)
    // would not exist when editing zoomed out.
    if (mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === b.id) return true;
    return mapState.zoom >= (b.planMinZoom != null ? b.planMinZoom : 4);
}

export function buildBuildingSvg(node, b) {
    const svg = document.createElementNS(mapState.SVG_NS, 'svg');
    svg.setAttribute('class', 'nv-building');
    svg.dataset.layerId = node.id || '';
    svg.dataset.buildingId = b.id;
    svg.style.transform = `scale(${mapState.zoom})`;
    const def = buildingTypeDef(b.type);
    const isDraft = b.id === '__buildingDraft__';
    const sel = !isDraft && mapState.buildingEdit && mapState.buildingEdit.buildingId === b.id;
    const planEditing = !isDraft && mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === b.id;
    const pts = isDraft ? buildingWorldPoints(b) : (b.footprint || []);
    if (pts.length < 3) return svg;
    svg.dataset.planShown = (!isDraft && buildingShowsPlan(b)) ? '1' : '0';
    if (svg.dataset.planShown === '1') {
        buildFloorPlanInto(svg, b, def, sel || planEditing);
        return svg;
    }
    // Roof view.
    const poly = document.createElementNS(mapState.SVG_NS, 'polygon');
    poly.setAttribute('points', pts.map(p => p.x.toFixed(2) + ',' + p.y.toFixed(2)).join(' '));
    poly.setAttribute('fill', def.roofColor);
    poly.setAttribute('stroke', sel ? '#ffae42' : def.outline);
    poly.setAttribute('stroke-width', sel ? 2.2 : 1);
    poly.setAttribute('stroke-linejoin', 'round');
    if (!isDraft) {
        poly.style.cursor = 'pointer';
        poly.addEventListener('mousedown', e => {
            if (e.button !== 0) return; // middle/right bubble (pan / context menu)
            if (mapState.mode !== 'edit' || mapState.toolMode !== 'select') return;
            e.stopPropagation();
            selectBuilding(b.id);
        });
        poly.addEventListener('contextmenu', e => {
            if (mapState.mode !== 'edit') return;
            e.preventDefault(); e.stopPropagation();
            showBuildingContextMenu(e, b);
        });
    }
    svg.appendChild(poly);
    if (b.type !== 'playground') {
        const roof = buildRoof(b, pts);
        // Shaded slope planes (decorative — hit-testing stays on the footprint).
        for (const f of roof.faces) {
            const fp = document.createElementNS(mapState.SVG_NS, 'polygon');
            fp.setAttribute('points', f.pts.map(p => p.x.toFixed(2) + ',' + p.y.toFixed(2)).join(' '));
            fp.setAttribute('fill', shadeRoof(def.roofColor, f.shade));
            fp.setAttribute('stroke', def.outline);
            fp.setAttribute('stroke-width', '0.4');
            fp.setAttribute('stroke-linejoin', 'round');
            fp.style.pointerEvents = 'none';
            svg.appendChild(fp);
        }
        for (const r of roof.lines) {
            const ln = document.createElementNS(mapState.SVG_NS, 'line');
            ln.setAttribute('x1', r.x1.toFixed(2)); ln.setAttribute('y1', r.y1.toFixed(2));
            ln.setAttribute('x2', r.x2.toFixed(2)); ln.setAttribute('y2', r.y2.toFixed(2));
            ln.setAttribute('stroke', def.outline);
            ln.setAttribute('stroke-width', '0.8');
            ln.setAttribute('opacity', '0.7');
            ln.style.pointerEvents = 'none';
            svg.appendChild(ln);
        }
    }
    return svg;
}

export function renderBuilding(node, b) {
    if (!b.footprint || b.footprint.length < 3) return;
    mapState.world.appendChild(buildBuildingSvg(node, b));
}

export function rebuildBuildingSvg(id) {
    const owner = findBuildingOwner(id), b = findBuilding(id);
    if (!owner || !b) return;
    const old = mapState.world.querySelector(`.nv-building[data-building-id="${id}"]`);
    const fresh = buildBuildingSvg(owner, b);
    if (old) old.replaceWith(fresh); else mapState.world.appendChild(fresh);
}
