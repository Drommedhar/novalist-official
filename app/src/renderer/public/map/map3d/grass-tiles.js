import {
    GRASS_BILLBOARD_CAPACITY,
    GRASS_BILLBOARD_DISTANCE,
    GRASS_BLADE_HEIGHT,
    GRASS_DEBUG,
    GRASS_FALLOFF_POWER,
    GRASS_MAX_BLADES_PER_TILE,
    GRASS_NEAR_RADIUS,
    GRASS_RENDER_RADIUS,
    GRASS_TICK_BUDGET_MS,
    GRASS_TILE_SIZE,
} from './grass-settings.js';
import {
    grassBillboardDummy,
    grassDebug,
    grassFrustum,
    grassPoolDummy,
    grassProjMatrix,
    grassSphere,
    grassState,
} from './grass-state.js';
import { mapHost } from './host.js';
import { sceneState } from './scene-state.js';
import { pointInPolygon } from './geometry.js';
import { evictFurthestPoolSlot } from './grass-geometry.js';

export function grassDebugReport(now) {
    if (!GRASS_DEBUG) return;
    if (grassDebug.lastReport === 0) { grassDebug.lastReport = now; return; }
    if (now - grassDebug.lastReport < 1000) return;
    let drawn = 0, cached = 0;
    for (const t of grassState.grassTileCache.values()) {
        cached++;
        if (t && t.visible && t.count > 0) drawn++;
    }
    const meanSample = grassDebug.sampleCount > 0
        ? (grassDebug.sampleMs / grassDebug.sampleCount).toFixed(2) : '0';
    const meanUpdate = grassDebug.updateCalls > 0
        ? (grassDebug.updateMs / grassDebug.updateCalls).toFixed(2) : '0';
    const meanVisLoop = grassDebug.updateCalls > 0
        ? (grassDebug.visibilityIterMs / grassDebug.updateCalls).toFixed(2) : '0';
    // Route through map.html's global log() so the line reaches the C#
    // host (Console.Error as "[MapJS] ..."). console.log alone would only
    // surface in the WebView2 DevTools, which isn't accessible here.
    const meanRender = grassDebug.renderFrames > 0
        ? (grassDebug.renderMs / grassDebug.renderFrames).toFixed(2) : '0';
    mapHost.log('[grass]'
        + ' fps=' + grassDebug.renderFrames
        + ' avgRender=' + meanRender + 'ms'
        + ' maxRender=' + grassDebug.renderMaxMs.toFixed(1) + 'ms'
        + ' avgUpdate=' + meanUpdate + 'ms'
        + ' maxUpdate=' + grassDebug.updateMaxMs.toFixed(1) + 'ms'
        + ' avgVisLoop=' + meanVisLoop + 'ms'
        + ' newTiles=' + grassDebug.newTiles
        + ' sampled=' + grassDebug.sampleCount
        + ' (fast=' + grassDebug.sampleFast + ')'
        + ' avgSample=' + meanSample + 'ms'
        + ' cached=' + cached
        + ' drawn=' + drawn
        + ' camXZ=(' + sceneState.camera.position.x.toFixed(0) + ',' + sceneState.camera.position.z.toFixed(0) + ')'
        + ' poolFree=' + grassState.grassFreeSlots.length
        + ' evictions=' + grassDebug.evictions
        + ' buckets=' + grassState.grassPolyBuckets.size
    );
    grassDebug.lastReport = now;
    grassDebug.updateMs = 0;
    grassDebug.updateCalls = 0;
    grassDebug.sampleMs = 0;
    grassDebug.sampleCount = 0;
    grassDebug.sampleFast = 0;
    grassDebug.newTiles = 0;
    grassDebug.visibilityIterMs = 0;
    grassDebug.renderMs = 0;
    grassDebug.renderFrames = 0;
    grassDebug.renderMaxCalls = 0;
    grassDebug.renderMaxMs = 0;
    grassDebug.updateMaxMs = 0;
    grassDebug.evictions = 0;
}

// Sample one tile in-place. Uses the precomputed per-tile bucket
// (polygons that intersect, hasGrass/hasOccluder flags, fastGrass for
// tiles fully inside one grass polygon). Returns the InstancedMesh, or
// null if no blades landed.
export function sampleGrassTile(ix, iz) {
    const tSample0 = GRASS_DEBUG ? performance.now() : 0;
    const bucket = grassState.grassPolyBuckets.get(ix + ',' + iz);
    if (!bucket || !bucket.hasGrass) return null;
    const x0 = ix * GRASS_TILE_SIZE, x1 = x0 + GRASS_TILE_SIZE;
    const z0 = iz * GRASS_TILE_SIZE, z1 = z0 + GRASS_TILE_SIZE;
    const polysIn = bucket.polys;
    const fastGrass = bucket.fastGrass;
    if (GRASS_DEBUG) {
        grassDebug.sampleCount++;
        if (fastGrass) grassDebug.sampleFast++;
    }

    // Use the SCENE-CLAMPED step so per-m² density stays uniform across
    // tiles regardless of occluder coverage.
    const step = grassState.grassStep;
    // Collect blades as objects so we can shuffle them into a random order
    // before flattening to typed arrays. Random order matters because
    // updateGrassVisibility uses inst.count = totalBlades × density(dist)
    // to thin tiles by distance — taking the first N of a SHUFFLED array
    // is an unbiased subsample (taking the first N of row-major-sampled
    // blades would draw only the "bottom-left strip" of the tile).
    const cands = [];
    for (let y = z0; y < z1; y += step) {
        for (let x = x0; x < x1; x += step) {
            const jx = x + (Math.random() - 0.5) * step;
            const jy = y + (Math.random() - 0.5) * step;
            let hit;
            if (fastGrass) {
                hit = fastGrass;
            } else {
                hit = null;
                // Top-down: highest index = topmost render order.
                for (let i = polysIn.length - 1; i >= 0; i--) {
                    const p = polysIn[i];
                    if (pointInPolygon(jx, jy, p.poly)) { hit = p; break; }
                }
                if (!hit || !hit.isGrass) continue;
            }
            cands.push({
                jx, jy,
                tr: hit.color.r, tg: hit.color.g, tb: hit.color.b,
                phase: Math.random() * Math.PI * 2,
                yaw: Math.random() * Math.PI * 2,
                scale: 0.7 + Math.random() * 0.6,
            });
        }
    }
    const n = cands.length;
    if (n === 0) {
        if (GRASS_DEBUG) grassDebug.sampleMs += performance.now() - tSample0;
        return null;
    }
    // Fisher–Yates.
    for (let i = n - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        const tmp = cands[i]; cands[i] = cands[j]; cands[j] = tmp;
    }

    // Cap to pool buffer capacity. If a tile would have more blades than
    // we can fit, drop the tail (cands is already shuffled, so dropping
    // the tail is an unbiased subsample).
    const cap = Math.min(cands.length, GRASS_MAX_BLADES_PER_TILE);

    // Acquire a pool slot. Free stack first; if empty, evict the slot
    // bound to the furthest tile from the camera.
    let slotIdx = grassState.grassFreeSlots.length > 0 ? grassState.grassFreeSlots.pop() : evictFurthestPoolSlot();
    if (slotIdx < 0) {
        if (GRASS_DEBUG) grassDebug.sampleMs += performance.now() - tSample0;
        return null;
    }
    const entry = grassState.grassPool[slotIdx];
    writeGrassPoolEntry(entry, cands, cap);
    bindGrassTile(entry, { ix, iz, cap, polysIn });
    if (GRASS_DEBUG) grassDebug.sampleMs += performance.now() - tSample0;
    return entry.mesh;
}

// Distance-based density LOD. Returns the fraction of a tile's blades to
// actually draw given the distance from camera to tile center.
// - dist ≤ NEAR_RADIUS → 1 (full density)
// - dist ≥ RENDER_RADIUS → 0 (cut)
// - in between → quadratic falloff (steeper than linear so far tiles
//   shed blades quickly while the near zone keeps its visual mass)
export function grassDensityFraction(dist) {
    if (dist <= GRASS_NEAR_RADIUS) return 1;
    if (dist >= GRASS_RENDER_RADIUS) return 0;
    const t = (GRASS_RENDER_RADIUS - dist)
        / (GRASS_RENDER_RADIUS - GRASS_NEAR_RADIUS);
    return Math.pow(t, GRASS_FALLOFF_POWER);
}

// Each tick: determine the set of tiles within the render radius around
// the camera. Sample any missing ones (with a per-frame cap after the
// first call, which primes everything synchronously to avoid an empty
// first frame), and for each visible tile set inst.count from its
// distance-derived density fraction so distant tiles draw fewer blades.
export function updateGrassVisibility() {
    if (grassState.grassPolygons.length === 0) return;
    const tUpdate0 = GRASS_DEBUG ? performance.now() : 0;
    const cx = sceneState.camera.position.x, cz = sceneState.camera.position.z;
    const ctx = Math.floor(cx / GRASS_TILE_SIZE);
    const ctz = Math.floor(cz / GRASS_TILE_SIZE);
    const radTiles = Math.ceil(GRASS_RENDER_RADIUS / GRASS_TILE_SIZE);
    const half = GRASS_TILE_SIZE * 0.5 * Math.SQRT2;
    const cullR = GRASS_RENDER_RADIUS + half;
    const cullR2 = cullR * cullR;

    const needed = new Map(); // key → squared distance
    for (let dz = -radTiles; dz <= radTiles; dz++) {
        for (let dx = -radTiles; dx <= radTiles; dx++) {
            const ix = ctx + dx, iz = ctz + dz;
            const tcx = (ix + 0.5) * GRASS_TILE_SIZE;
            const tcz = (iz + 0.5) * GRASS_TILE_SIZE;
            const ddx = tcx - cx, ddz = tcz - cz;
            const d2 = ddx * ddx + ddz * ddz;
            if (d2 > cullR2) continue;
            needed.set(ix + ',' + iz, d2);
        }
    }

    // Prime tick: build everything synchronously (one entry hitch). Later
    // ticks: stop once the per-frame time budget is exhausted so movement
    // can never trigger a multi-tile sampling spike. Order tiles by
    // distance ascending so the closest missing tiles get built first —
    // matters when moving fast: you see the in-front tiles fill before the
    // ones you're leaving behind.
    const t0 = performance.now();
    const budget = grassState.grassPrimed ? GRASS_TICK_BUDGET_MS : Infinity;
    const sorted = Array.from(needed.entries())
        .filter(([k]) => !grassState.grassTileCache.has(k))
        .sort((a, b) => a[1] - b[1]);
    for (const [key] of sorted) {
        if (performance.now() - t0 >= budget) break;
        const [ixStr, izStr] = key.split(',');
        const tile = sampleGrassTile(parseInt(ixStr, 10), parseInt(izStr, 10));
        grassState.grassTileCache.set(key, tile); // null marks "sampled, no blades here"
        // Pool meshes are already parented at buildGrass time. sampleGrassTile
        // either bound one to this tile or returned null (no blades).
        if (tile && GRASS_DEBUG) grassDebug.newTiles++;
    }
    grassState.grassPrimed = true;

    // Build frustum once per tick from the camera's current view-proj.
    grassProjMatrix.multiplyMatrices(
        sceneState.camera.projectionMatrix, sceneState.camera.matrixWorldInverse);
    grassFrustum.setFromProjectionMatrix(grassProjMatrix);
    const tileHalfDiag = Math.hypot(
        GRASS_TILE_SIZE * 0.5, GRASS_TILE_SIZE * 0.5,
        GRASS_BLADE_HEIGHT * 0.5);

    const tVis0 = GRASS_DEBUG ? performance.now() : 0;
    updateGrassTiles(needed, tileHalfDiag);
    if (GRASS_DEBUG) {
        const now = performance.now();
        grassDebug.visibilityIterMs += now - tVis0;
        const updateDt = now - tUpdate0;
        grassDebug.updateMs += updateDt;
        grassDebug.updateCalls++;
        if (updateDt > grassDebug.updateMaxMs) grassDebug.updateMaxMs = updateDt;
        grassDebugReport(now);
    }
}


function writeGrassPoolEntry(entry, cands, cap) {
    const arrRoot = entry.rootsAttr.array;
    const arrTint = entry.tintsAttr.array;
    const arrPhase = entry.phasesAttr.array;
    for (let i = 0; i < cap; i++) {
        const c = cands[i];
        arrRoot[i * 2] = c.jx; arrRoot[i * 2 + 1] = c.jy;
        arrTint[i * 3] = c.tr; arrTint[i * 3 + 1] = c.tg; arrTint[i * 3 + 2] = c.tb;
        arrPhase[i] = c.phase;
    }
    // Mark sub-ranges dirty — three.js webgpu writes only the used region
    // via queue.writeBuffer, which is much cheaper than buffer creation.
    entry.rootsAttr.needsUpdate = true;
    entry.tintsAttr.needsUpdate = true;
    entry.phasesAttr.needsUpdate = true;

    for (let i = 0; i < cap; i++) {
        const c = cands[i];
        grassPoolDummy.position.set(c.jx, 0.02, c.jy);
        grassPoolDummy.rotation.set(0, c.yaw, 0);
        grassPoolDummy.scale.setScalar(c.scale);
        grassPoolDummy.updateMatrix();
        entry.mesh.setMatrixAt(i, grassPoolDummy.matrix);
    }
    entry.mesh.instanceMatrix.needsUpdate = true;
}


function bindGrassTile(entry, { ix, iz, cap, polysIn }) {

    // Update bbox / sphere so frustum culling uses the actual tile extent.
    const tileCx = (ix + 0.5) * GRASS_TILE_SIZE;
    const tileCz = (iz + 0.5) * GRASS_TILE_SIZE;
    const half = GRASS_TILE_SIZE * 0.5;
    entry.geo.boundingBox.min.set(tileCx - half, 0, tileCz - half);
    entry.geo.boundingBox.max.set(tileCx + half, GRASS_BLADE_HEIGHT, tileCz + half);
    entry.geo.boundingSphere.center.set(tileCx, GRASS_BLADE_HEIGHT * 0.5, tileCz);
    entry.geo.boundingSphere.radius = Math.hypot(half, half, GRASS_BLADE_HEIGHT * 0.5);

    entry.mesh.count = cap;
    entry.mesh.userData.totalBlades = cap;
    entry.mesh.userData.grassCenter = { x: tileCx, z: tileCz };
    // Representative grass colour for the billboard LOD — first grass
    // polygon covering the tile wins (matches what most blades would tint
    // to anyway).
    let tileColor = null;
    for (const p of polysIn) {
        if (p.isGrass) { tileColor = p.color; break; }
    }
    entry.mesh.userData.tileColor = tileColor;
    entry.mesh.visible = true;
    entry.tileKey = ix + ',' + iz;
}


function updateGrassTiles(needed, tileHalfDiag) {
    const billboardTints = grassState.grassBillboardTintAttr
        ? grassState.grassBillboardTintAttr.array : null;
    let billboardCount = 0;
    for (const [key, tile] of grassState.grassTileCache) {
        if (!tile) continue;
        const d2 = needed.get(key);
        if (d2 === undefined) {
            tile.visible = false;
            continue;
        }
        const dist = Math.sqrt(d2);
        const cc = tile.userData.grassCenter;
        grassSphere.center.set(cc.x, GRASS_BLADE_HEIGHT * 0.5, cc.z);
        grassSphere.radius = tileHalfDiag;
        const inFrustum = grassFrustum.intersectsSphere(grassSphere);

        if (dist >= GRASS_BILLBOARD_DISTANCE) {
            tile.visible = false;
            if (!inFrustum) continue;
            if (!grassState.grassBillboardMesh || billboardCount >= GRASS_BILLBOARD_CAPACITY) continue;
            grassBillboardDummy.position.set(cc.x, 0.05, cc.z);
            grassBillboardDummy.rotation.set(0, 0, 0);
            grassBillboardDummy.scale.set(1, 1, 1);
            grassBillboardDummy.updateMatrix();
            grassState.grassBillboardMesh.setMatrixAt(billboardCount, grassBillboardDummy.matrix);
            const tc = tile.userData.tileColor;
            if (tc && billboardTints) {
                billboardTints[billboardCount * 3] = tc.r;
                billboardTints[billboardCount * 3 + 1] = tc.g;
                billboardTints[billboardCount * 3 + 2] = tc.b;
            } else if (billboardTints) {
                billboardTints[billboardCount * 3] = 1;
                billboardTints[billboardCount * 3 + 1] = 1;
                billboardTints[billboardCount * 3 + 2] = 1;
            }
            billboardCount++;
        } else {
            // Blade LOD — instanced blade mesh with density falloff.
            const f = grassDensityFraction(dist);
            const drawCount = Math.ceil(tile.userData.totalBlades * f);
            if (drawCount === 0 || !inFrustum) {
                tile.visible = false;
                continue;
            }
            tile.count = drawCount;
            tile.visible = true;
        }
    }
    if (grassState.grassBillboardMesh) {
        grassState.grassBillboardMesh.count = billboardCount;
        grassState.grassBillboardMesh.instanceMatrix.needsUpdate = true;
        if (grassState.grassBillboardTintAttr) grassState.grassBillboardTintAttr.needsUpdate = true;
    }
}
