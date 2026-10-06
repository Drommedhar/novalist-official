import * as THREE from "../three.webgpu.min.js";
import {
    GRASS_BILLBOARD_CAPACITY,
    GRASS_BILLBOARD_TEX_RES,
    GRASS_BLADE_HEIGHT,
    GRASS_BLADE_WIDTH,
    GRASS_DEBUG,
    GRASS_MAX_BLADES_PER_TILE,
    GRASS_POOL_SIZE,
    GRASS_TILE_SIZE,
    GRASS_WIND_AMPL,
    GRASS_WIND_FREQ,
} from './grass-settings.js';
import { MeshLambertNodeMaterial } from "../three.webgpu.min.js";
import { Fn, attribute, mix, positionLocal, texture, time, uv, vec3 } from "../three.tsl.min.js";
import { grassDebug, grassState } from './grass-state.js';
import { sceneState } from './scene-state.js';
import { renderBillboardTexture } from './billboard-rendering.js';

// 4-quad tapered ribbon, two columns of verts (left/right), five rows
// (root → tip). UV.y runs 0 at the root, 1 at the tip, so the wind shader
// can pivot the sway about the root.
export function makeGrassBladeGeometry() {
    const rows = 5;
    const pos = [];
    const uvs = [];
    for (let r = 0; r < rows; r++) {
        const v = r / (rows - 1);
        const taper = 1 - v * 0.95;
        const hw = GRASS_BLADE_WIDTH * 0.5 * taper;
        pos.push(-hw, v * GRASS_BLADE_HEIGHT, 0,
                  hw, v * GRASS_BLADE_HEIGHT, 0);
        uvs.push(0, v, 1, v);
    }
    const idx = [];
    for (let r = 0; r < rows - 1; r++) {
        const a = r * 2, b = r * 2 + 1, c = (r + 1) * 2 + 1, d = (r + 1) * 2;
        idx.push(a, b, c, a, c, d);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
    g.setIndex(idx);
    g.computeVertexNormals();
    return g;
}

// Shared by every grass tile in this scene build — one material + one
// geometry template, then per-tile clones override the instanced
// attributes. Reusing the material keeps the compiled shader hot.
export function makeGrassMaterial() {
    const mat = new MeshLambertNodeMaterial({ side: THREE.DoubleSide });
    mat.positionNode = Fn(() => {
        const local = positionLocal.toVar();
        const phase = attribute('grassPhase').toVar();
        const rXZ = attribute('grassRoot').toVar();
        // Sway: per-blade phase + a world-space drift so neighbours don't
        // sway in lockstep. Weight by uv.y² → root anchored, tip swings.
        // No distance fade in-shader — the per-tile inst.count cull thins
        // density continuously to zero at the radius, so blades never need
        // to shrink mid-shader.
        const t = time.mul(GRASS_WIND_FREQ)
            .add(phase)
            .add(rXZ.x.mul(0.05))
            .add(rXZ.y.mul(0.05));
        const sway = t.sin().mul(GRASS_WIND_AMPL).mul(uv().y.mul(uv().y));
        return vec3(local.x.add(sway), local.y, local.z);
    })();
    mat.colorNode = Fn(() => {
        // Subtle earth-shade at the root, full shape colour up top.
        // Revo-realms uses a heavily-brown base; we keep most of the
        // blade green so the shape's authored colour reads from above
        // (top-down editing view) and only the lowest portion gets the
        // darker earthy bias for depth.
        const tint = attribute('grassTint').toVar();
        const top = vec3(tint.x, tint.y, tint.z);
        // Slightly darker + warmer at the root: 70% of tip, +small red tint.
        const rootShade = vec3(top.x.mul(0.55).add(0.08),
                               top.y.mul(0.55),
                               top.z.mul(0.45));
        const h = uv().y;
        // Pull the transition closer to the root so most of the visible
        // blade reads as the shape colour.
        return mix(rootShade, top, h.mul(0.6).add(0.4));
    })();
    return mat;
}

// One-time pool setup. Builds GRASS_POOL_SIZE invisible InstancedMesh
// objects, each with max-capacity instance buffers, and adds them to the
// scene. After this, sampleGrassTile NEVER creates a new mesh — it just
// overwrites these buffers and toggles visibility. Cost is paid upfront
// behind the loading screen.
export function buildGrassPool() {
    grassState.grassPool = [];
    grassState.grassFreeSlots = [];
    for (let i = 0; i < GRASS_POOL_SIZE; i++) {
        const geo = makeGrassBladeGeometry();
        const rootsAttr = new THREE.InstancedBufferAttribute(
            new Float32Array(GRASS_MAX_BLADES_PER_TILE * 2), 2);
        const tintsAttr = new THREE.InstancedBufferAttribute(
            new Float32Array(GRASS_MAX_BLADES_PER_TILE * 3), 3);
        const phasesAttr = new THREE.InstancedBufferAttribute(
            new Float32Array(GRASS_MAX_BLADES_PER_TILE), 1);
        geo.setAttribute('grassRoot', rootsAttr);
        geo.setAttribute('grassTint', tintsAttr);
        geo.setAttribute('grassPhase', phasesAttr);
        // bbox / sphere set per-tile when a slot is bound. Start with a
        // far-away placeholder so the slot frustum-culls until used.
        geo.boundingBox = new THREE.Box3(
            new THREE.Vector3(-1, -1, -1), new THREE.Vector3(1, 1, 1));
        geo.boundingSphere = new THREE.Sphere(new THREE.Vector3(), 1);
        const mesh = new THREE.InstancedMesh(geo, grassState.grassMaterial, GRASS_MAX_BLADES_PER_TILE);
        // Frustum culling on InstancedMesh in three.js webgpu uses
        // geometry.boundingSphere transformed by mesh.matrixWorld. Pool
        // mesh transforms are identity and we bake each tile's world rect
        // into geometry.boundingBox / boundingSphere on bind — but the
        // renderer often picked a stale or placeholder sphere and culled
        // visible tiles. updateGrassVisibility already culls by 2D
        // distance, so we skip the GPU-side cull entirely.
        mesh.frustumCulled = false;
        mesh.castShadow = false;
        mesh.receiveShadow = false;
        mesh.matrixAutoUpdate = false;
        mesh.matrixWorldAutoUpdate = false;
        mesh.visible = false;
        mesh.count = 0;
        mesh.userData.totalBlades = 0;
        mesh.userData.grassCenter = { x: 0, z: 0 };
        mesh.updateMatrix();
        mesh.updateMatrixWorld(true);
        grassState.grassRoot.add(mesh);
        grassState.grassPool.push({
            mesh, geo, rootsAttr, tintsAttr, phasesAttr, tileKey: null,
        });
        grassState.grassFreeSlots.push(i);
    }
}

// Evict the pool slot whose bound tile is furthest from the camera, free
// its cache entry, and return the slot's index. The caller will
// immediately rebind it to a new tile. Used when the free stack is empty.
export function evictFurthestPoolSlot() {
    const cx = sceneState.camera.position.x, cz = sceneState.camera.position.z;
    let furthestIdx = -1, furthestD2 = -1;
    for (let i = 0; i < grassState.grassPool.length; i++) {
        const e = grassState.grassPool[i];
        if (e.tileKey === null) return i; // free slot somehow
        const c = e.mesh.userData.grassCenter;
        const dx = c.x - cx, dz = c.z - cz;
        const d2 = dx * dx + dz * dz;
        if (d2 > furthestD2) { furthestD2 = d2; furthestIdx = i; }
    }
    if (furthestIdx < 0) return -1;
    const ev = grassState.grassPool[furthestIdx];
    if (ev.tileKey !== null) {
        grassState.grassTileCache.delete(ev.tileKey);
        ev.tileKey = null;
    }
    ev.mesh.visible = false;
    ev.mesh.count = 0;
    if (GRASS_DEBUG) grassDebug.evictions++;
    return furthestIdx;
}

// Pre-render the billboard texture by drawing a representative patch of
// blades into an offscreen RenderTarget using the actual blade material.
// Top-down ortho view so the result tiles cleanly across a flat quad.
export function generateGrassBillboardTexture() {
    if (!sceneState.renderer || !grassState.grassMaterial) return null;
    const W = GRASS_BILLBOARD_TEX_RES, H = GRASS_BILLBOARD_TEX_RES;
    const PATCH = 8; // world units (square half-extent doubled below)

    const subScene = new THREE.Scene();
    // Match main-scene lighting roughly so the texture's shading reads
    // consistent with nearby live blades.
    subScene.add(new THREE.AmbientLight(0xffffff, 0.6));
    const sun = new THREE.DirectionalLight(0xffffcc, 0.85);
    sun.position.set(0.4, 1, 0.3);
    subScene.add(sun);

    const count = Math.max(200, Math.round(PATCH * PATCH * grassState.grassEffectiveDensity));
    const geo = makeGrassBladeGeometry();
    const roots = new Float32Array(count * 2);
    const tints = new Float32Array(count * 3);
    const phases = new Float32Array(count);
    const dummy = new THREE.Object3D();
    geo.setAttribute('grassRoot', new THREE.InstancedBufferAttribute(roots, 2));
    geo.setAttribute('grassTint', new THREE.InstancedBufferAttribute(tints, 3));
    geo.setAttribute('grassPhase', new THREE.InstancedBufferAttribute(phases, 1));
    const mesh = new THREE.InstancedMesh(geo, grassState.grassMaterial, count);
    mesh.frustumCulled = false;
    for (let i = 0; i < count; i++) {
        const x = (Math.random() - 0.5) * PATCH;
        const z = (Math.random() - 0.5) * PATCH;
        roots[i * 2] = x; roots[i * 2 + 1] = z;
        tints[i * 3] = 1; tints[i * 3 + 1] = 1; tints[i * 3 + 2] = 1; // white — coloured at billboard render time
        phases[i] = Math.random() * Math.PI * 2;
        dummy.position.set(x, 0, z);
        dummy.rotation.set(0, Math.random() * Math.PI * 2, 0);
        dummy.scale.setScalar(0.7 + Math.random() * 0.6);
        dummy.updateMatrix();
        mesh.setMatrixAt(i, dummy.matrix);
    }
    mesh.instanceMatrix.needsUpdate = true;
    subScene.add(mesh);

    const cam = new THREE.OrthographicCamera(
        -PATCH * 0.5, PATCH * 0.5, PATCH * 0.5, -PATCH * 0.5, 0.1, 50);
    // Slight pitch — pure top-down loses all blade vertical detail. ~30°
    // tilt reads as "grass viewed from afar at a glancing angle".
    cam.position.set(0, 6, 4);
    cam.lookAt(0, 0, 0);
    cam.updateMatrixWorld(true);

    const rt = new THREE.RenderTarget(W, H, {
        type: THREE.UnsignedByteType,
        format: THREE.RGBAFormat,
        depthBuffer: true,
    });
    rt.texture.wrapS = THREE.RepeatWrapping;
    rt.texture.wrapT = THREE.RepeatWrapping;
    rt.texture.colorSpace = THREE.SRGBColorSpace;

    renderBillboardTexture(rt, subScene, cam, 'grass');

    geo.dispose();
    return rt.texture;
}

export function buildGrassBillboardMesh() {
    if (grassState.grassBillboardMesh) {
        grassState.grassRoot.remove(grassState.grassBillboardMesh);
        try { grassState.grassBillboardMesh.geometry.dispose(); } catch (error) { console.warn("map3d: buildGrassBillboardMesh failed", error instanceof Error ? error.name : typeof error); }
        try { grassState.grassBillboardMesh.material.dispose(); } catch (error) { console.warn("map3d: buildGrassBillboardMesh failed", error instanceof Error ? error.name : typeof error); }
    }
    const geo = new THREE.PlaneGeometry(GRASS_TILE_SIZE, GRASS_TILE_SIZE);
    geo.rotateX(-Math.PI / 2); // flat on XZ
    grassState.grassBillboardTintAttr = new THREE.InstancedBufferAttribute(
        new Float32Array(GRASS_BILLBOARD_CAPACITY * 3), 3);
    geo.setAttribute('grassTint', grassState.grassBillboardTintAttr);

    const mat = new MeshLambertNodeMaterial({
        side: THREE.DoubleSide,
        transparent: true,
        alphaTest: 0.4,
    });
    mat.colorNode = Fn(() => {
        const tint = attribute('grassTint').toVar();
        const samp = texture(grassState.grassBillboardTex, uv()).toVar();
        return vec3(samp.x.mul(tint.x), samp.y.mul(tint.y), samp.z.mul(tint.z));
    })();
    mat.opacityNode = Fn(() => texture(grassState.grassBillboardTex, uv()).w)();

    grassState.grassBillboardMesh = new THREE.InstancedMesh(geo, mat, GRASS_BILLBOARD_CAPACITY);
    grassState.grassBillboardMesh.frustumCulled = false;
    grassState.grassBillboardMesh.castShadow = false;
    grassState.grassBillboardMesh.receiveShadow = false;
    grassState.grassBillboardMesh.matrixAutoUpdate = false;
    grassState.grassBillboardMesh.matrixWorldAutoUpdate = false;
    grassState.grassBillboardMesh.visible = true;
    grassState.grassBillboardMesh.count = 0;
    grassState.grassBillboardMesh.updateMatrix();
    grassState.grassBillboardMesh.updateMatrixWorld(true);
    grassState.grassRoot.add(grassState.grassBillboardMesh);
}
