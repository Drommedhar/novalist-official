import * as THREE from "../three.webgpu.min.js";

export const grassPoolDummy = new THREE.Object3D(); // scratch matrix builder

// Manual frustum culling — three.js's per-mesh frustum cull on
// InstancedMesh doesn't honour our manual geometry bbox in webgpu, so we
// test each tile's centre sphere against the camera frustum directly.
export const grassFrustum = new THREE.Frustum();

export const grassProjMatrix = new THREE.Matrix4();

export const grassSphere = new THREE.Sphere();

export const grassBillboardDummy = new THREE.Object3D();

export const grassDebug = {
    lastReport: 0,         // perf.now of last console dump
    updateMs: 0,           // total time spent in updateGrassVisibility() this window
    updateCalls: 0,
    sampleMs: 0,           // total time spent in sampleGrassTile() this window
    sampleCount: 0,        // tiles sampled this window
    sampleFast: 0,         // tiles that hit the fastGrass path
    newTiles: 0,           // tiles added to scene this window (sampled non-null)
    visibilityIterMs: 0,   // time spent in the per-cached-tile visibility loop
    renderMs: 0,           // total time spent in renderer.render()
    renderFrames: 0,       // frame count for renderMs averaging
    renderMaxCalls: 0,     // peak per-frame draw call count this window
    renderMaxMs: 0,        // peak single-frame renderer.render() time
    updateMaxMs: 0,        // peak single-frame updateGrassVisibility() time
    evictions: 0,          // pool-slot evictions this window
};

export const grassState = {
    // Polygon order matches terrain painting; later entries mask earlier ones.
    grassPolygons: [],
    grassPolyBuckets: new Map(),
    grassTileCache: new Map(),
    grassMaterial: null,
    grassRoot: null,
    grassPrimed: false,
    // Pool slots own reusable instance buffers. tileKey links each slot to the
    // cache; free slots have no tile and are reassigned without GPU allocation.
    grassPool: [],
    grassFreeSlots: [],
    grassStep: 1,
    grassEffectiveDensity: 1,
    grassBillboardTex: null,
    grassBillboardMesh: null,
    grassBillboardTintAttr: null,
};
