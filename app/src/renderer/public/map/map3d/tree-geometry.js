import * as THREE from "../three.webgpu.min.js";
import {
    TREE_BILLBOARD_CAPACITY,
    TREE_BILLBOARD_TEX_RES,
    TREE_DENSITY,
    TREE_FOLIAGE_COLOR,
    TREE_FOLIAGE_HEIGHT,
    TREE_FOLIAGE_RADIUS,
    TREE_MAX_PER_TILE,
    TREE_POOL_SIZE,
    TREE_SCALE,
    TREE_TILE_SIZE,
    TREE_TRUNK_COLOR,
    TREE_TRUNK_HEIGHT,
    TREE_TRUNK_RADIUS,
} from './tree-settings.js';
import { MeshLambertNodeMaterial } from "../three.webgpu.min.js";
import { treeState } from './tree-state.js';
import { grassState } from './grass-state.js';
import { sceneState } from './scene-state.js';
import { renderBillboardTexture } from './billboard-rendering.js';
import { Fn, attribute, cameraPosition, positionLocal, texture, uv, vec3 } from "../three.tsl.min.js";
import { mapHost } from './host.js';
import { pointInPolygon } from './geometry.js';

// Procedural trees (forest shapes). Mirrors the grass pool / bucket / LOD
// architecture: a tile-grid InstancedMesh pool for close trees, a shared
// camera-facing billboard mesh for the far ring.

// Build the merged trunk + foliage geometry as a single BufferGeometry
// with a per-vertex `color` attribute. One geometry shared by every pool
// tile mesh (instance buffers carry tile placement).
// Layered conifer: trunk cylinder + N stacked, overlapping cones that
// narrow as they go up — gives a pine silhouette instead of one peak.
// The cones overlap so the layers read as drooping branches rather than a
// smooth gradient. Top cone is slightly darker for shading depth.
export function makeTreeGeometry() {
    const segs = 8;
    const trunkH = TREE_TRUNK_HEIGHT;
    const trunkR = TREE_TRUNK_RADIUS;
    const foliageH = TREE_FOLIAGE_HEIGHT;
    const foliageR = TREE_FOLIAGE_RADIUS;

    // Trunk: tapered cylinder (thinner at top).
    const trunk = new THREE.CylinderGeometry(trunkR * 0.7, trunkR, trunkH, segs);
    trunk.translate(0, trunkH * 0.5, 0);

    const parts = [{ geo: trunk, color: TREE_TRUNK_COLOR }];

    // Foliage layers — start a bit below the trunk top so the lowest
    // skirt drapes over the trunk like a fir.
    const layers = 5;
    const baseY = trunkH * 0.6;
    const topY = trunkH + foliageH;
    const totalH = topY - baseY;
    const layerH = totalH * 0.4; // overlap (sum > totalH)
    const layerLight = new THREE.Color(TREE_FOLIAGE_COLOR);
    const layerDark = new THREE.Color(0x274b1a);
    for (let i = 0; i < layers; i++) {
        const t = i / (layers - 1); // 0..1 bottom→top
        const radius = foliageR * (1 - t * 0.78);
        const h = layerH * (1 - t * 0.15);
        const yBase = baseY + (totalH - h) * t;
        const cone = new THREE.ConeGeometry(radius, h, segs);
        cone.translate(0, yBase + h * 0.5, 0);
        const c = new THREE.Color().copy(layerDark).lerp(layerLight, 1 - t);
        parts.push({ geo: cone, color: c.getHex() });
    }

    // Paint per part, then concat into one BufferGeometry.
    function paint(g, colorHex) {
        const c = new THREE.Color(colorHex);
        const n = g.attributes.position.count;
        const arr = new Float32Array(n * 3);
        for (let i = 0; i < n; i++) {
            arr[i * 3] = c.r;
            arr[i * 3 + 1] = c.g;
            arr[i * 3 + 2] = c.b;
        }
        g.setAttribute('color', new THREE.BufferAttribute(arr, 3));
    }
    for (const p of parts) paint(p.geo, p.color);

    let totalVerts = 0, totalIdx = 0;
    for (const p of parts) {
        totalVerts += p.geo.attributes.position.count;
        totalIdx += p.geo.index.count;
    }
    const positions = new Float32Array(totalVerts * 3);
    const normals = new Float32Array(totalVerts * 3);
    const colors = new Float32Array(totalVerts * 3);
    const indices = new Uint16Array(totalIdx);
    let vOff = 0, iOff = 0;
    for (const p of parts) {
        const g = p.geo;
        const vc = g.attributes.position.count;
        positions.set(g.attributes.position.array, vOff * 3);
        normals.set(g.attributes.normal.array, vOff * 3);
        colors.set(g.attributes.color.array, vOff * 3);
        const src = g.index.array;
        for (let k = 0; k < src.length; k++) indices[iOff + k] = src[k] + vOff;
        vOff += vc;
        iOff += src.length;
    }
    const merged = new THREE.BufferGeometry();
    merged.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    merged.setAttribute('normal', new THREE.BufferAttribute(normals, 3));
    merged.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    merged.setIndex(new THREE.BufferAttribute(indices, 1));
    for (const p of parts) p.geo.dispose();
    return merged;
}

// Lambert material using the per-vertex color (trunk vs foliage). Per-
// instance tint allows shape-colour variation later if we want it.
export function makeTreeMaterial() {
    const mat = new MeshLambertNodeMaterial({
        side: THREE.DoubleSide,
        vertexColors: true,
    });
    return mat;
}

// Two InstancedMesh per pool slot: one for bark, one for canopy. Both
// share the same instance matrices (one tree = one matrix applied to both
// meshes). updateTreeVisibility writes the same count / visible to both.
export function buildTreePool() {
    treeState.treePool = [];
    treeState.treeFreeSlots = [];
    // If GLB load failed for any reason, fall back to the procedural
    // layered-cone mesh so trees still render.
    const useGlb = treeState.treeAssetsLoaded && treeState.treeBarkGeo && treeState.treeCanopyGeo;
    if (!useGlb && !treeState.treeTileGeometry) treeState.treeTileGeometry = makeTreeGeometry();

    const bbMin = new THREE.Vector3(-3, 0, -3);
    const bbMax = new THREE.Vector3(3, 12, 3);

    for (let i = 0; i < TREE_POOL_SIZE; i++) {
        const meshes = [];
        if (useGlb) {
            const barkMesh = new THREE.InstancedMesh(
                treeState.treeBarkGeo, treeState.treeBarkMaterial, TREE_MAX_PER_TILE);
            const canopyMesh = new THREE.InstancedMesh(
                treeState.treeCanopyGeo, treeState.treeCanopyMaterial, TREE_MAX_PER_TILE);
            meshes.push(barkMesh, canopyMesh);
        } else {
            const geo = treeState.treeTileGeometry.clone();
            geo.boundingBox = new THREE.Box3(bbMin.clone(), bbMax.clone());
            geo.boundingSphere = new THREE.Sphere(
                new THREE.Vector3(0, 6, 0), 8);
            meshes.push(new THREE.InstancedMesh(
                geo, treeState.treeMaterial, TREE_MAX_PER_TILE));
        }
        for (const m of meshes) {
            m.frustumCulled = false;
            m.castShadow = false;
            m.receiveShadow = false;
            m.matrixAutoUpdate = false;
            m.matrixWorldAutoUpdate = false;
            m.visible = false;
            m.count = 0;
            m.updateMatrix();
            m.updateMatrixWorld(true);
            grassState.grassRoot.add(m);
        }
        // First mesh acts as the "primary" — userData lives on it so the
        // visibility loop has a single source for totalTrees / center /
        // treePositions.
        const primary = meshes[0];
        primary.userData.totalTrees = 0;
        primary.userData.treeCenter = { x: 0, z: 0 };
        primary.userData.treePositions = [];
        treeState.treePool.push({ meshes, primary, tileKey: null });
        treeState.treeFreeSlots.push(i);
    }
}

export function evictFurthestTreeSlot() {
    const cx = sceneState.camera.position.x, cz = sceneState.camera.position.z;
    let furthestIdx = -1, furthestD2 = -1;
    for (let i = 0; i < treeState.treePool.length; i++) {
        const e = treeState.treePool[i];
        if (e.tileKey === null) return i;
        const c = e.primary.userData.treeCenter;
        const dx = c.x - cx, dz = c.z - cz;
        const d2 = dx * dx + dz * dz;
        if (d2 > furthestD2) { furthestD2 = d2; furthestIdx = i; }
    }
    if (furthestIdx < 0) return -1;
    const ev = treeState.treePool[furthestIdx];
    if (ev.tileKey !== null) {
        treeState.treeTileCache.delete(ev.tileKey);
        ev.tileKey = null;
    }
    for (const m of ev.meshes) { m.visible = false; m.count = 0; }
    return furthestIdx;
}

export function generateTreeBillboardTexture() {
    if (!sceneState.renderer || !treeState.treeMaterial || !treeState.treeTileGeometry) return null;
    const W = TREE_BILLBOARD_TEX_RES, H = TREE_BILLBOARD_TEX_RES;

    const subScene = new THREE.Scene();
    subScene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const sun = new THREE.DirectionalLight(0xffffcc, 0.85);
    sun.position.set(0.4, 1, 0.3);
    subScene.add(sun);

    const mesh = new THREE.Mesh(treeState.treeTileGeometry, treeState.treeMaterial);
    subScene.add(mesh);

    const halfW = TREE_FOLIAGE_RADIUS * 1.2;
    const totalH = TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT;
    // Camera looks at the tree from the +Z side at ground level so the
    // texture frames a standing silhouette (alpha=0 around it).
    const cam = new THREE.OrthographicCamera(
        -halfW, halfW, totalH, 0, 0.1, 50);
    cam.position.set(0, totalH * 0.5, 10);
    cam.lookAt(0, totalH * 0.5, 0);
    cam.updateMatrixWorld(true);

    const rt = new THREE.RenderTarget(W, H, {
        type: THREE.UnsignedByteType,
        format: THREE.RGBAFormat,
        depthBuffer: true,
    });
    rt.texture.colorSpace = THREE.SRGBColorSpace;
    rt.texture.wrapS = THREE.ClampToEdgeWrapping;
    rt.texture.wrapT = THREE.ClampToEdgeWrapping;

    renderBillboardTexture(rt, subScene, cam, 'tree');
    return rt.texture;
}

// Far-LOD billboard mesh. Each tree becomes a STANDING camera-facing
// quad: in the vertex shader we build a right-vector perpendicular to
// (anchor → camera) on the XZ plane and emit the quad's local x along it,
// keeping the trunk straight up. Per-instance anchor + size carried as
// instance attributes; instanceMatrix is identity.
export function buildTreeBillboardMesh() {
    if (treeState.treeBillboardMesh) {
        grassState.grassRoot.remove(treeState.treeBillboardMesh);
        try { treeState.treeBillboardMesh.geometry.dispose(); } catch (error) { console.warn("map3d: buildTreeBillboardMesh failed", error instanceof Error ? error.name : typeof error); }
        try { treeState.treeBillboardMesh.material.dispose(); } catch (error) { console.warn("map3d: buildTreeBillboardMesh failed", error instanceof Error ? error.name : typeof error); }
    }
    // Quad: local x in [-0.5..0.5], y in [0..1]. Single tri pair.
    const geo = new THREE.BufferGeometry();
    const positions = new Float32Array([
        -0.5, 0, 0,   0.5, 0, 0,   0.5, 1, 0,
        -0.5, 0, 0,   0.5, 1, 0,  -0.5, 1, 0,
    ]);
    const uvs = new Float32Array([
        0, 0,  1, 0,  1, 1,
        0, 0,  1, 1,  0, 1,
    ]);
    geo.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geo.setAttribute('uv', new THREE.BufferAttribute(uvs, 2));

    treeState.treeBillboardAnchorAttr = new THREE.InstancedBufferAttribute(
        new Float32Array(TREE_BILLBOARD_CAPACITY * 3), 3);
    treeState.treeBillboardTintAttr = new THREE.InstancedBufferAttribute(
        new Float32Array(TREE_BILLBOARD_CAPACITY * 3), 3);
    const sizeAttr = new THREE.InstancedBufferAttribute(
        new Float32Array(TREE_BILLBOARD_CAPACITY * 2), 2);
    geo.setAttribute('treeAnchor', treeState.treeBillboardAnchorAttr);
    geo.setAttribute('treeTint', treeState.treeBillboardTintAttr);
    geo.setAttribute('treeSize', sizeAttr);
    // Pre-fill size so the shader always reads non-zero (instance count
    // is what bounds drawing; unused slots stay invisible anyway).
    const totalH = TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT;
    const w = TREE_FOLIAGE_RADIUS * 2.4;
    for (let i = 0; i < TREE_BILLBOARD_CAPACITY; i++) {
        sizeAttr.array[i * 2] = w;
        sizeAttr.array[i * 2 + 1] = totalH;
    }

    const mat = new MeshLambertNodeMaterial({
        side: THREE.DoubleSide,
        transparent: true,
        alphaTest: 0.4,
    });
    mat.positionNode = Fn(() => {
        const local = positionLocal.toVar();
        const anchor = attribute('treeAnchor').toVar();
        const size = attribute('treeSize').toVar();
        // Direction camera → tree on XZ. Right vector perpendicular on XZ.
        const dx = anchor.x.sub(cameraPosition.x);
        const dz = anchor.z.sub(cameraPosition.z);
        const len = dx.mul(dx).add(dz.mul(dz)).sqrt().max(0.001);
        const ndx = dx.div(len);
        const ndz = dz.div(len);
        // right = ( ndz, 0, -ndx )  ← perpendicular on XZ
        const wx = anchor.x.add(ndz.mul(local.x).mul(size.x));
        const wy = anchor.y.add(local.y.mul(size.y));
        const wz = anchor.z.add(ndx.negate().mul(local.x).mul(size.x));
        return vec3(wx, wy, wz);
    })();
    mat.colorNode = Fn(() => {
        const tint = attribute('treeTint').toVar();
        const samp = texture(treeState.treeBillboardTex, uv()).toVar();
        return vec3(samp.x.mul(tint.x), samp.y.mul(tint.y), samp.z.mul(tint.z));
    })();
    mat.opacityNode = Fn(() => texture(treeState.treeBillboardTex, uv()).w)();

    treeState.treeBillboardMesh = new THREE.InstancedMesh(geo, mat, TREE_BILLBOARD_CAPACITY);
    treeState.treeBillboardMesh.frustumCulled = false;
    treeState.treeBillboardMesh.castShadow = false;
    treeState.treeBillboardMesh.receiveShadow = false;
    treeState.treeBillboardMesh.matrixAutoUpdate = false;
    treeState.treeBillboardMesh.matrixWorldAutoUpdate = false;
    treeState.treeBillboardMesh.visible = true;
    treeState.treeBillboardMesh.count = 0;
    treeState.treeBillboardMesh.updateMatrix();
    treeState.treeBillboardMesh.updateMatrixWorld(true);
    grassState.grassRoot.add(treeState.treeBillboardMesh);
}

// Sample every forest tile in the map once, write to two InstancedMesh
// (bark + canopy) sized exactly for the result, add to scene. No tile
// cache, no pool, no billboard — three.js draws them all every frame.
// Works well because trees are sparse (TREE_DENSITY = 0.05/m²) so even a
// huge map produces a few hundred thousand instances at worst.
export function buildStaticTreeMeshes() {
    if (!treeState.treeAssetsLoaded || !treeState.treeBarkGeo || !treeState.treeCanopyGeo
        || !treeState.treeBarkMaterial || !treeState.treeCanopyMaterial) {
        mapHost.log('[tree] static mode: tree assets missing, skipping');
        return;
    }
    const t0 = performance.now();
    const trees = [];
    // Area-based per-tile count instead of a jittered grid. A grid
    // quantises to floor(tile_size / step)² candidates per tile, which
    // SNAPS in 4× / 9× steps as `step` crosses tile-size boundaries —
    // that's the "tree count jumps between 0.00062 and 0.00063" symptom.
    // Per-tile target = tile_area × density; fractional remainder is
    // honoured by a Bernoulli draw so the global density is exact.
    const tileArea = TREE_TILE_SIZE * TREE_TILE_SIZE;
    for (const [key, bucket] of treeState.treePolyBuckets) {
        if (!bucket.hasForest) continue;
        const [ixStr, izStr] = key.split(',');
        const ix = parseInt(ixStr, 10), iz = parseInt(izStr, 10);
        const x0 = ix * TREE_TILE_SIZE;
        const z0 = iz * TREE_TILE_SIZE;
        const polysIn = bucket.polys;
        const target = tileArea * TREE_DENSITY;
        const base = Math.floor(target);
        const frac = target - base;
        const N = base + (Math.random() < frac ? 1 : 0);
        for (let k = 0; k < N; k++) {
            const jx = x0 + Math.random() * TREE_TILE_SIZE;
            const jy = z0 + Math.random() * TREE_TILE_SIZE;
            let hit = null;
            for (let i = polysIn.length - 1; i >= 0; i--) {
                const p = polysIn[i];
                if (pointInPolygon(jx, jy, p.poly)) { hit = p; break; }
            }
            if (!hit || !hit.isForest) continue;
            trees.push({
                x: jx, z: jy,
                yaw: Math.random() * Math.PI * 2,
                scale: (0.85 + Math.random() * 0.45)
                    * treeState.treeRefScale * TREE_SCALE,
            });
        }
    }
    const sampleMs = performance.now() - t0;
    mapHost.log('[tree] static-mode sampled ' + trees.length
        + ' trees in ' + sampleMs.toFixed(0) + 'ms');
    if (trees.length === 0) return;

    treeState.treeBarkStaticMesh = new THREE.InstancedMesh(
        treeState.treeBarkGeo, treeState.treeBarkMaterial, trees.length);
    treeState.treeCanopyStaticMesh = new THREE.InstancedMesh(
        treeState.treeCanopyGeo, treeState.treeCanopyMaterial, trees.length);
    for (const m of [treeState.treeBarkStaticMesh, treeState.treeCanopyStaticMesh]) {
        // Frustum culling on InstancedMesh uses geometry's bounding
        // sphere (a single tree at origin) — wrong for instances spread
        // across the map. Disabling means three.js draws every instance;
        // for a sparse tree count this is fine and the GPU does its own
        // primitive-level cull at clip space anyway.
        m.frustumCulled = false;
        m.matrixAutoUpdate = false;
        m.matrixWorldAutoUpdate = false;
        m.visible = true;
        m.updateMatrix();
        m.updateMatrixWorld(true);
    }
    // Bark — solid geometry, casts + receives normal shadows.
    treeState.treeBarkStaticMesh.castShadow = true;
    treeState.treeBarkStaticMesh.receiveShadow = true;
    // Canopy — cross-quad cards. three.js webgpu auto-derives the shadow
    // depth shader from the main material (alphaTest + opacityNode),
    // so no customDepthMaterial is needed.
    treeState.treeCanopyStaticMesh.castShadow = true;
    treeState.treeCanopyStaticMesh.receiveShadow = true;
    const dummy = new THREE.Object3D();
    for (let i = 0; i < trees.length; i++) {
        const t = trees[i];
        dummy.position.set(t.x, 0, t.z);
        dummy.rotation.set(0, t.yaw, 0);
        dummy.scale.setScalar(t.scale);
        dummy.updateMatrix();
        treeState.treeBarkStaticMesh.setMatrixAt(i, dummy.matrix);
        treeState.treeCanopyStaticMesh.setMatrixAt(i, dummy.matrix);
    }
    treeState.treeBarkStaticMesh.instanceMatrix.needsUpdate = true;
    treeState.treeCanopyStaticMesh.instanceMatrix.needsUpdate = true;
    grassState.grassRoot.add(treeState.treeBarkStaticMesh);
    grassState.grassRoot.add(treeState.treeCanopyStaticMesh);
}
