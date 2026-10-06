import * as THREE from "../three.webgpu.min.js";
import { distToPolygon, pointInPolygon, polygonArea, smoothstep01, subdivideTris } from './geometry.js';
import { sceneState } from './scene-state.js';
import {
    WATER_DEEP_REACH,
    WATER_FALLBACK_COLOR,
    WATER_FLOW,
    WATER_FLOW_TAPER,
    WATER_LAKE_LOD_FAR,
    WATER_LAKE_SUBDIV_BASE,
    WATER_LAKE_SUBDIV_FAR,
    WATER_RIVER_SUBDIV,
    WATER_TURB,
} from './water-settings.js';
import { toColor } from './map-content.js';

// Closed water body → a flat surface mesh in local XY (x = world x, y =
// -world z, so a -90deg X rotation lays it flat with +Z up). The outline is
// triangulated and subdivided for interior vertices; each vertex carries a
// baked `waterDepth` (smoothstep of its distance to the shore) and a
// `flowDir` (a gentle uniform drift — lakes have no current).
export function buildLakeWaterGeo(outline) {
    if (!outline || outline.length < 3) return null;
    if (Math.abs(polygonArea(outline)) < 1) return null;
    const shape = new THREE.Shape();
    shape.moveTo(outline[0].x, outline[0].y);
    for (let i = 1; i < outline.length; i++)
        shape.lineTo(outline[i].x, outline[i].y);
    shape.closePath();
    const sg = new THREE.ShapeGeometry(shape).toNonIndexed();
    let tris = Array.from(sg.attributes.position.array); // (worldX, worldZ, 0)
    sg.dispose();
    if (tris.length === 0) return null;
    // LOD: drop one subdivision level for lakes whose centroid sits past
    // WATER_LAKE_LOD_FAR from the current camera position. Cheap, evaluated
    // once at build; rebuildScene re-runs when the camera shifts enough.
    let cx = 0, cy = 0, n = 0;
    for (let i = 0; i < tris.length; i += 3) {
        cx += tris[i]; cy += tris[i + 1]; n++;
    }
    cx /= n; cy /= n;
    let camDist = 0;
    if (sceneState.camera) {
        const dx = cx - sceneState.camera.position.x, dz = cy - sceneState.camera.position.z;
        camDist = Math.hypot(dx, dz);
    }
    const subdivLevels = camDist > WATER_LAKE_LOD_FAR
        ? WATER_LAKE_SUBDIV_FAR : WATER_LAKE_SUBDIV_BASE;
    tris = subdivideTris(tris, subdivLevels);
    // First pass: position + raw shore distance, tracking the deepest point
    // so the depth fade is normalised to THIS lake's shape (an elongated
    // lake's middle is far closer to shore than an equal-area circle's).
    const pos = [], dist = [];
    let maxD = 1e-6;
    for (let i = 0; i < tris.length; i += 3) {
        const wx = tris[i], wz = tris[i + 1];
        pos.push(wx, -wz, 0); // local XY for the -90deg X rotation
        const dd = distToPolygon(wx, wz, outline);
        dist.push(dd);
        if (dd > maxD) maxD = dd;
    }
    // Basin curve: gentle shallow SHELF along the outer rim, then a smooth
    // ramp all the way to the centre. NO flatline plateau — a bird's-eye
    // top-down lake view shows the centre across most of the screen, so a
    // plateau there makes most of the visible water read as one saturated
    // depth (caustics + tint look uniform). Smooth ramp keeps the dRaw
    // gradient continuous across the whole surface; only the very centre
    // reaches d=1 after BASIN_DEPTH scaling.
    const reach = maxD * WATER_DEEP_REACH;
    const SHELF = 0.10;
    function basinCurve(m) {
        if (m <= 0) return 0;
        if (m >= 1) return 1;
        if (m < SHELF) {
            const t = m / SHELF;
            return 0.12 * t * t * (3 - 2 * t); // shelf up to ~0.12
        }
        const t = (m - SHELF) / (1 - SHELF);
        return 0.12 + 0.88 * t * t * (3 - 2 * t); // smooth ramp to 1 at centre
    }
    const depth = dist.map(dd => basinCurve(dd / reach));
    // Lakes have no current — a gentle uniform drift keeps ripples alive.
    const flow = [];
    for (let i = 0; i < depth.length; i++) flow.push(0.5, 0.35);
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setAttribute('waterDepth', new THREE.Float32BufferAttribute(depth, 1));
    geo.setAttribute('flowDir', new THREE.Float32BufferAttribute(flow, 2));
    geo.computeVertexNormals();
    return geo;
}

// Open river → a flat ribbon surface in local XY with cross-section rows.
// `waterDepth` runs 0 at the banks to 1 along the centreline. `flowDir`
// follows the centreline tangent, plus a curvature-driven cross-flow kick
// on bends (turbulence) and a center-fast / bank-slow speed taper.
export function buildRiverWaterGeo(samples) {
    if (!samples || samples.length < 2) return null;
    const { CROSS, denseSamples } = sampleRiverCrossSections(samples);
    samples = denseSamples;
    const n = samples.length;
    // Per-sample unit tangent (smoothed) along the centreline.
    const tan = [];
    for (let i = 0; i < n; i++) {
        const prev = samples[Math.max(0, i - 1)];
        const next = samples[Math.min(n - 1, i + 1)];
        const dx = next.x - prev.x, dy = next.y - prev.y;
        const len = Math.hypot(dx, dy) || 1;
        tan.push({ x: dx / len, y: dy / len });
    }
    const rows = [];
    for (let i = 0; i < n; i++) {
        const s = samples[i];
        const t = tan[i];
        const nx = -t.y, ny = t.x, hw = (s.w || 4) / 2;
        // Curvature: signed turn between the tangents two samples either
        // side (~sin of the bend angle). Drives a cross-flow so the water
        // churns on bends instead of sliding straight through.
        const tp = tan[Math.max(0, i - 2)], tn = tan[Math.min(n - 1, i + 2)];
        const turn = tp.x * tn.y - tp.y * tn.x;
        // World-space flow = tangent + curvature cross-flow, renormalised.
        let fwx = t.x + nx * turn * WATER_TURB;
        let fwy = t.y + ny * turn * WATER_TURB;
        const fl = Math.hypot(fwx, fwy) || 1;
        fwx /= fl; fwy /= fl;
        const row = [];
        for (const f of CROSS) {
            // Mid-channel runs fast, banks creep — steep falloff so the
            // speed difference actually reads (a linear taper looks uniform
            // across most of the width).
            const speed = 0.05 + 0.95 * Math.pow(1 - Math.abs(f), WATER_FLOW_TAPER);
            const mag = WATER_FLOW * speed;
            row.push({
                x: s.x + nx * f * hw,
                z: s.y + ny * f * hw,
                d: smoothstep01((1 - Math.abs(f)) / WATER_DEEP_REACH),
                // Flow in geometry's local XY (x = world x, y = -world z).
                // WaterNode's setup() does `flow.x *= -1` internally; the
                // sampler then offsets uv BY flow*offset, so the pattern
                // appears to move OPPOSITE the flow vector. To get the
                // texture to scroll downstream (along +tangent in world),
                // we feed flow = +tangent (no negation); WaterNode flips
                // x, the negative uv offset moves the pattern toward +x.
                // Y axis: local y = -world z, and WaterNode doesn't flip
                // y, so to scroll the pattern toward +worldZ we feed
                // flow.y = +world_tangent_y (because uv.y INCREASING
                // makes pattern visually move toward -y_local = +worldZ).
                fx: fwx * mag, fy: fwy * mag,
            });
        }
        rows.push(row);
    }
    const pos = [], depth = [], flow = [];
    const v = p => {
        pos.push(p.x, -p.z, 0);
        depth.push(p.d);
        flow.push(p.fx, p.fy);
    };
    for (let i = 0; i < rows.length - 1; i++) {
        const A = rows[i], B = rows[i + 1];
        for (let j = 0; j < CROSS.length - 1; j++) {
            const a = A[j], b = A[j + 1], c = B[j + 1], d = B[j];
            v(a); v(b); v(c);
            v(a); v(c); v(d);
        }
    }
    if (pos.length === 0) return null;
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setAttribute('waterDepth', new THREE.Float32BufferAttribute(depth, 1));
    geo.setAttribute('flowDir', new THREE.Float32BufferAttribute(flow, 2));
    geo.computeVertexNormals();
    return geo;
}

// Walk every vertex of the merged water mesh and recompute waterDepth +
// waterFill + waterRim from the UNION of the source polygons. For each
// vertex collect the distance-to-bank from EVERY source that contains it,
// then take the max — at a junction interior the vertex is inside both
// ribbons and the larger of the two distances reads as deep, so foam +
// depth tint don't streak across the seam. Colour is blended by those
// same distances so the dominant (deeper-containing) river wins.
export function rebakeMergedWaterAttrs(merged, sources) {
    const pos = merged.attributes.position.array;
    const n = merged.attributes.position.count;
    const depthAttr = merged.attributes.waterDepth.array;
    const fillAttr = merged.attributes.waterFill.array;
    const rimAttr = merged.attributes.waterRim.array;
    // First sweep: for every merged vertex, see which source polygons
    // contain it, and update each containing source's maxD with the
    // vertex's distance-to-bank. Gives a per-source normalisation that
    // matches how the per-spline bake worked before (deepest interior
    // point of that source maps to depth=1).
    for (const s of sources) s.maxD = 1e-6;
    for (let i = 0; i < n; i++) {
        const wx = pos[i * 3], wy = -pos[i * 3 + 1];
        for (const s of sources) {
            if (!pointInPolygon(wx, wy, s.outline)) continue;
            const dd = distToPolygon(wx, wy, s.outline);
            if (dd > s.maxD) s.maxD = dd;
        }
    }
    // SHELF / DROP from buildLakeWaterGeo's basin curve — keep the same
    // profile here so the depth tint matches what individual lakes had.
    const SHELF = 0.10;
    const basinCurve = (m) => {
        if (m <= 0) return 0;
        if (m >= 1) return 1;
        if (m < SHELF) {
            const t = m / SHELF;
            return 0.12 * t * t * (3 - 2 * t);
        }
        const t = (m - SHELF) / (1 - SHELF);
        return 0.12 + 0.88 * t * t * (3 - 2 * t);
    };
    for (let i = 0; i < n; i++) {
        // Mesh local XY is (worldX, -worldZ); source polygons are in
        // (worldX, worldY=worldZ-as-map-coord). Convert.
        const lx = pos[i * 3];
        const ly = pos[i * 3 + 1];
        const wx = lx, wy = -ly;
        let bestNorm = 0;
        let weightSum = 0;
        let fr = 0, fg = 0, fb = 0, rr = 0, rg = 0, rb = 0;
        for (const s of sources) {
            if (!pointInPolygon(wx, wy, s.outline)) continue;
            const dd = distToPolygon(wx, wy, s.outline);
            const norm = dd / (s.maxD * WATER_DEEP_REACH);
            if (norm > bestNorm) bestNorm = norm;
            const w = dd; // weight = how interior this vertex is in this source
            weightSum += w;
            fr += s.fill.r * w; fg += s.fill.g * w; fb += s.fill.b * w;
            rr += s.rim.r * w;  rg += s.rim.g * w;  rb += s.rim.b * w;
        }
        if (weightSum > 0) {
            depthAttr[i] = basinCurve(Math.min(1, bestNorm));
            fillAttr[i * 3]     = fr / weightSum;
            fillAttr[i * 3 + 1] = fg / weightSum;
            fillAttr[i * 3 + 2] = fb / weightSum;
            rimAttr[i * 3]     = rr / weightSum;
            rimAttr[i * 3 + 1] = rg / weightSum;
            rimAttr[i * 3 + 2] = rb / weightSum;
        }
        // else: vertex isn't inside any source polygon — keep the
        // per-spline values baked earlier (rim vertex outside any
        // polygon is essentially on the boundary anyway).
    }
    merged.attributes.waterDepth.needsUpdate = true;
    merged.attributes.waterFill.needsUpdate = true;
    merged.attributes.waterRim.needsUpdate = true;
}

// Bake a flat per-vertex rgb attribute named `name` from a hex / css colour
// string. Lets the merged water mesh tint each contributing spline's
// vertices independently with no GPU branching.
export function bakeColorAttribute(geo, name, color) {
    const c = toColor(color, WATER_FALLBACK_COLOR);
    const n = geo.attributes.position.count;
    const arr = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
        arr[i * 3]     = c.r;
        arr[i * 3 + 1] = c.g;
        arr[i * 3 + 2] = c.b;
    }
    geo.setAttribute(name, new THREE.Float32BufferAttribute(arr, 3));
}

// Concatenate several water surface BufferGeometries (each carrying
// position + waterDepth + flowDir + waterFill + waterRim) into ONE
// BufferGeometry. Single WaterMesh wraps it; junctions read as merged
// because there are no separate meshes to z-fight or seam.
export function mergeWaterGeometries(geos) {
    let total = 0;
    for (const g of geos) total += g.attributes.position.count;
    if (total === 0) return null;
    const pos   = new Float32Array(total * 3);
    const depth = new Float32Array(total);
    const flow  = new Float32Array(total * 2);
    const fill  = new Float32Array(total * 3);
    const rim   = new Float32Array(total * 3);
    let off = 0;
    for (const g of geos) {
        const n = g.attributes.position.count;
        pos.set(g.attributes.position.array,   off * 3);
        depth.set(g.attributes.waterDepth.array, off);
        flow.set(g.attributes.flowDir.array,    off * 2);
        fill.set(g.attributes.waterFill.array,  off * 3);
        rim.set(g.attributes.waterRim.array,    off * 3);
        off += n;
        g.dispose();
    }
    const merged = new THREE.BufferGeometry();
    merged.setAttribute('position',   new THREE.Float32BufferAttribute(pos,   3));
    merged.setAttribute('waterDepth', new THREE.Float32BufferAttribute(depth, 1));
    merged.setAttribute('flowDir',    new THREE.Float32BufferAttribute(flow,  2));
    merged.setAttribute('waterFill',  new THREE.Float32BufferAttribute(fill,  3));
    merged.setAttribute('waterRim',   new THREE.Float32BufferAttribute(rim,   3));
    merged.computeVertexNormals();
    return merged;
}


function sampleRiverCrossSections(samples) {
    // Build CROSS via cubic-spaced taper, optionally densified by
    // WATER_RIVER_SUBDIV (inserting K-1 evenly-spaced steps between each
    // pair). The d=0 banks must stay exactly at f=±1 so foam still lines
    // up with the rim.
    const BASE_CROSS = [-1, -0.6, -0.25, 0, 0.25, 0.6, 1];
    const sub = Math.max(1, WATER_RIVER_SUBDIV | 0);
    const CROSS = [];
    for (let i = 0; i < BASE_CROSS.length - 1; i++) {
        const a = BASE_CROSS[i], b = BASE_CROSS[i + 1];
        for (let k = 0; k < sub; k++) CROSS.push(a + (b - a) * k / sub);
    }
    CROSS.push(BASE_CROSS[BASE_CROSS.length - 1]);
    // Lengthwise: linearly interpolate extra samples between each pair so
    // there are sub-1 in-between rows per gap. Each new sample inherits a
    // smoothed tangent so the local flow direction stays sensible.
    let denseSamples;
    if (sub === 1) {
        denseSamples = samples;
    } else {
        denseSamples = [];
        for (let i = 0; i < samples.length - 1; i++) {
            const a = samples[i], b = samples[i + 1];
            for (let k = 0; k < sub; k++) {
                const t = k / sub;
                denseSamples.push({
                    x: a.x + (b.x - a.x) * t,
                    y: a.y + (b.y - a.y) * t,
                    w: (a.w || 4) + ((b.w || 4) - (a.w || 4)) * t,
                });
            }
        }
        denseSamples.push(samples[samples.length - 1]);
    }
    return { CROSS, denseSamples };
}
