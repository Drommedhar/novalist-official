import { mapState } from './map-state.js';
import { walkNodes } from './map-layers.js';

export function splineProfile(kind, preset) {
    // "custom:<id>" → a user-authored profile from mapData.customProfiles,
    // normalised into the builtin profile shape the renderer expects.
    if (preset && preset.indexOf('custom:') === 0) {
        const id = preset.slice(7);
        const cp = (mapState.mapData.customProfiles || []).find(p => p.id === id);
        if (cp) return {
            defaultWidth: cp.defaultWidth || 24,
            casing: { color: cp.casingColor || '#3f3413', extra: cp.casingExtra == null ? 3 : cp.casingExtra },
            bands: (cp.bands && cp.bands.length) ? cp.bands : [{ from: -1, to: 1, color: '#cccccc' }],
            markings: cp.markings || [],
            straight: !!cp.straight,
        };
    }
    const table = mapState.SPLINE_PROFILES[kind] || mapState.SPLINE_PROFILES.road;
    return table[preset] || Object.values(table)[0];
}

export function findSpline(splineId) {
    let found = null;
    walkNodes(mapState.mapData.layers, n => {
        if (!found) found = (n.splines || []).find(s => s.id === splineId);
    });
    return found || null;
}

export function findSplineOwner(splineId) {
    let owner = null;
    walkNodes(mapState.mapData.layers, n => {
        if (!owner && (n.splines || []).some(s => s.id === splineId)) owner = n;
    });
    return owner;
}

// Centripetal Catmull-Rom: returns densely-sampled {x,y,w,seg,t} points.
// seg = lower knot index of the segment this sample belongs to; t = fraction
// (0..1) within that knot-segment — used for per-knot type cross-fading.
export function sampleSpline(points, straight, closed) {
    if (!points || points.length === 0) return [];
    if (points.length === 1)
        return [{ x: points[0].x, y: points[0].y, w: points[0].width || 24, seg: 0, t: 0 }];
    const n = points.length;
    const loop = !!closed && n >= 3;        // Catmull-Rom wrap needs 3+ knots
    const out = [];
    if (straight || n === 2) {
        const wrap = !!closed && n >= 2;
        const segs = wrap ? n : n - 1;
        for (let i = 0; i < segs; i++) {
            const a = points[i], b = points[(i + 1) % n];
            const steps = 12;
            for (let s = 0; s < steps; s++) {
                const t = s / steps;
                out.push({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t,
                           w: (a.width || 24) + ((b.width || 24) - (a.width || 24)) * t,
                           seg: i, t });
            }
        }
        const endIdx = wrap ? 0 : n - 1;
        const end = points[endIdx];
        out.push({ x: end.x, y: end.y, w: end.width || 24, seg: segs - 1, t: 1 });
        return out;
    }
    // Hermite spline with per-knot tangents. With sharpness 0 and no angle
    // override the tangents reduce to the centripetal Catmull-Rom tangents, so
    // this matches the previous behaviour exactly.
    const m = knotTangents(points, loop);
    const segs = loop ? n : n - 1;
    for (let i = 0; i < segs; i++) {
        const p1 = points[i];
        const j = loop ? (i + 1) % n : i + 1;
        const p2 = points[j];
        const m1 = m[i], m2 = m[j];
        const w1 = p1.width || 24, w2 = p2.width || 24;
        const steps = 18;
        for (let s = 0; s < steps; s++) {
            const t = s / steps, t2 = t * t, t3 = t2 * t;
            const h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t;
            const h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
            const x = h00 * p1.x + h10 * m1.x + h01 * p2.x + h11 * m2.x;
            const y = h00 * p1.y + h10 * m1.y + h01 * p2.y + h11 * m2.y;
            out.push({ x, y, w: w1 + (w2 - w1) * t, seg: i, t });
        }
    }
    const endIdx = loop ? 0 : n - 1;
    const end = points[endIdx];
    out.push({ x: end.x, y: end.y, w: end.width || 24, seg: segs - 1, t: 1 });
    return out;
}

// Per-knot Hermite tangents. Base = centripetal-style cardinal tangent
// 0.5*(next-prev). An optional per-knot `angle` (radians) forces the tangent
// direction; `sharpness` (0..1) scales the magnitude toward zero — at 1 the
// knot becomes a hard corner (straight in, straight out).
export function knotTangents(points, closed) {
    const n = points.length;
    const m = [];
    for (let i = 0; i < n; i++) {
        const knot = points[i];
        const prev = closed ? points[(i - 1 + n) % n] : points[Math.max(0, i - 1)];
        const next = closed ? points[(i + 1) % n] : points[Math.min(n - 1, i + 1)];
        let tx = 0.5 * (next.x - prev.x), ty = 0.5 * (next.y - prev.y);
        if (knot.angle != null) {
            let mag = Math.hypot(tx, ty);
            if (mag < 1e-3) {
                const dn = Math.hypot(next.x - knot.x, next.y - knot.y);
                const dp = Math.hypot(knot.x - prev.x, knot.y - prev.y);
                mag = 0.5 * (Math.max(dn, dp) || 24);
            }
            tx = Math.cos(knot.angle) * mag;
            ty = Math.sin(knot.angle) * mag;
        }
        const sharp = knot.sharpness || 0;
        m.push({ x: tx * (1 - sharp), y: ty * (1 - sharp) });
    }
    return m;
}

// Linear-interpolate two #rrggbb colours.
export function lerpColor(a, b, t) {
    const pa = parseInt(a.slice(1), 16), pb = parseInt(b.slice(1), 16);
    const ar = (pa >> 16) & 255, ag = (pa >> 8) & 255, ab = pa & 255;
    const br = (pb >> 16) & 255, bg = (pb >> 8) & 255, bb = pb & 255;
    const r = Math.round(ar + (br - ar) * t);
    const g = Math.round(ag + (bg - ag) * t);
    const bl = Math.round(ab + (bb - ab) * t);
    return '#' + ((1 << 24) | (r << 16) | (g << 8) | bl).toString(16).slice(1);
}

// Resolve the profile for one knot, honouring its optional type override.
export function knotProfile(spline, knotIdx) {
    const knot = spline.points[Math.max(0, Math.min(spline.points.length - 1, knotIdx))];
    return splineProfile(spline.kind, (knot && knot.typeOverride) || spline.preset);
}

// Remap the raw segment fraction by the spline's blend factor:
// b=1 → linear (full smooth), b=0 → hard step at the midpoint,
// in between → the transition is compressed into the middle `b` of the segment.
export function blendT(t, b) {
    if (b >= 1) return t;
    if (b <= 0) return t < 0.5 ? 0 : 1;
    return Math.max(0, Math.min(1, (t - 0.5) / b + 0.5));
}

// Blended visual at a sample: cross-fades casing + fill colours between the
// two knots bounding the sample's segment (so a spline can morph type-to-type).
// A segment's blend softness is the MIN of its two knots' factors — so dialling
// one knot's slider down hardens both segments touching it.
export function knotBlendFactor(knot) {
    return (knot && knot.blendFactor != null) ? knot.blendFactor : 1;
}

// Cross-fade context at a sample: the two bounding knots' profiles + the
// blend-remapped fraction. Pass-level code lerps casing / bands from this.
export function sampleBlend(spline, sample) {
    const N = spline.points.length;
    const segB = spline.closed ? (sample.seg + 1) % N : sample.seg + 1;
    const pA = knotProfile(spline, sample.seg);
    const pB = knotProfile(spline, segB);
    const bf = Math.min(knotBlendFactor(spline.points[sample.seg]),
                        knotBlendFactor(spline.points[segB]));
    return { pA, pB, t: blendT(sample.t, bf) };
}

export function sampleVisual(spline, sample) {
    const { pA, pB, t } = sampleBlend(spline, sample);
    return {
        casingColor: lerpColor(pA.casing.color, pB.casing.color, t),
        casingExtra: pA.casing.extra + (pB.casing.extra - pA.casing.extra) * t,
    };
}

// Quad path between two samples at a given half-width fraction (+ extra world units).
export function segQuad(s0, n0, s1, n1, frac, extra) {
    const o0 = (s0.w / 2) * frac + extra;
    const o1 = (s1.w / 2) * frac + extra;
    const l0x = (s0.x + n0.x * o0) * mapState.zoom, l0y = (s0.y + n0.y * o0) * mapState.zoom;
    const l1x = (s1.x + n1.x * o1) * mapState.zoom, l1y = (s1.y + n1.y * o1) * mapState.zoom;
    const r0x = (s0.x - n0.x * o0) * mapState.zoom, r0y = (s0.y - n0.y * o0) * mapState.zoom;
    const r1x = (s1.x - n1.x * o1) * mapState.zoom, r1y = (s1.y - n1.y * o1) * mapState.zoom;
    return `M${l0x.toFixed(1)},${l0y.toFixed(1)} L${l1x.toFixed(1)},${l1y.toFixed(1)} ` +
           `L${r1x.toFixed(1)},${r1y.toFixed(1)} L${r0x.toFixed(1)},${r0y.toFixed(1)} Z`;
}

// Quad spanning two arbitrary half-width fractions (a band's from→to edges).
export function segQuadBand(s0, n0, s1, n1, from, to) {
    const a0 = (s0.w / 2) * from, b0 = (s0.w / 2) * to;
    const a1 = (s1.w / 2) * from, b1 = (s1.w / 2) * to;
    const ax0 = (s0.x + n0.x * a0) * mapState.zoom, ay0 = (s0.y + n0.y * a0) * mapState.zoom;
    const bx0 = (s0.x + n0.x * b0) * mapState.zoom, by0 = (s0.y + n0.y * b0) * mapState.zoom;
    const ax1 = (s1.x + n1.x * a1) * mapState.zoom, ay1 = (s1.y + n1.y * a1) * mapState.zoom;
    const bx1 = (s1.x + n1.x * b1) * mapState.zoom, by1 = (s1.y + n1.y * b1) * mapState.zoom;
    return `M${ax0.toFixed(1)},${ay0.toFixed(1)} L${ax1.toFixed(1)},${ay1.toFixed(1)} ` +
           `L${bx1.toFixed(1)},${by1.toFixed(1)} L${bx0.toFixed(1)},${by0.toFixed(1)} Z`;
}

// Per-sample unit normals (perpendicular to the tangent).
// When closed, the sample list's last entry duplicates the first, so the
// cyclic neighbour lookup spans length-1 to skip that duplicate.
export function sampleNormals(samples, closed) {
    const normals = [];
    const m = samples.length;
    const cyc = closed ? m - 1 : m;
    for (let i = 0; i < m; i++) {
        let pi, ni;
        if (closed) {
            pi = ((i - 1) % cyc + cyc) % cyc;
            ni = (i + 1) % cyc;
        } else {
            pi = Math.max(0, i - 1);
            ni = Math.min(m - 1, i + 1);
        }
        const prev = samples[pi], next = samples[ni];
        let tx = next.x - prev.x, ty = next.y - prev.y;
        const len = Math.hypot(tx, ty) || 1;
        tx /= len; ty /= len;
        normals.push({ x: -ty, y: tx }); // rotate tangent 90°
    }
    return normals;
}

// Build an SVG path 'd' for a band polygon between two half-width fractions.
export function bandPath(samples, normals, fromFrac, toFrac, extra) {
    const left = [], right = [];
    for (let i = 0; i < samples.length; i++) {
        const s = samples[i], nrm = normals[i];
        const half = s.w / 2;
        const outer = half * toFrac + (toFrac >= 0 ? extra : -extra);
        const inner = half * fromFrac + (fromFrac >= 0 ? extra : -extra);
        left.push([(s.x + nrm.x * outer) * mapState.zoom, (s.y + nrm.y * outer) * mapState.zoom]);
        right.push([(s.x + nrm.x * inner) * mapState.zoom, (s.y + nrm.y * inner) * mapState.zoom]);
    }
    let d = 'M' + left.map(p => p[0].toFixed(1) + ',' + p[1].toFixed(1)).join(' L');
    d += ' L' + right.reverse().map(p => p[0].toFixed(1) + ',' + p[1].toFixed(1)).join(' L');
    return d + ' Z';
}

// Build an SVG path 'd' for a marking line at an offset fraction of half-width.
export function markingPath(samples, normals, offsetFrac) {
    const pts = [];
    for (let i = 0; i < samples.length; i++) {
        const s = samples[i], nrm = normals[i];
        const o = (s.w / 2) * offsetFrac;
        pts.push([(s.x + nrm.x * o) * mapState.zoom, (s.y + nrm.y * o) * mapState.zoom]);
    }
    return 'M' + pts.map(p => p[0].toFixed(1) + ',' + p[1].toFixed(1)).join(' L');
}
