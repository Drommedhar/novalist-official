import { mapState } from './map-state.js';
import { walkNodes } from './map-layers.js';
import { splineProfile, sampleSpline, lerpColor } from './spline-geometry.js';
import { distToSegment } from './terrain-editing.js';

export function buildingTypeDef(type) { return mapState.BUILDING_TYPES[type] || mapState.BUILDING_TYPES.singleFamily; }

// Tiny seeded RNG (mulberry32) — drives the per-type footprint generators.
export function mulberry32(seed) {
    let a = seed >>> 0;
    return function () {
        a |= 0; a = a + 0x6D2B79F5 | 0;
        let t = Math.imul(a ^ a >>> 15, 1 | a);
        t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
        return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
}

export function rectPoly(w, h) {
    return [{ x: -w / 2, y: -h / 2 }, { x: w / 2, y: -h / 2 },
            { x: w / 2, y: h / 2 }, { x: -w / 2, y: h / 2 }];
}

// Per-type random footprint generator → polygon centred on the origin.
export function generateFootprint(type, rng) {
    const jit = (base, amt) => base + (rng() - 0.5) * 2 * amt;
    if (type === 'rowHome') return rectPoly(jit(11, 2), jit(22, 4));
    if (type === 'singleFamily') {
        const w = jit(18, 3), h = jit(15, 3);
        const TL = { x: -w / 2, y: -h / 2 }, TR = { x: w / 2, y: -h / 2 };
        const BR = { x: w / 2, y: h / 2 }, BL = { x: -w / 2, y: h / 2 };
        if (rng() > 0.4) { // garage / wing bump
            const gw = jit(7, 1.5), gh = jit(8, 2);
            if (rng() > 0.5) // bump on the right, between TR and BR
                return [TL, TR, { x: w / 2, y: h / 2 - gh },
                        { x: w / 2 + gw, y: h / 2 - gh }, { x: w / 2 + gw, y: h / 2 }, BR, BL];
            // bump on the left, between BL and TL
            return [TL, TR, BR, BL, { x: -w / 2 - gw, y: h / 2 },
                    { x: -w / 2 - gw, y: h / 2 - gh }, { x: -w / 2, y: h / 2 - gh }];
        }
        return [TL, TR, BR, BL];
    }
    if (type === 'school') {
        const w = jit(42, 6), h = jit(24, 4), ww = jit(14, 3), wh = jit(16, 3);
        const poly = rectPoly(w, h);
        poly.splice(2, 0, { x: w / 2, y: h / 2 - wh },
            { x: w / 2 + ww, y: h / 2 - wh }, { x: w / 2 + ww, y: h / 2 });
        return poly;
    }
    if (type === 'police') return rectPoly(jit(22, 3), jit(18, 3));
    if (type === 'fireStation') {
        const w = jit(26, 3), h = jit(18, 2);
        const poly = rectPoly(w, h);
        poly.splice(2, 0, { x: w / 2, y: h / 2 - 4 },
            { x: w / 2 + 6, y: h / 2 - 4 }, { x: w / 2 + 6, y: h / 2 });
        return poly;
    }
    if (type === 'hall') return rectPoly(jit(34, 5), jit(22, 4));
    if (type === 'playground') {
        const n = 8, rad = jit(16, 3), out = [];
        for (let i = 0; i < n; i++) {
            const ang = (i / n) * Math.PI * 2, r = rad * jit(1, 0.25);
            out.push({ x: Math.cos(ang) * r, y: Math.sin(ang) * r });
        }
        return out;
    }
    if (type === 'trainStation') return rectPoly(jit(46, 6), jit(14, 2));
    return rectPoly(jit(18, 3), jit(15, 3));
}

export function initializeBuildingGeometry() {
    mapState.BUILDING_TYPES = {
        rowHome:      { roofColor: '#b06a4a', outline: '#5e3826' },
        singleFamily: { roofColor: '#c08552', outline: '#6e4626' },
        school:       { roofColor: '#9a8f73', outline: '#534b39' },
        police:       { roofColor: '#6f7d92', outline: '#3b4452' },
        fireStation:  { roofColor: '#a8443a', outline: '#5e231e' },
        hall:         { roofColor: '#8a7fa0', outline: '#473f57' },
        playground:   { roofColor: '#d6c060', outline: '#7a6c2e' },
        trainStation: { roofColor: '#7a6d5a', outline: '#3f382d' },
    };
}

// Footprint in absolute world coords (rotation + translation applied).
export function buildingWorldPoints(b) {
    const rad = (b.rotation || 0) * Math.PI / 180;
    const cos = Math.cos(rad), sin = Math.sin(rad);
    const ox = b.x || 0, oy = b.y || 0;
    return (b.footprint || []).map(p => ({
        x: ox + p.x * cos - p.y * sin,
        y: oy + p.x * sin + p.y * cos,
    }));
}

// Snap the placement preview parallel + offset to the nearest road (any layer).
// Shift on the move event suppresses it (free placement), like knot snapping.
export function attachBuildingToRoads(b, wx, wy, shift) {
    b.rotation = 0;
    if (shift) {
        b.x = wx; b.y = wy;
        b.rotation = mapState.buildingDraftRotation; // free placement → user angle
        return;
    }
    let best = null, bestD = 90 / mapState.zoom;
    walkNodes(mapState.mapData.layers, node => {
        for (const sp of node.splines || []) {
            if (sp.kind !== 'road' || !sp.points || sp.points.length < 2) continue;
            const prof = splineProfile(sp.kind, sp.preset);
            const samples = sampleSpline(sp.points, prof.straight, sp.closed);
            for (let i = 0; i < samples.length - 1; i++) {
                const a = samples[i], c = samples[i + 1];
                const d = distToSegment(wx, wy, a.x, a.y, c.x, c.y);
                if (d < bestD) { bestD = d; best = { a, c }; }
            }
        }
    });
    if (!best) {
        // No road in range → free placement with user-controlled rotation.
        b.x = wx; b.y = wy;
        b.rotation = mapState.buildingDraftRotation;
        return;
    }
    const dx = best.c.x - best.a.x, dy = best.c.y - best.a.y;
    const len2 = dx * dx + dy * dy || 1;
    let t = ((wx - best.a.x) * dx + (wy - best.a.y) * dy) / len2;
    t = Math.max(0, Math.min(1, t));
    const nx = best.a.x + t * dx, ny = best.a.y + t * dy;
    const tl = Math.hypot(dx, dy) || 1, tnx = dx / tl, tny = dy / tl;
    let normX = -tny, normY = tnx;
    if ((wx - nx) * normX + (wy - ny) * normY < 0) { normX = -normX; normY = -normY; }
    const halfW = ((best.a.w || 24) + (best.c.w || 24)) / 4;
    // Front faces the road, plus the user's wheel-driven offset.
    b.rotation = Math.atan2(tny, tnx) * 180 / Math.PI + 90 + mapState.buildingDraftRotation;
    // Project the rotated footprint onto the road normal: the most-negative
    // projection is the vertex reaching furthest toward the road. Offset the
    // building so that vertex sits `margin` past the road edge → no overlap.
    const rad = b.rotation * Math.PI / 180, cos = Math.cos(rad), sin = Math.sin(rad);
    let minProj = 1e9;
    for (const p of b.footprint) {
        const rx = p.x * cos - p.y * sin, ry = p.x * sin + p.y * cos;
        const proj = rx * normX + ry * normY;
        if (proj < minProj) minProj = proj;
    }
    const margin = 2;
    const dist = halfW + margin - minProj;
    b.x = nx + normX * dist;
    b.y = ny + normY * dist;
}

export function polyCentroid(pts) {
    let cx = 0, cy = 0;
    for (const p of pts) { cx += p.x; cy += p.y; }
    return { x: cx / pts.length, y: cy / pts.length };
}

// Ridge axis = direction of the longest footprint edge.
export function footprintRidgeDir(pts) {
    let bx = 1, by = 0, bl = -1;
    for (let i = 0; i < pts.length; i++) {
        const a = pts[i], b = pts[(i + 1) % pts.length];
        const dx = b.x - a.x, dy = b.y - a.y, l = dx * dx + dy * dy;
        if (l > bl) { bl = l; bx = dx; by = dy; }
    }
    const l = Math.hypot(bx, by) || 1;
    return { x: bx / l, y: by / l };
}

// Lighten (f > 0) or darken (f < 0) a #rrggbb colour by fraction |f|.
export function shadeRoof(hex, f) {
    return f >= 0 ? lerpColor(hex, '#ffffff', f) : lerpColor(hex, '#000000', -f);
}

// Clip a polygon to one half-plane of the line through p0 along dir. keepPos =
// keep the +normal side. Sutherland-Hodgman against a single edge.
export function clipPolyHalf(poly, p0, dir, keepPos) {
    const nx = -dir.y, ny = dir.x;
    const sgn = keepPos ? 1 : -1;
    const side = q => ((q.x - p0.x) * nx + (q.y - p0.y) * ny) * sgn;
    const out = [];
    const n = poly.length;
    for (let i = 0; i < n; i++) {
        const a = poly[i], b = poly[(i + 1) % n];
        const sa = side(a), sb = side(b);
        if (sa >= 0) out.push(a);
        if ((sa >= 0) !== (sb >= 0)) {
            const t = sa / (sa - sb);
            out.push({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t });
        }
    }
    return out;
}

// Roof geometry for the roof view: two shaded eave planes (the footprint split
// along the ridge centre-line, so they always clip exactly to the footprint —
// no spill on rotated / irregular shapes), plus the ridge line and, for hips,
// hip lines fanning to the corners. flat = nothing.
// Returns { faces: [{pts, shade}], lines: [{x1,y1,x2,y2}] }.
export function buildRoof(b, footprint) {
    const kind = (b.roof && b.roof.kind) || 'gable';
    if (kind === 'flat' || footprint.length < 3) return { faces: [], lines: [] };
    const c = polyCentroid(footprint);
    const d = footprintRidgeDir(footprint);
    const nx = -d.y, ny = d.x;
    let aLo = 1e9, aHi = -1e9, pSpan = 0;
    for (const q of footprint) {
        const px = q.x - c.x, py = q.y - c.y;
        const a = px * d.x + py * d.y;
        aLo = Math.min(aLo, a); aHi = Math.max(aHi, a);
    }
    let pLo = 1e9, pHi = -1e9;
    for (const q of footprint) {
        const p = (q.x - c.x) * nx + (q.y - c.y) * ny;
        pLo = Math.min(pLo, p); pHi = Math.max(pHi, p);
    }
    pSpan = pHi - pLo;
    const aSpan = aHi - aLo;
    // Ridge runs the full along-axis for a gable; a hip pulls the ends in.
    const e = kind === 'hip' ? Math.min(pSpan * 0.5, aSpan * 0.42) : 0;
    const W = (a, p) => ({ x: c.x + d.x * a + nx * p, y: c.y + d.y * a + ny * p });
    const rS = W(aLo + e, 0), rE = W(aHi - e, 0);
    // Two eave planes = footprint clipped by the ridge centre-line.
    const planePos = clipPolyHalf(footprint, c, d, true);
    const planeNeg = clipPolyHalf(footprint, c, d, false);
    const lx = -0.55, ly = -0.84; // fixed light, upper-left, world space
    // Pitch drives the slope-shading contrast (top-down can't show true 3D
    // steepness): shallow roof = nearly flat-looking, steep roof = strong faces.
    const pitch = (b.roof && b.roof.pitch != null) ? b.roof.pitch : 0.5;
    const contrast = 0.06 + 0.30 * Math.max(0, Math.min(1, pitch));
    const shadeOf = (sx, sy) => {
        const sl = Math.hypot(sx, sy) || 1;
        return contrast * ((sx / sl) * lx + (sy / sl) * ly);
    };
    const faces = [];
    if (planePos.length >= 3) faces.push({ pts: planePos, shade: shadeOf(nx, ny) });
    if (planeNeg.length >= 3) faces.push({ pts: planeNeg, shade: shadeOf(-nx, -ny) });
    const lines = [{ x1: rS.x, y1: rS.y, x2: rE.x, y2: rE.y }];
    if (kind === 'hip') {
        for (const en of [rS, rE]) {
            const near = footprint.slice().sort((p, q) =>
                Math.hypot(p.x - en.x, p.y - en.y) - Math.hypot(q.x - en.x, q.y - en.y));
            for (const v of near.slice(0, 2))
                lines.push({ x1: en.x, y1: en.y, x2: v.x, y2: v.y });
        }
    }
    return { faces, lines };
}

// Naive inward polygon inset (miter offset) — used for upper-floor outlines.
export function insetPolygon(pts, dist) {
    const n = pts.length;
    if (dist <= 0 || n < 3) return pts.map(p => ({ x: p.x, y: p.y }));
    let area = 0;
    for (let i = 0; i < n; i++) { const a = pts[i], b = pts[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
    const sign = area > 0 ? 1 : -1;
    const out = [];
    for (let i = 0; i < n; i++) {
        const prev = pts[(i - 1 + n) % n], cur = pts[i], next = pts[(i + 1) % n];
        let e1x = cur.x - prev.x, e1y = cur.y - prev.y, e2x = next.x - cur.x, e2y = next.y - cur.y;
        const l1 = Math.hypot(e1x, e1y) || 1, l2 = Math.hypot(e2x, e2y) || 1;
        e1x /= l1; e1y /= l1; e2x /= l2; e2y /= l2;
        const n1x = e1y * sign, n1y = -e1x * sign, n2x = e2y * sign, n2y = -e2x * sign;
        let bx = n1x + n2x, by = n1y + n2y;
        const bl = Math.hypot(bx, by) || 1; bx /= bl; by /= bl;
        let dot = bx * n1x + by * n1y;
        if (Math.abs(dot) < 0.25) dot = dot < 0 ? -0.25 : 0.25; // clamp sharp miters
        const s = dist / dot;
        out.push({ x: cur.x + bx * s, y: cur.y + by * s });
    }
    return out;
}

// Upper floors lose usable area to the roof slope. Returns null (no shrink) or
// { kind, amt } where amt is a fraction in [0, 0.8].
export function roofShrink(roof, floorIdx) {
    if (floorIdx <= 0 || !roof || roof.kind === 'flat') return null;
    const amt = Math.min(0.8, (roof.pitch || 0) * floorIdx * 0.22);
    if (amt <= 0) return null;
    return { kind: roof.kind === 'hip' ? 'hip' : 'gable', amt };
}

export function buildingFloorOutline(b, floorIdx) {
    const fp = b.footprint || [];
    const sh = roofShrink(b.roof, floorIdx);
    if (!sh || fp.length < 3) return fp.map(p => ({ x: p.x, y: p.y }));
    const c = polyCentroid(fp);
    if (sh.kind === 'hip') {
        // Hip: all sides slope inward → uniform inset.
        let r = 0;
        for (const p of fp) r += Math.hypot(p.x - c.x, p.y - c.y);
        r /= fp.length;
        return insetPolygon(fp, r * sh.amt);
    }
    // Gable: only the eave sides slope → squeeze perpendicular to the ridge
    // axis; the gable ends (along the ridge) keep their extent.
    const d = footprintRidgeDir(fp);
    const nx = -d.y, ny = d.x;
    return fp.map(p => {
        const px = p.x - c.x, py = p.y - c.y;
        const along = px * d.x + py * d.y;
        const perp = (px * nx + py * ny) * (1 - sh.amt);
        return { x: c.x + d.x * along + nx * perp, y: c.y + d.y * along + ny * perp };
    });
}

// Rotate footprint + every floor's contents (walls, stairs, labels, pins) around
// the footprint centroid. Openings store a parametric t along walls / outline
// edges, so they follow automatically.
export function rotateBuildingGeometry(b, c, delta) {
    const cos = Math.cos(delta), sin = Math.sin(delta);
    const rot = (x, y) => {
        const dx = x - c.x, dy = y - c.y;
        return { x: c.x + dx * cos - dy * sin, y: c.y + dx * sin + dy * cos };
    };
    (b.footprint || []).forEach(p => { const r = rot(p.x, p.y); p.x = r.x; p.y = r.y; });
    const degDelta = delta * 180 / Math.PI;
    (b.floors || []).forEach(fl => {
        (fl.walls || []).forEach(w => {
            const a = rot(w.x1, w.y1), d = rot(w.x2, w.y2);
            w.x1 = a.x; w.y1 = a.y; w.x2 = d.x; w.y2 = d.y;
        });
        (fl.stairs || []).forEach(s => {
            const r = rot(s.x, s.y); s.x = r.x; s.y = r.y;
            s.rotation = (s.rotation || 0) + degDelta;
        });
        (fl.labels || []).forEach(l => { const r = rot(l.x, l.y); l.x = r.x; l.y = r.y; });
        (fl.pins || []).forEach(p => { const r = rot(p.x, p.y); p.x = r.x; p.y = r.y; });
    });
}
