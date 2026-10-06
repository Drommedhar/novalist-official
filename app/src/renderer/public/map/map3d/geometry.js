import * as THREE from "../three.webgpu.min.js";

export function smoothstep01(t) {
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    return t * t * (3 - 2 * t);
}

// Signed area (shoelace) of a polygon [{x,y}].
export function polygonArea(poly) {
    let a = 0;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++)
        a += (poly[j].x + poly[i].x) * (poly[j].y - poly[i].y);
    return a / 2;
}

// Shortest distance from point (px,py) to segment (x1,y1)-(x2,y2).
export function distToSeg(px, py, x1, y1, x2, y2) {
    const dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy;
    let t = l2 ? ((px - x1) * dx + (py - y1) * dy) / l2 : 0;
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    return Math.hypot(px - (x1 + t * dx), py - (y1 + t * dy));
}

// Shortest distance from a point to a polygon's boundary.
export function distToPolygon(px, py, poly) {
    let min = Infinity;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
        const d = distToSeg(px, py, poly[j].x, poly[j].y, poly[i].x, poly[i].y);
        if (d < min) min = d;
    }
    return min;
}

// Even-odd ray cast — is (px, py) inside the polygon (whatever winding)?
export function pointInPolygon(px, py, poly) {
    let inside = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
        const xi = poly[i].x, yi = poly[i].y;
        const xj = poly[j].x, yj = poly[j].y;
        const intersect = ((yi > py) !== (yj > py))
            && (px < (xj - xi) * (py - yi) / (yj - yi + 1e-9) + xi);
        if (intersect) inside = !inside;
    }
    return inside;
}

// For a sampled centreline, build the closed ribbon outline (left bank
// forward + right bank reversed). `extraHalf` is added to s.w/2 so road
// casings can be expanded to their outer edge for grass occlusion.
export function ribbonOutlinePolygon(samples, extraHalf) {
    const n = samples.length;
    if (n < 2) return [];
    const tan = [];
    for (let i = 0; i < n; i++) {
        const prev = samples[Math.max(0, i - 1)];
        const next = samples[Math.min(n - 1, i + 1)];
        const dx = next.x - prev.x, dy = next.y - prev.y;
        const len = Math.hypot(dx, dy) || 1;
        tan.push({ x: dx / len, y: dy / len });
    }
    const left = [], right = [];
    for (let i = 0; i < n; i++) {
        const s = samples[i], t = tan[i];
        const nx = -t.y, ny = t.x, hw = (s.w || 4) / 2 + (extraHalf || 0);
        left.push({ x: s.x + nx * hw, y: s.y + ny * hw });
        right.push({ x: s.x - nx * hw, y: s.y - ny * hw });
    }
    return left.concat(right.reverse());
}

export function riverOutlinePolygon(samples) {
    return ribbonOutlinePolygon(samples, 0);
}

// Midpoint-subdivide a flat triangle list (9 floats per triangle) `levels`
// times — each pass turns one triangle into four. Used to give the lake bed
// enough interior vertices to actually curve.
export function subdivideTris(tris, levels) {
    let cur = tris;
    for (let l = 0; l < levels; l++) {
        const next = [];
        for (let i = 0; i < cur.length; i += 9) {
            const ax = cur[i], ay = cur[i + 1], az = cur[i + 2];
            const bx = cur[i + 3], by = cur[i + 4], bz = cur[i + 5];
            const cx = cur[i + 6], cy = cur[i + 7], cz = cur[i + 8];
            const abx = (ax + bx) / 2, aby = (ay + by) / 2, abz = (az + bz) / 2;
            const bcx = (bx + cx) / 2, bcy = (by + cy) / 2, bcz = (bz + cz) / 2;
            const cax = (cx + ax) / 2, cay = (cy + ay) / 2, caz = (cz + az) / 2;
            next.push(ax, ay, az, abx, aby, abz, cax, cay, caz);
            next.push(abx, aby, abz, bx, by, bz, bcx, bcy, bcz);
            next.push(cax, cay, caz, bcx, bcy, bcz, cx, cy, cz);
            next.push(abx, aby, abz, bcx, bcy, bcz, cax, cay, caz);
        }
        cur = next;
    }
    return cur;
}

// Concatenate position + normal of several non-indexed geometries into one
// (drops uv — merged building geometry is untextured).
export function mergeGeometries(geoms) {
    let total = 0;
    for (const g of geoms) total += g.attributes.position.count;
    const pos = new Float32Array(total * 3);
    const nrm = new Float32Array(total * 3);
    let off = 0;
    for (const g of geoms) {
        const p = g.attributes.position.array;
        pos.set(p, off * 3);
        if (g.attributes.normal) nrm.set(g.attributes.normal.array, off * 3);
        off += g.attributes.position.count;
    }
    const out = new THREE.BufferGeometry();
    out.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    out.setAttribute('normal', new THREE.BufferAttribute(nrm, 3));
    return out;
}

// Group meshes by shared material and merge each group into a single mesh
// (bakes each mesh's transform into the geometry). Cuts draw calls hard for
// floor-plan-heavy buildings. Single-mesh groups pass through untouched.
export function mergeByMaterial(meshes) {
    const groups = new Map();
    for (const m of meshes) {
        if (!groups.has(m.material)) groups.set(m.material, []);
        groups.get(m.material).push(m);
    }
    const out = [];
    groups.forEach((list, mat) => {
        if (list.length === 1) { out.push(list[0]); return; }
        const geoms = list.map(m => {
            m.updateMatrix();
            const g = m.geometry.index
                ? m.geometry.toNonIndexed() : m.geometry.clone();
            g.applyMatrix4(m.matrix);
            return g;
        });
        const merged = new THREE.Mesh(mergeGeometries(geoms), mat);
        for (const g of geoms) g.dispose();
        out.push(merged);
    });
    return out;
}
