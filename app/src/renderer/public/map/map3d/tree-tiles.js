import * as THREE from "../three.webgpu.min.js";
import { treePoolDummy, treeState } from './tree-state.js';
import { elementShown, mapLayers } from './map-content.js';
import { bboxOf, forEachSplineOutline } from './vegetation-polygons.js';
import {
    TREE_BILLBOARD_CAPACITY,
    TREE_BILLBOARD_DISTANCE,
    TREE_DENSITY,
    TREE_FALLOFF_POWER,
    TREE_FOLIAGE_HEIGHT,
    TREE_MAX_PER_TILE,
    TREE_NEAR_RADIUS,
    TREE_RENDER_RADIUS,
    TREE_STATIC_MODE,
    TREE_TICK_BUDGET_MS,
    TREE_TILE_SIZE,
    TREE_TRUNK_HEIGHT,
} from './tree-settings.js';
import {
    buildStaticTreeMeshes,
    buildTreeBillboardMesh,
    buildTreePool,
    evictFurthestTreeSlot,
    generateTreeBillboardTexture,
    makeTreeMaterial,
} from './tree-geometry.js';
import { makeTreeMaterialsFromGlb } from './tree-assets.js';
import { pointInPolygon } from './geometry.js';
import { sceneState } from './scene-state.js';
import { grassFrustum, grassSphere } from './grass-state.js';

export function buildTrees(root, visIds) {
    treeState.treePolygons = [];
    treeState.treePolyBuckets = new Map();
    treeState.treeTileCache = new Map();
    treeState.treeMaterial = null;
    treeState.treePrimed = false;
    treeState.treePool = [];
    treeState.treeFreeSlots = [];
    function pushOccluder(poly) {
        if (!poly || poly.length < 3) return;
        treeState.treePolygons.push({
            poly, bb: bboxOf(poly),
            color: null, isForest: false,
        });
    }

    // 1) Terrain shapes — collect forest shapes + every shape as occluder
    // (a sand or road shape painted on a forest carves out the trees).
    let sawForest = false;
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const sh of n.shapes || []) {
                if (!elementShown('shape', sh.id, n.id, visIds)) continue;
                const poly = sh.points || [];
                if (poly.length < 3) continue;
                const isForest = sh.type === 'forest';
                if (isForest) sawForest = true;
                treeState.treePolygons.push({
                    poly, bb: bboxOf(poly),
                    color: new THREE.Color(0x4a6b30),
                    isForest,
                });
            }
            walk(n.children);
        }
    })(mapLayers());

    if (!sawForest) {
        treeState.treePolygons = [];
        return;
    }

    // 2) Spline ribbons (roads + water) — sit on top of every shape, mask
    // trees the same way they mask grass.
    forEachSplineOutline(visIds, pushOccluder);

    // 3) Building footprints.
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const b of n.buildings || []) {
                if (!elementShown('building', b.id, n.id, visIds)) continue;
                pushOccluder(b.footprint || []);
            }
            walk(n.children);
        }
    })(mapLayers());
    buildTreeBuckets();

    treeState.treeStep = 1 / Math.sqrt(TREE_DENSITY);
    treeState.treeMaterial = makeTreeMaterial(); // legacy / fallback
    if (treeState.treeAssetsLoaded) makeTreeMaterialsFromGlb();

    if (TREE_STATIC_MODE) {
        buildStaticTreeMeshes();
        return;
    }

    buildTreePool();
    treeState.treeBillboardTex = generateTreeBillboardTexture();
    if (treeState.treeBillboardTex) buildTreeBillboardMesh();
}

export function sampleTreeTile(ix, iz) {
    const bucket = treeState.treePolyBuckets.get(ix + ',' + iz);
    if (!bucket || !bucket.hasForest) return null;
    const x0 = ix * TREE_TILE_SIZE, x1 = x0 + TREE_TILE_SIZE;
    const z0 = iz * TREE_TILE_SIZE, z1 = z0 + TREE_TILE_SIZE;
    const polysIn = bucket.polys;
    const cands = [];
    for (let y = z0; y < z1; y += treeState.treeStep) {
        for (let x = x0; x < x1; x += treeState.treeStep) {
            const jx = x + (Math.random() - 0.5) * treeState.treeStep;
            const jy = y + (Math.random() - 0.5) * treeState.treeStep;
            let hit = null;
            for (let i = polysIn.length - 1; i >= 0; i--) {
                const p = polysIn[i];
                if (pointInPolygon(jx, jy, p.poly)) { hit = p; break; }
            }
            if (!hit || !hit.isForest) continue;
            cands.push({
                jx, jy,
                yaw: Math.random() * Math.PI * 2,
                scale: 0.85 + Math.random() * 0.45,
            });
        }
    }
    if (cands.length === 0) return null;
    // Shuffle for unbiased subsample.
    for (let i = cands.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        const tmp = cands[i]; cands[i] = cands[j]; cands[j] = tmp;
    }
    const n = Math.min(cands.length, TREE_MAX_PER_TILE);

    let slotIdx = treeState.treeFreeSlots.length > 0
        ? treeState.treeFreeSlots.pop()
        : evictFurthestTreeSlot();
    if (slotIdx < 0) return null;
    const entry = treeState.treePool[slotIdx];
    // Same instance matrices written to every mesh in the slot (bark +
    // canopy share placement). treeRefScale normalises the GLB's
    // authored size to roughly the same height as our procedural fallback.
    for (let i = 0; i < n; i++) {
        const c = cands[i];
        treePoolDummy.position.set(c.jx, 0, c.jy);
        treePoolDummy.rotation.set(0, c.yaw, 0);
        treePoolDummy.scale.setScalar(c.scale * treeState.treeRefScale);
        treePoolDummy.updateMatrix();
        for (const m of entry.meshes) m.setMatrixAt(i, treePoolDummy.matrix);
    }
    for (const m of entry.meshes) {
        m.instanceMatrix.needsUpdate = true;
        m.count = n;
    }
    entry.primary.userData.totalTrees = n;
    entry.primary.userData.treeCenter = {
        x: (ix + 0.5) * TREE_TILE_SIZE,
        z: (iz + 0.5) * TREE_TILE_SIZE,
    };
    entry.primary.userData.treePositions = cands.slice(0, n)
        .map(c => ({ x: c.jx, z: c.jy, scale: c.scale }));
    entry.primary.userData.poolEntry = entry;
    for (const m of entry.meshes) m.visible = true;
    entry.tileKey = ix + ',' + iz;
    // Return primary so the cache + visibility loop have a single handle.
    return entry.primary;
}

export function treeDensityFraction(dist) {
    if (dist <= TREE_NEAR_RADIUS) return 1;
    if (dist >= TREE_RENDER_RADIUS) return 0;
    const t = (TREE_RENDER_RADIUS - dist)
        / (TREE_RENDER_RADIUS - TREE_NEAR_RADIUS);
    return Math.pow(t, TREE_FALLOFF_POWER);
}

export function updateTreeVisibility() {
    if (treeState.treePolygons.length === 0) return;
    // Static mode: trees are baked into mega-meshes at buildTrees, no
    // per-frame visibility / pool work needed.
    if (TREE_STATIC_MODE) return;
    const cx = sceneState.camera.position.x, cz = sceneState.camera.position.z;
    const ctx = Math.floor(cx / TREE_TILE_SIZE);
    const ctz = Math.floor(cz / TREE_TILE_SIZE);
    const radTiles = Math.ceil(TREE_RENDER_RADIUS / TREE_TILE_SIZE);
    const half = TREE_TILE_SIZE * 0.5 * Math.SQRT2;
    const cullR = TREE_RENDER_RADIUS + half;
    const cullR2 = cullR * cullR;

    const needed = new Map();
    for (let dz = -radTiles; dz <= radTiles; dz++) {
        for (let dx = -radTiles; dx <= radTiles; dx++) {
            const ix = ctx + dx, iz = ctz + dz;
            const tcx = (ix + 0.5) * TREE_TILE_SIZE;
            const tcz = (iz + 0.5) * TREE_TILE_SIZE;
            const ddx = tcx - cx, ddz = tcz - cz;
            const d2 = ddx * ddx + ddz * ddz;
            if (d2 > cullR2) continue;
            needed.set(ix + ',' + iz, d2);
        }
    }

    const t0 = performance.now();
    const budget = treeState.treePrimed ? TREE_TICK_BUDGET_MS : Infinity;
    const sorted = Array.from(needed.entries())
        .filter(([k]) => !treeState.treeTileCache.has(k))
        .sort((a, b) => a[1] - b[1]);
    for (const [key] of sorted) {
        if (performance.now() - t0 >= budget) break;
        const [ixStr, izStr] = key.split(',');
        const tile = sampleTreeTile(parseInt(ixStr, 10), parseInt(izStr, 10));
        treeState.treeTileCache.set(key, tile);
    }
    treeState.treePrimed = true;
    updateTreeTiles(needed);
}


function buildTreeBuckets() {

    // Bucket per tile.
    for (const p of treeState.treePolygons) {
        const ix0 = Math.floor(p.bb.minX / TREE_TILE_SIZE);
        const ix1 = Math.floor(p.bb.maxX / TREE_TILE_SIZE);
        const iz0 = Math.floor(p.bb.minY / TREE_TILE_SIZE);
        const iz1 = Math.floor(p.bb.maxY / TREE_TILE_SIZE);
        for (let iz = iz0; iz <= iz1; iz++) {
            for (let ix = ix0; ix <= ix1; ix++) {
                const k = ix + ',' + iz;
                let b = treeState.treePolyBuckets.get(k);
                if (!b) {
                    b = { polys: [], hasForest: false, hasOccluder: false };
                    treeState.treePolyBuckets.set(k, b);
                }
                b.polys.push(p);
                if (p.isForest) b.hasForest = true; else b.hasOccluder = true;
            }
        }
    }
}


function updateTreeTiles(needed) {

    // Per-cached-tile: mesh visibility + billboard population.
    const tileHalfDiag = Math.hypot(
        TREE_TILE_SIZE * 0.5, TREE_TILE_SIZE * 0.5,
        (TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT) * 0.5);
    const billboardAnchors = treeState.treeBillboardAnchorAttr
        ? treeState.treeBillboardAnchorAttr.array : null;
    const billboardTints = treeState.treeBillboardTintAttr
        ? treeState.treeBillboardTintAttr.array : null;
    let billboardCount = 0;
    for (const [key, tile] of treeState.treeTileCache) {
        if (!tile) continue;
        const entry = tile.userData.poolEntry;
        const setMeshes = (visible, count) => {
            if (!entry) { tile.visible = visible; tile.count = count; return; }
            for (const m of entry.meshes) {
                m.visible = visible;
                if (count !== undefined) m.count = count;
            }
        };
        const d2 = needed.get(key);
        if (d2 === undefined) {
            setMeshes(false);
            continue;
        }
        const dist = Math.sqrt(d2);
        const cc = tile.userData.treeCenter;
        grassSphere.center.set(cc.x,
            (TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT) * 0.5, cc.z);
        grassSphere.radius = tileHalfDiag;
        const inFrustum = grassFrustum.intersectsSphere(grassSphere);

        if (dist >= TREE_BILLBOARD_DISTANCE) {
            setMeshes(false);
            if (!inFrustum) continue;
            if (!treeState.treeBillboardMesh) continue;
            const positions = tile.userData.treePositions || [];
            for (const pos of positions) {
                if (billboardCount >= TREE_BILLBOARD_CAPACITY) break;
                if (billboardAnchors) {
                    billboardAnchors[billboardCount * 3] = pos.x;
                    billboardAnchors[billboardCount * 3 + 1] = 0;
                    billboardAnchors[billboardCount * 3 + 2] = pos.z;
                }
                if (billboardTints) {
                    billboardTints[billboardCount * 3] = 1;
                    billboardTints[billboardCount * 3 + 1] = 1;
                    billboardTints[billboardCount * 3 + 2] = 1;
                }
                billboardCount++;
            }
        } else {
            const f = treeDensityFraction(dist);
            const drawCount = Math.ceil(tile.userData.totalTrees * f);
            if (drawCount === 0 || !inFrustum) {
                setMeshes(false);
                continue;
            }
            setMeshes(true, drawCount);
        }
    }
    if (treeState.treeBillboardMesh) {
        treeState.treeBillboardMesh.count = billboardCount;
        // The anchor / tint / size attributes are static-write each tick;
        // mark them dirty so three.js uploads.
        if (treeState.treeBillboardAnchorAttr) treeState.treeBillboardAnchorAttr.needsUpdate = true;
        if (treeState.treeBillboardTintAttr) treeState.treeBillboardTintAttr.needsUpdate = true;
    }
}
