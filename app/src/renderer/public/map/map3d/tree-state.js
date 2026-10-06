import * as THREE from "../three.webgpu.min.js";

export const treePoolDummy = new THREE.Object3D();

export const treeState = {
    treePolygons: [],
    treePolyBuckets: new Map(),
    treeTileCache: new Map(),
    treeMaterial: null,
    treeTileGeometry: null,
    treeBarkGeo: null,
    treeCanopyGeo: null,
    treeBarkMaterial: null,
    treeCanopyMaterial: null,
    treeCanopyTex: null,
    treeBarkDiffuseTex: null,
    treeBarkNormalTex: null,
    // Scene meshes share these assets. tearDown disposes the scene and clears
    // both load markers so re-entry cannot reuse released geometry or textures.
    treeAssetsLoaded: false,
    treeAssetsPromise: null,
    treeRefScale: 1,
    treeStep: 1,
    treePool: [],
    treeFreeSlots: [],
    treePrimed: false,
    treeBillboardTex: null,
    treeBillboardMesh: null,
    treeBillboardTintAttr: null,
    treeBillboardAnchorAttr: null,
    treeBarkStaticMesh: null,
    treeCanopyStaticMesh: null,
};
