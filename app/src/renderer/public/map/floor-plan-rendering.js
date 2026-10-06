import { mapState } from './map-state.js';
import { buildingFloorOutline } from './building-geometry.js';
import { onBuildingPlanMouseDown, onWallMouseDown, deleteWall, openingSegment, flipOpening, deleteOpening, deleteStair } from './floor-plan-editing.js';
import { showBuildingContextMenu } from './building-actions.js';
import { showSimpleContextMenu } from './map-context-menu.js';

// Draw one staircase glyph. opts.ghost = faint, dashed, non-interactive (used as
// a vertical-alignment guide showing adjacent floors' stairs).
export function drawStairInto(st, opts) {
    opts = opts || {};
    const g = document.createElementNS(mapState.SVG_NS, 'g');
    g.setAttribute('transform', `translate(${st.x} ${st.y}) rotate(${st.rotation || 0})`);
    const W = st.width || 10, Ln = st.length || 18;
    const rect = document.createElementNS(mapState.SVG_NS, 'rect');
    rect.setAttribute('x', -W / 2); rect.setAttribute('y', -Ln / 2);
    rect.setAttribute('width', W); rect.setAttribute('height', Ln);
    rect.setAttribute('fill', opts.ghost ? 'none' : '#e6dcc6');
    rect.setAttribute('stroke', '#3a342c');
    rect.setAttribute('stroke-width', '0.8');
    if (opts.ghost) rect.setAttribute('stroke-dasharray', '2 2');
    g.appendChild(rect);
    for (let k = 1; k < 6; k++) {
        const y = -Ln / 2 + Ln * k / 6;
        const tl = document.createElementNS(mapState.SVG_NS, 'line');
        tl.setAttribute('x1', -W / 2); tl.setAttribute('y1', y);
        tl.setAttribute('x2', W / 2); tl.setAttribute('y2', y);
        tl.setAttribute('stroke', '#3a342c'); tl.setAttribute('stroke-width', '0.5');
        g.appendChild(tl);
    }
    const dir = st.direction === 'down' ? -1 : 1;
    const arrow = document.createElementNS(mapState.SVG_NS, 'path');
    arrow.setAttribute('d', `M 0 ${dir * Ln * 0.3} L ${-W / 4} ${-dir * Ln * 0.05} L ${W / 4} ${-dir * Ln * 0.05} Z`);
    arrow.setAttribute('fill', '#3a342c'); arrow.setAttribute('opacity', '0.6');
    g.appendChild(arrow);
    if (opts.ghost) {
        g.setAttribute('opacity', '0.28');
        g.style.pointerEvents = 'none';
    }
    return g;
}

// Floor-plan view: usable outline + walls (+ the wall draft while editing).
export function buildFloorPlanInto(svg, b, def, highlight) {
    const { fi, floor, editingThis } = renderFloorPlanSurface(svg, b, def, highlight);
    renderFloorWalls(svg, b, fi, floor, editingThis);
    renderFloorOpenings(svg, b, fi, floor, editingThis);
    renderFloorStairs(svg, b, fi, floor, editingThis);
}

export function renderFloorPlanSurface(svg, b, def, highlight) {
    const fi = b.activeFloor || 0;
    if (fi > 0) {
        const ghost = document.createElementNS(mapState.SVG_NS, 'polygon');
        ghost.setAttribute('points', (b.footprint || []).map(p => p.x.toFixed(2) + ',' + p.y.toFixed(2)).join(' '));
        ghost.setAttribute('fill', 'none');
        ghost.setAttribute('stroke', def.outline);
        ghost.setAttribute('stroke-width', '0.6');
        ghost.setAttribute('stroke-dasharray', '3 3');
        ghost.setAttribute('opacity', '0.5');
        svg.appendChild(ghost);
    }
    const outline = buildingFloorOutline(b, fi);
    const poly = document.createElementNS(mapState.SVG_NS, 'polygon');
    poly.setAttribute('points', outline.map(p => p.x.toFixed(2) + ',' + p.y.toFixed(2)).join(' '));
    poly.setAttribute('fill', '#f4efe6');
    poly.setAttribute('stroke', highlight ? '#ffae42' : def.outline);
    poly.setAttribute('stroke-width', highlight ? 2 : 1.2);
    poly.setAttribute('stroke-linejoin', 'round');
    poly.style.cursor = 'pointer';
    poly.addEventListener('mousedown', e => onBuildingPlanMouseDown(e, b));
    poly.addEventListener('contextmenu', e => {
        if (mapState.mode !== 'edit') return;
        e.preventDefault(); e.stopPropagation();
        showBuildingContextMenu(e, b);
    });
    svg.appendChild(poly);
    const floor = (b.floors || [])[fi] || {};
    const editingThis = mapState.floorPlanEdit && mapState.floorPlanEdit.buildingId === b.id;
    return { fi, floor, editingThis };
}

export function renderFloorWalls(svg, b, fi, floor, editingThis) {
    (floor.walls || []).forEach((w, wi) => {
        const ln = document.createElementNS(mapState.SVG_NS, 'line');
        ln.setAttribute('x1', w.x1.toFixed(2)); ln.setAttribute('y1', w.y1.toFixed(2));
        ln.setAttribute('x2', w.x2.toFixed(2)); ln.setAttribute('y2', w.y2.toFixed(2));
        ln.setAttribute('stroke', '#3a342c');
        ln.setAttribute('stroke-width', w.thickness || 3);
        ln.setAttribute('stroke-linecap', 'round');
        ln.style.pointerEvents = 'stroke';
        ln.addEventListener('mousedown', e => onWallMouseDown(e, b, wi, w));
        ln.addEventListener('contextmenu', e => {
            if (mapState.mode !== 'edit' || !editingThis) return;
            e.preventDefault(); e.stopPropagation();
            deleteWall(b.id, fi, wi);
        });
        svg.appendChild(ln);
    });
}

export function renderFloorOpenings(svg, b, fi, floor, editingThis) {
    (floor.openings || []).forEach((op, oi) => {
        const w = openingSegment(b, fi, op);
        if (!w) return;
        const dx = w.x2 - w.x1, dy = w.y2 - w.y1, L = Math.hypot(dx, dy) || 1;
        const ux = dx / L, uy = dy / L;
        const sn = op.flip ? -1 : 1;
        const nx = -uy * sn, ny = ux * sn;
        const half = (op.width || 8) / 2;
        const cx = w.x1 + ux * (L * op.t), cy = w.y1 + uy * (L * op.t);
        const ax = cx - ux * half, ay = cy - uy * half;
        const bx = cx + ux * half, by = cy + uy * half;
        const gap = document.createElementNS(mapState.SVG_NS, 'line');
        gap.setAttribute('x1', ax); gap.setAttribute('y1', ay);
        gap.setAttribute('x2', bx); gap.setAttribute('y2', by);
        gap.setAttribute('stroke', '#f4efe6');
        gap.setAttribute('stroke-width', (w.thickness || 3) + 1.5);
        gap.setAttribute('stroke-linecap', 'butt');
        gap.style.pointerEvents = 'stroke';
        gap.addEventListener('contextmenu', e => {
            if (mapState.mode !== 'edit' || !editingThis) return;
            e.preventDefault(); e.stopPropagation();
            const items = [];
            if (op.kind === 'door')
                items.push([mapState.mapStrings.fpFlipSide, () => flipOpening(b.id, fi, oi)]);
            items.push([mapState.mapStrings.ctxDelete, () => deleteOpening(b.id, fi, oi)]);
            showSimpleContextMenu(e, items);
        });
        svg.appendChild(gap);
        if (op.kind === 'window') {
            const g = document.createElementNS(mapState.SVG_NS, 'line');
            g.setAttribute('x1', ax); g.setAttribute('y1', ay);
            g.setAttribute('x2', bx); g.setAttribute('y2', by);
            g.setAttribute('stroke', '#6f9ab8');
            g.setAttribute('stroke-width', '1');
            svg.appendChild(g);
        } else {
            const leaf = document.createElementNS(mapState.SVG_NS, 'line');
            leaf.setAttribute('x1', ax); leaf.setAttribute('y1', ay);
            leaf.setAttribute('x2', ax + nx * op.width); leaf.setAttribute('y2', ay + ny * op.width);
            leaf.setAttribute('stroke', '#3a342c'); leaf.setAttribute('stroke-width', '1');
            svg.appendChild(leaf);
            const arc = document.createElementNS(mapState.SVG_NS, 'path');
            const sweep = op.flip ? 0 : 1;
            arc.setAttribute('d', `M ${bx} ${by} A ${op.width} ${op.width} 0 0 ${sweep} ${ax + nx * op.width} ${ay + ny * op.width}`);
            arc.setAttribute('fill', 'none');
            arc.setAttribute('stroke', '#3a342c');
            arc.setAttribute('stroke-width', '0.6');
            arc.setAttribute('opacity', '0.6');
            svg.appendChild(arc);
        }
    });
}

export function renderFloorStairs(svg, b, fi, floor, editingThis) {
    if (editingThis) {
        [fi - 1, fi + 1].forEach(adj => {
            const af = (b.floors || [])[adj];
            if (!af) return;
            (af.stairs || []).forEach(st => svg.appendChild(drawStairInto(st, { ghost: true })));
        });
    }
    (floor.stairs || []).forEach((st, si) => {
        const g = drawStairInto(st, {});
        g.style.cursor = 'pointer';
        g.addEventListener('contextmenu', e => {
            if (mapState.mode !== 'edit' || !editingThis) return;
            e.preventDefault(); e.stopPropagation();
            deleteStair(b.id, fi, si);
        });
        svg.appendChild(g);
    });
    if (editingThis && mapState.floorPlanTool === 'stairs' && mapState.stairDraft && mapState.stairDraft.cursor) {
        const s = mapState.stairDraft.start, c = mapState.stairDraft.cursor;
        const dx = c.x - s.x, dy = c.y - s.y, len = Math.hypot(dx, dy);
        if (len > 1) {
            const g = document.createElementNS(mapState.SVG_NS, 'g');
            g.setAttribute('transform',
                `translate(${(s.x + c.x) / 2} ${(s.y + c.y) / 2}) rotate(${Math.atan2(dy, dx) * 180 / Math.PI - 90})`);
            const rect = document.createElementNS(mapState.SVG_NS, 'rect');
            rect.setAttribute('x', -5); rect.setAttribute('y', -len / 2);
            rect.setAttribute('width', 10); rect.setAttribute('height', len);
            rect.setAttribute('fill', 'rgba(255,174,66,0.3)');
            rect.setAttribute('stroke', '#ffae42');
            rect.setAttribute('stroke-width', '1');
            rect.setAttribute('stroke-dasharray', '3 2');
            g.appendChild(rect);
            svg.appendChild(g);
        }
    }
    if (editingThis && mapState.wallDraft && mapState.wallDraft.points.length) {
        const dp = mapState.wallDraft.points;
        for (let i = 0; i < dp.length - 1; i++) {
            const ln = document.createElementNS(mapState.SVG_NS, 'line');
            ln.setAttribute('x1', dp[i].x.toFixed(2)); ln.setAttribute('y1', dp[i].y.toFixed(2));
            ln.setAttribute('x2', dp[i + 1].x.toFixed(2)); ln.setAttribute('y2', dp[i + 1].y.toFixed(2));
            ln.setAttribute('stroke', '#ffae42');
            ln.setAttribute('stroke-width', '3');
            ln.setAttribute('stroke-linecap', 'round');
            svg.appendChild(ln);
        }
    }
}
