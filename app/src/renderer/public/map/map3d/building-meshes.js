import * as THREE from "../three.webgpu.min.js";
import { DEG, FLOOR_H, IWALL_H, RO_BILLBOARD, RO_BUILDING } from './scene-settings.js';
import { mapHost } from './host.js';
import { elementShown, mapLayers, toColor } from './map-content.js';
import { mergeByMaterial } from './geometry.js';

// Horizontal polygon cap at height y (floor ledge / ceiling). Optional
// `holes` (array of {x,y} point arrays) are cut out — used for stairwells.
export function polyCapMesh(pts, y, mat, holes) {
    const shape = new THREE.Shape();
    shape.moveTo(pts[0].x, pts[0].y);
    for (let i = 1; i < pts.length; i++) shape.lineTo(pts[i].x, pts[i].y);
    shape.closePath();
    for (const h of holes || []) {
        if (!h || h.length < 3) continue;
        const path = new THREE.Path();
        path.moveTo(h[0].x, h[0].y);
        for (let i = 1; i < h.length; i++) path.lineTo(h[i].x, h[i].y);
        path.closePath();
        shape.holes.push(path);
    }
    const mesh = new THREE.Mesh(new THREE.ShapeGeometry(shape), mat);
    mesh.rotation.x = Math.PI / 2;
    mesh.position.y = y;
    return mesh;
}

// The rotated rectangle footprint of a staircase, as a {x,y} polygon.
export function stairRectPoly(st) {
    const rot = (st.rotation || 0) * DEG, c = Math.cos(rot), s = Math.sin(rot);
    const hw = (st.width || 10) / 2, hl = (st.length || 18) / 2;
    return [[-hw, -hl], [hw, -hl], [hw, hl], [-hw, hl]].map(p => ({
        x: st.x + p[0] * c - p[1] * s,
        y: st.y + p[0] * s + p[1] * c,
    }));
}

// Real 3D roof volume on the top floor. gable / hip = eave planes rising to
// a ridge (hip pulls the ridge ends in); flat = a plain cap. Built off the
// top outline's oriented bounding box (ridge axis = longest edge).
export function buildRoof3D(b, outline, baseY, mat) {
    const kind = (b.roof && b.roof.kind) || 'gable';
    if (kind === 'flat' || outline.length < 3)
        return polyCapMesh(outline, baseY, mat);
    const pitch = (b.roof && b.roof.pitch != null) ? b.roof.pitch : 0.5;
    const c = mapHost.polyCentroid(outline);
    const d = mapHost.footprintRidgeDir(outline);
    const nx = -d.y, ny = d.x;
    let aLo = 1e9, aHi = -1e9, pLo = 1e9, pHi = -1e9;
    for (const q of outline) {
        const px = q.x - c.x, py = q.y - c.y;
        const a = px * d.x + py * d.y, p = px * nx + py * ny;
        aLo = Math.min(aLo, a); aHi = Math.max(aHi, a);
        pLo = Math.min(pLo, p); pHi = Math.max(pHi, p);
    }
    const aSpan = aHi - aLo, pSpan = pHi - pLo;
    const W = (a, p) => ({ x: c.x + d.x * a + nx * p, y: c.y + d.y * a + ny * p });
    const ridgeH = Math.max(2, pitch * (pSpan / 2));
    const e = kind === 'hip' ? Math.min(pSpan * 0.5, aSpan * 0.42) : 0;
    const A = W(aLo, pLo), B = W(aHi, pLo), Cc = W(aHi, pHi), D = W(aLo, pHi);
    const R0 = W(aLo + e, 0), R1 = W(aHi - e, 0);
    const top = baseY + ridgeH;
    const pos = [];
    function tri(p1, y1, p2, y2, p3, y3) {
        pos.push(p1.x, y1, p1.y, p2.x, y2, p2.y, p3.x, y3, p3.y);
    }
    function quad(a, b, c, d) {
        tri(...a, ...b, ...c);
        tri(...a, ...c, ...d);
    }
    quad([A, baseY], [B, baseY], [R1, top], [R0, top]);   // eave plane, pLo side
    quad([D, baseY], [Cc, baseY], [R1, top], [R0, top]);  // eave plane, pHi side
    tri(A, baseY, D, baseY, R0, top);             // end / hip, aLo
    tri(B, baseY, Cc, baseY, R1, top);            // end / hip, aHi
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.computeVertexNormals();
    return new THREE.Mesh(g, mat);
}

// A box for a wall segment (x1,y1)-(x2,y2) in 2D world, from yLo to yHi.
export function wallBox({ x: x1, y: y1 }, { x: x2, y: y2 }, thickness, yLo, yHi, mat) {
    const dx = x2 - x1, dy = y2 - y1, L = Math.hypot(dx, dy);
    if (L < 1e-3 || yHi <= yLo) return null;
    const m = new THREE.Mesh(new THREE.BoxGeometry(L, yHi - yLo, thickness), mat);
    m.position.set((x1 + x2) / 2, (yLo + yHi) / 2, (y1 + y2) / 2);
    m.rotation.y = Math.atan2(-dy, dx);
    m.renderOrder = RO_BUILDING;
    return m;
}

// Gap content for one opening, hosted on segment (hx1,hy1)-(hx2,hy2).
// window → sill + lintel + translucent pane; door → a thin slab. `wallH` is
// the host wall's height (defaults to an interior-wall height). Pushes
// meshes into `out` (merged later by material).
export function addOpening(out, { x: hx1, y: hy1 }, { x: hx2, y: hy2 }, op, { thickness, yBase, wallH }, mats) {
    wallH = wallH || IWALL_H;
    const dx = hx2 - hx1, dy = hy2 - hy1, L = Math.hypot(dx, dy) || 1;
    const half = (op.width || 8) / 2 / L;
    const t0 = Math.max(0, op.t - half), t1 = Math.min(1, op.t + half);
    const A = { x: hx1 + dx * t0, y: hy1 + dy * t0 };
    const B = { x: hx1 + dx * t1, y: hy1 + dy * t1 };
    if (op.kind === 'window') {
        const sillTop = yBase + wallH * 0.35;
        const lintelBot = yBase + wallH * 0.70;
        const sill = wallBox({ x: A.x, y: A.y }, { x: B.x, y: B.y }, thickness, yBase, sillTop, mats.wall);
        if (sill) out.push(sill);
        const lintel = wallBox({ x: A.x, y: A.y }, { x: B.x, y: B.y }, thickness, lintelBot, yBase + wallH, mats.wall);
        if (lintel) out.push(lintel);
        const pane = wallBox({ x: A.x, y: A.y }, { x: B.x, y: B.y }, thickness * 0.35, sillTop, lintelBot, mats.pane);
        if (pane) out.push(pane);
    } else {
        const slab = wallBox({ x: A.x, y: A.y }, { x: B.x, y: B.y }, thickness * 0.5, yBase, yBase + wallH * 0.92, mats.door);
        if (slab) out.push(slab);
    }
}

// Build a building's shared materials.
export function makeBuildingMats(def) {
    return {
        ext: new THREE.MeshLambertMaterial({
            color: toColor(mapHost.lerpColor(def.roofColor, '#ffffff', 0.6), 0xddd6c6),
            side: THREE.DoubleSide,
        }),
        roof: new THREE.MeshLambertMaterial({
            color: toColor(def.roofColor, 0xc08552), side: THREE.DoubleSide,
        }),
        wall: new THREE.MeshLambertMaterial({ color: 0x6b6258, side: THREE.DoubleSide }),
        pane: new THREE.MeshLambertMaterial({
            color: 0x9fc4dd, transparent: true, opacity: 0.4,
            side: THREE.DoubleSide, depthWrite: false,
        }),
        door: new THREE.MeshLambertMaterial({
            color: toColor(def.outline, 0x6e4626), side: THREE.DoubleSide,
        }),
        stair: new THREE.MeshLambertMaterial({ color: 0xb9ad97 }),
        floor: new THREE.MeshLambertMaterial({ color: 0xb8af9d, side: THREE.DoubleSide }),
    };
}

// Exterior walls — one box per footprint edge per floor, with real gaps cut
// for exterior openings (wallIndex < 0 → footprint edge -wallIndex-1) so you
// can actually see in / out through windows and doors.
export function addExteriorWalls(out, b, mats) {
    const fp = b.footprint || [];
    const n = fp.length;
    if (n < 3) return;
    const floors = Math.max(0, b.floorCount || 0);
    const TH = 5; // exterior wall thickness
    // sill/lintel of an exterior opening use the exterior wall colour.
    const extMats = { wall: mats.ext, pane: mats.pane, door: mats.door };
    for (let fi = 0; fi < floors; fi++) {
        const floor = (b.floors || [])[fi] || {};
        const yBase = fi * FLOOR_H, yTop = yBase + FLOOR_H;
        for (let i = 0; i < n; i++) {
            const P1 = fp[i], P2 = fp[(i + 1) % n];
            const x1 = P1.x, y1 = P1.y, x2 = P2.x, y2 = P2.y;
            const ops = (floor.openings || [])
                .filter(o => o.wallIndex < 0 && (-o.wallIndex - 1) === i)
                .sort((a, c) => a.t - c.t);
            if (ops.length === 0) {
                const box = wallBox({ x: x1, y: y1 }, { x: x2, y: y2 }, TH, yBase, yTop, mats.ext);
                if (box) out.push(box);
                continue;
            }
            const L = Math.hypot(x2 - x1, y2 - y1) || 1;
            const pt = t => ({ x: x1 + (x2 - x1) * t, y: y1 + (y2 - y1) * t });
            let cursor = 0;
            for (const op of ops) {
                const half = (op.width || 8) / 2 / L;
                const o0 = Math.max(0, op.t - half), o1 = Math.min(1, op.t + half);
                if (o0 > cursor) {
                    const A = pt(cursor), B = pt(o0);
                    const box = wallBox({ x: A.x, y: A.y }, { x: B.x, y: B.y }, TH, yBase, yTop, mats.ext);
                    if (box) out.push(box);
                }
                cursor = Math.max(cursor, o1);
                addOpening(out, { x: x1, y: y1 }, { x: x2, y: y2 }, op, { thickness: TH, yBase: yBase, wallH: FLOOR_H }, extMats);
            }
            if (cursor < 1) {
                const A = pt(cursor), B = pt(1);
                const box = wallBox({ x: A.x, y: A.y }, { x: B.x, y: B.y }, TH, yBase, yTop, mats.ext);
                if (box) out.push(box);
            }
        }
    }
}

// Interior wall: solid spans between its openings + the openings' gap content.
export function addInteriorWall(out, w, openings, yBase, mats) {
    const x1 = w.x1, y1 = w.y1, x2 = w.x2, y2 = w.y2;
    const th = w.thickness || 3;
    const L = Math.hypot(x2 - x1, y2 - y1) || 1;
    const pt = t => ({ x: x1 + (x2 - x1) * t, y: y1 + (y2 - y1) * t });
    const ops = openings.slice().sort((a, b) => a.t - b.t);
    let cursor = 0;
    function solid(a, b) {
        if (b - a <= 1e-4) return;
        const A = pt(a), B = pt(b);
        const box = wallBox({ x: A.x, y: A.y }, { x: B.x, y: B.y }, th, yBase + IWALL_H * 0.022, yBase + IWALL_H + IWALL_H * 0.02, mats.wall);
        if (box) out.push(box);
    }
    for (const op of ops) {
        const half = (op.width || 8) / 2 / L;
        const o0 = Math.max(0, op.t - half), o1 = Math.min(1, op.t + half);
        if (o0 > cursor) solid(cursor, o0);
        cursor = Math.max(cursor, o1);
        addOpening(out, { x: x1, y: y1 }, { x: x2, y: y2 }, op,
            { thickness: th, yBase: yBase + IWALL_H * 0.022 }, mats);
    }
    if (cursor < 1) solid(cursor, 1);
}

// A staircase: solid stepped mass anchored to the floor, spanning one floor
// height ('up' rises above yBase, 'down' descends below it). Pushes step
// boxes into `out`.
export function buildStair(out, st, yBase, mat) {
    const rot = (st.rotation || 0) * DEG;
    const len = st.length || 18, wid = st.width || 10;
    // 2D stair rect long axis = local +Y; rotated by `rot`.
    const rdx = -Math.sin(rot), rdy = Math.cos(rot);
    const sx = st.x - rdx * len / 2, sy = st.y - rdy * len / 2;
    const N = 9, up = st.direction === 'down' ? -1 : 1;
    for (let s = 0; s < N; s++) {
        const frac = (s + 0.5) / N;
        const cx = sx + rdx * len * frac, cy = sy + rdy * len * frac;
        const h = FLOOR_H * (s + 1) / N; // each step solid up to its tread
        const box = new THREE.Mesh(
            new THREE.BoxGeometry(wid, h, len / N), mat);
        box.position.set(cx, yBase + up * h / 2, cy);
        box.rotation.y = Math.atan2(rdx, rdy);
        box.renderOrder = RO_BUILDING;
        out.push(box);
    }
}

// Camera-facing text label (canvas texture sprite). Just the text — a
// subtle light outline gives legibility on any background, no opaque box.
// `fontSize` (the 2D label's size, world units) drives the world height.
export function makeLabelSprite(text, hexColor, fontSize) {
    text = text || '';
    const fontPx = 128; // high-res render → crisp when scaled in 3D
    const measure = document.createElement('canvas').getContext('2d');
    measure.font = fontPx + 'px "Segoe UI", sans-serif';
    const tw = Math.max(8, measure.measureText(text).width);
    const pad = fontPx * 0.3;
    const cv = document.createElement('canvas');
    cv.width = Math.ceil(tw + pad * 2);
    cv.height = Math.ceil(fontPx + pad * 2);
    const ctx = cv.getContext('2d');
    ctx.font = fontPx + 'px "Segoe UI", sans-serif';
    ctx.textBaseline = 'middle';
    ctx.lineJoin = 'round';
    ctx.lineWidth = fontPx * 0.16;
    ctx.strokeStyle = 'rgba(0,0,0,0.9)';
    ctx.strokeText(text, pad, cv.height / 2);
    ctx.fillStyle = hexColor || '#1c1a18';
    ctx.fillText(text, pad, cv.height / 2);
    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.minFilter = THREE.LinearFilter; // no mip blur
    tex.generateMipmaps = false;
    // depthTest on (walls occlude it — shows only when actually visible);
    // depthWrite off so the transparent quad doesn't block anything.
    const spr = new THREE.Sprite(new THREE.SpriteMaterial({
        map: tex, depthTest: true, depthWrite: false, transparent: true,
    }));
    const wh = (fontSize && fontSize > 0 ? fontSize : 8) * 0.5;
    spr.scale.set(wh * cv.width / cv.height, wh, 1);
    spr.renderOrder = RO_BILLBOARD;
    return spr;
}

// An upright pin marker (stem + head) + optional camera-facing label above.
export function makePinMarker(hexColor, label) {
    const g = new THREE.Group();
    const mat = new THREE.MeshLambertMaterial({ color: toColor(hexColor, 0xf9c46a) });
    const stem = new THREE.Mesh(new THREE.CylinderGeometry(0.6, 0.6, 8, 8), mat);
    stem.position.y = 4;
    const head = new THREE.Mesh(new THREE.SphereGeometry(2.4, 12, 10), mat);
    head.position.y = 9;
    g.add(stem); g.add(head);
    if (label) {
        const spr = makeLabelSprite(label, '#1c1a18');
        spr.position.y = 14;
        g.add(spr);
    }
    return g;
}

// Per-floor interiors: walls + openings + stairs + floor-scoped labels/pins.
// All floors render stacked (a 3D building shows every floor at once — the
// 2D "active floor only" rule is a 2D limitation). Visible by flying inside.
// Solid interior geometry → `out` (merged by material later); camera-facing
// labels / pins → `root` (can't be merged).
export function buildBuildingInteriors(out, root, b, mats) {
    const floors = Math.max(0, b.floorCount || 0);
    const fp = b.footprint || [];
    for (let fi = 0; fi < floors; fi++) {
        const floor = (b.floors || [])[fi] || {};
        const yBase = fi * FLOOR_H;
        // Floor slab (= ceiling of the level below), with stairwell holes
        // cut only for staircases that actually pass through THIS slab:
        // 'up' stairs on the level below, and 'down' stairs on this level.
        if (fp.length >= 3) {
            const below = (fi > 0 && (b.floors || [])[fi - 1]) || {};
            const upBelow = (below.stairs || [])
                .filter(s => (s.direction || 'up') !== 'down');
            const downHere = (floor.stairs || [])
                .filter(s => s.direction === 'down');
            const holes = upBelow.concat(downHere).map(stairRectPoly);
            const slab = polyCapMesh(fp, yBase, mats.floor, holes);
            slab.renderOrder = RO_BUILDING;
            out.push(slab);
        }
        const walls = floor.walls || [];
        walls.forEach((w, wi) => {
            const ops = (floor.openings || []).filter(o => o.wallIndex === wi);
            addInteriorWall(out, w, ops, yBase, mats);
        });
        // Openings on the outer outline (wallIndex < 0) are handled by
        // addExteriorWalls — it cuts real gaps in the exterior walls.
        (floor.stairs || []).forEach(st => buildStair(out, st, yBase, mats.stair));
        (floor.labels || []).forEach(l => {
            if (!l.text) return;
            const spr = makeLabelSprite(l.text, l.color, l.fontSize);
            spr.position.set(l.x, yBase + FLOOR_H * 0.5, l.y);
            root.add(spr);
        });
        (floor.pins || []).forEach(p => {
            const m = makePinMarker(p.color, p.label);
            m.position.set(p.x, yBase, p.y);
            root.add(m);
        });
    }
}

// Buildings → a full-footprint wall prism + a real 3D roof, plus per-floor
// interiors (walls / openings / stairs / floor-scoped labels & pins).
export function buildBuildings(root, visIds) {
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const b of n.buildings || []) {
                if (!elementShown('building', b.id, n.id, visIds)) continue;
                const fp = b.footprint || [];
                if (fp.length < 3) continue;
                const def = mapHost.buildingTypeDef(b.type);
                const mats = makeBuildingMats(def);
                const floors = Math.max(0, b.floorCount || 0);
                if (floors < 1) {
                    // playground / pad — a flat coloured surface.
                    const pad = polyCapMesh(fp, 0.3, mats.roof);
                    pad.renderOrder = RO_BUILDING;
                    root.add(pad);
                    continue;
                }
                // Exterior massing: walls go straight up the full footprint
                // for every floor — only the roof slopes. Exterior walls are
                // gapped at openings so windows / doors see through.
                const wallTop = floors * FLOOR_H;
                const bMeshes = [];
                addExteriorWalls(bMeshes, b, mats);
                bMeshes.push(polyCapMesh(fp, wallTop, mats.roof));
                bMeshes.push(buildRoof3D(b, fp, wallTop, mats.roof));
                buildBuildingInteriors(bMeshes, root, b, mats);
                // Merge this building's solid geometry by material — one draw
                // call per material instead of one per box / slab / step.
                for (const m of mergeByMaterial(bMeshes)) {
                    m.renderOrder = RO_BUILDING;
                    // Transparent geometry (window panes) must NOT cast
                    // shadows — the shadow map treats it as opaque, so the
                    // window would block all light. Let light through.
                    m.castShadow = !m.material.transparent;
                    m.receiveShadow = true;
                    root.add(m);
                }
            }
            walk(n.children);
        }
    })(mapLayers());
}
