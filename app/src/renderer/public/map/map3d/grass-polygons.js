import * as THREE from "../three.webgpu.min.js";
import { grassState } from './grass-state.js';
import { elementShown, mapLayers, toColor } from './map-content.js';
import {
    GRASS_DEBUG,
    GRASS_DENSITY,
    GRASS_FALLBACK_COLOR,
    GRASS_MAX_BLADES_PER_TILE,
    GRASS_TILE_SIZE,
} from './grass-settings.js';
import { bboxOf, forEachSplineOutline } from './vegetation-polygons.js';
import { pointInPolygon } from './geometry.js';
import {
    buildGrassBillboardMesh,
    buildGrassPool,
    generateGrassBillboardTexture,
    makeGrassMaterial,
} from './grass-geometry.js';
import { mapHost } from './host.js';

export function buildGrass(root, visIds) {
    // Fresh per scene build — old tiles were disposed via disposeTree.
    grassState.grassPolygons = [];
    grassState.grassPolyBuckets = new Map();
    grassState.grassTileCache = new Map();
    grassState.grassMaterial = null;
    grassState.grassRoot = root;
    grassState.grassPrimed = false;
    grassState.grassPool = [];
    grassState.grassFreeSlots = [];
    function pushOccluder(poly) {
        if (!poly || poly.length < 3) return;
        grassState.grassPolygons.push({
            poly, bb: bboxOf(poly),
            color: null, // unused — never spawns a blade
            isGrass: false,
        });
    }

    // 1) Terrain shapes — walked in the SAME order as buildTerrain so our
    // array index matches the 2D map's render order: later entries sit on
    // top of earlier entries. Non-grass shapes act as occluders that mask
    // off grass at sample time.
    let sawGrass = false;
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const sh of n.shapes || []) {
                if (!elementShown('shape', sh.id, n.id, visIds)) continue;
                const poly = sh.points || [];
                if (poly.length < 3) continue;
                const isGrass = sh.type === 'grass';
                if (isGrass) sawGrass = true;
                grassState.grassPolygons.push({
                    poly, bb: bboxOf(poly),
                    color: new THREE.Color(toColor(sh.color, GRASS_FALLBACK_COLOR)),
                    isGrass,
                });
            }
            walk(n.children);
        }
    })(mapLayers());

    // Nothing to do if no grass shape exists at all.
    if (!sawGrass) {
        grassState.grassPolygons = [];
        return;
    }

    // 2) Spline ribbons — rivers/lakes (RO_SPLINE) and road/trail/track
    // ribbons. All splines render ABOVE every terrain shape, so they go
    // after the shape stack as universal occluders. Open rivers use the
    // water ribbon outline; closed water bodies are the sample polygon
    // directly; roads/trails/tracks use the casing extent (w/2 + extra).
    forEachSplineOutline(visIds, pushOccluder);

    // 3) Building footprints (RO_BUILDING) — always above everything flat.
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const b of n.buildings || []) {
                if (!elementShown('building', b.id, n.id, visIds)) continue;
                pushOccluder(b.footprint || []);
            }
            walk(n.children);
        }
    })(mapLayers());
    buildGrassBuckets();

    // Clamp the effective density so that a fully-grass tile produces at
    // most MAX_BLADES_PER_TILE candidates. Without this, a high
    // GRASS_DENSITY setting CLAMPS THE FULL TILE but not partially-
    // occluded tiles, making per-m² density INCREASE with occluder
    // coverage (the opposite of intent). Derive the jitter step once.
    const maxFitDensity = GRASS_MAX_BLADES_PER_TILE
        / (GRASS_TILE_SIZE * GRASS_TILE_SIZE);
    grassState.grassEffectiveDensity = Math.min(GRASS_DENSITY, maxFitDensity);
    grassState.grassStep = 1 / Math.sqrt(grassState.grassEffectiveDensity);

    grassState.grassMaterial = makeGrassMaterial();
    buildGrassPool();
    // Billboard texture must be generated AFTER grassMaterial exists (it
    // renders blades using that material). Then the billboard mesh wraps
    // it; instance data is written per tick from updateGrassBillboards.
    grassState.grassBillboardTex = generateGrassBillboardTexture();
    if (grassState.grassBillboardTex) buildGrassBillboardMesh();
    logGrassBuild();
}


function buildGrassBuckets() {

    // 4) Precompute per-tile polygon buckets. The scene is static once
    // built, so the per-tile broad-phase + corner test that sampleGrassTile
    // used to repeat on every build is folded into one pass here.
    for (const p of grassState.grassPolygons) {
        const ix0 = Math.floor(p.bb.minX / GRASS_TILE_SIZE);
        const ix1 = Math.floor(p.bb.maxX / GRASS_TILE_SIZE);
        const iz0 = Math.floor(p.bb.minY / GRASS_TILE_SIZE);
        const iz1 = Math.floor(p.bb.maxY / GRASS_TILE_SIZE);
        for (let iz = iz0; iz <= iz1; iz++) {
            for (let ix = ix0; ix <= ix1; ix++) {
                const k = ix + ',' + iz;
                let b = grassState.grassPolyBuckets.get(k);
                if (!b) {
                    b = { polys: [], hasGrass: false, hasOccluder: false, fastGrass: null };
                    grassState.grassPolyBuckets.set(k, b);
                }
                // Polys are pushed in original render-order because the
                // outer loop iterates grassPolygons in that order — so
                // bucket.polys[last] is still the topmost in that tile.
                b.polys.push(p);
                if (p.isGrass) b.hasGrass = true; else b.hasOccluder = true;
            }
        }
    }
    // Corner test: which tiles are fully inside a single grass polygon
    // with no occluder intersecting? Those tiles sample at full speed,
    // skipping per-candidate point-in-polygon entirely.
    for (const [key, b] of grassState.grassPolyBuckets) {
        if (b.hasOccluder || !b.hasGrass) continue;
        const [ixStr, izStr] = key.split(',');
        const ix = parseInt(ixStr, 10), iz = parseInt(izStr, 10);
        const x0 = ix * GRASS_TILE_SIZE, x1 = x0 + GRASS_TILE_SIZE;
        const z0 = iz * GRASS_TILE_SIZE, z1 = z0 + GRASS_TILE_SIZE;
        for (const p of b.polys) {
            if (!p.isGrass) continue;
            if (pointInPolygon(x0, z0, p.poly)
                && pointInPolygon(x1, z0, p.poly)
                && pointInPolygon(x0, z1, p.poly)
                && pointInPolygon(x1, z1, p.poly)) {
                b.fastGrass = p;
                break;
            }
        }
    }
}


function logGrassBuild() {
    if (GRASS_DEBUG) {
        let nGrassPolys = 0, nOccluders = 0, nGrassBuckets = 0;
        for (const p of grassState.grassPolygons) {
            if (p.isGrass) nGrassPolys++; else nOccluders++;
        }
        for (const b of grassState.grassPolyBuckets.values()) {
            if (b.hasGrass) nGrassBuckets++;
        }
        mapHost.log('[grass] build complete:'
            + ' polygons=' + grassState.grassPolygons.length
            + ' grass=' + nGrassPolys
            + ' occluders=' + nOccluders
            + ' buckets=' + grassState.grassPolyBuckets.size
            + ' grassBuckets=' + nGrassBuckets
            + ' pool=' + grassState.grassPool.length
            + ' configDensity=' + GRASS_DENSITY
            + ' effectiveDensity=' + grassState.grassEffectiveDensity.toFixed(2));
    }
}
