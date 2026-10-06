import * as THREE from "../three.webgpu.min.js";
import { treeState } from './tree-state.js';
import { GLTFLoader } from '../GLTFLoader.js';
import { DRACOLoader } from '../DRACOLoader.js';
import { mapHost } from './host.js';
import { TREE_FOLIAGE_HEIGHT, TREE_TRUNK_HEIGHT } from './tree-settings.js';
import { KTX2Loader } from '../KTX2Loader.js';
import { sceneState } from './scene-state.js';
import { MeshLambertNodeMaterial } from "../three.webgpu.min.js";
import { Fn, float, normalMap, texture, uv, vec3 } from "../three.tsl.min.js";

// Pool of tile-meshes — same shape as grass pool. Each entry holds up to
// TREE_MAX_PER_TILE tree instances; we overwrite the instance matrices
// when a tile binds.
// Async-load the GLB + canopy texture once per session. Resolves with
// `true` on success, `false` if anything's missing — caller falls back to
// the procedural tree mesh.
export function loadTreeAssets() {
    if (treeState.treeAssetsLoaded) return Promise.resolve(true);
    if (treeState.treeAssetsPromise) return treeState.treeAssetsPromise;
    const loader = new GLTFLoader();
    // revo-realms's realm.glb uses Draco compression on its mesh data.
    // Attach a DRACOLoader pointed at the bundled wasm decoder.
    const draco = new DRACOLoader();
    draco.setDecoderPath('./draco/');
    draco.setDecoderConfig({ type: 'wasm' });
    loader.setDRACOLoader(draco);
    const texLoader = new THREE.TextureLoader();
    treeState.treeAssetsPromise = (async () => {
        try {
            mapHost.log('[tree] loading sekai.glb (Draco)...');
            const gltf = await loader.loadAsync('./vegetation/sekai.glb');
            mapHost.log('[tree] sekai.glb parsed');
            const barkMesh = gltf.scene.getObjectByName('pine_tree_bark');
            const canopyMesh = gltf.scene.getObjectByName('pine_tree_canopy');
            if (!barkMesh || !canopyMesh) {
                mapHost.log('[tree] GLB missing pine_tree_bark / pine_tree_canopy');
                return false;
            }
            treeState.treeBarkGeo = barkMesh.geometry;
            treeState.treeCanopyGeo = canopyMesh.geometry;
            mapHost.log('[tree] geometries extracted'
                + ' (bark verts=' + treeState.treeBarkGeo.attributes.position.count
                + ', canopy verts=' + treeState.treeCanopyGeo.attributes.position.count + ')');
            treeState.treeBarkGeo.computeBoundingBox();
            const bb = treeState.treeBarkGeo.boundingBox;
            const glbHeight = Math.max(0.1, bb.max.y - bb.min.y);
            const targetHeight = TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT;
            treeState.treeRefScale = targetHeight / glbHeight;
            mapHost.log('[tree] refScale=' + treeState.treeRefScale.toFixed(3)
                + ' (glbHeight=' + glbHeight.toFixed(2)
                + ', target=' + targetHeight + ')');
            await loadTreeTextures(texLoader);

            treeState.treeAssetsLoaded = true;
            return true;
        } catch (e) {
            mapHost.log('[tree] asset load failed: '
                + (e && e.stack ? e.stack : e && e.message ? e.message : e));
            return false;
        }
    })();
    return treeState.treeAssetsPromise;
}

export function makeTreeMaterialsFromGlb() {
    // Bark — solid brown Lambert. (We don't ship the bark KTX2 texture.)
    treeState.treeBarkMaterial = new MeshLambertNodeMaterial({
        side: THREE.DoubleSide,
        vertexColors: false,
    });
    // Bark — match revo-realms PineTreeBarkMaterial: diffuse × ~3.5,
    // UV scale 3 (tiles around trunk), optional normal map with scale 3.
    const BARK_UV_SCALE = 3;
    const BARK_DIFFUSE_SCALE = 3.5;
    const BARK_NORMAL_SCALE = 3;
    if (treeState.treeBarkDiffuseTex) {
        treeState.treeBarkMaterial.colorNode = Fn(() => {
            const tUv = uv().mul(BARK_UV_SCALE);
            const samp = texture(treeState.treeBarkDiffuseTex, tUv).toVar();
            return vec3(samp.x, samp.y, samp.z).mul(BARK_DIFFUSE_SCALE);
        })();
    } else {
        treeState.treeBarkMaterial.colorNode = Fn(() => vec3(0.42, 0.26, 0.13))();
    }
    if (treeState.treeBarkNormalTex) {
        treeState.treeBarkMaterial.normalNode = Fn(() => {
            const tUv = uv().mul(BARK_UV_SCALE);
            return normalMap(
                texture(treeState.treeBarkNormalTex, tUv), float(BARK_NORMAL_SCALE));
        })();
    }

    // Canopy — alpha-tested needles. Matches revo-realms
    // PineTreeCanopyMaterial: opaque, alphaTest, no blending. Setting
    // `transparent: true` here would re-enable depth sorting (and the
    // ordered alpha blend) which breaks the cross-quad foliage — the
    // mesh has dozens of overlapping quads that depend on z-test + a
    // hard discard, not blending.
    treeState.treeCanopyMaterial = new MeshLambertNodeMaterial({
        side: THREE.DoubleSide,
        transparent: false,
        // Higher alphaTest = crisper silhouette for both main render AND
        // the auto-derived shadow depth pass. Too high eats needle tips;
        // too low merges card edges into solid shadow blobs.
        alphaTest: 0.5,
        forceSinglePass: true,
    });
    treeState.treeCanopyMaterial.colorNode = Fn(() => {
        const samp = texture(treeState.treeCanopyTex, uv()).toVar();
        return vec3(samp.x, samp.y, samp.z).mul(0.6);
    })();
    treeState.treeCanopyMaterial.opacityNode = Fn(() => texture(treeState.treeCanopyTex, uv()).w)();
}


async function loadTreeTextures(texLoader) {
    mapHost.log('[tree] loading KTX2 canopy texture...');
    const ktx2 = new KTX2Loader();
    ktx2.setTranscoderPath('./basis/');
    try {
        ktx2.detectSupport(sceneState.renderer);
        mapHost.log('[tree] KTX2 detectSupport ok');
    } catch (e) {
        mapHost.log('[tree] KTX2 detectSupport failed: ' + (e && e.message));
    }
    try {
        treeState.treeCanopyTex = await ktx2.loadAsync(
            './vegetation/pine-canopy-diffuse.ktx2');
        mapHost.log('[tree] KTX2 canopy loaded');
    } catch (e) {
        mapHost.log('[tree] KTX2 load failed, falling back to PNG: '
            + (e && e.stack ? e.stack : e && e.message ? e.message : e));
        treeState.treeCanopyTex = await texLoader.loadAsync(
            './vegetation/pine-canopy-diffuse.png');
        mapHost.log('[tree] PNG canopy loaded as fallback');
    }
    treeState.treeCanopyTex.colorSpace = THREE.SRGBColorSpace;
    treeState.treeCanopyTex.wrapS = THREE.ClampToEdgeWrapping;
    treeState.treeCanopyTex.wrapT = THREE.ClampToEdgeWrapping;

    // Bark — diffuse + normal, both KTX2 (UASTC). The bark tiles
    // around the trunk, so wrap repeats and we don't need to
    // pre-set UV scale on the texture itself; the material picks
    // a UV multiplier per-fragment.
    mapHost.log('[tree] loading bark KTX2 textures...');
    try {
        treeState.treeBarkDiffuseTex = await ktx2.loadAsync(
            './vegetation/tree-bark-diffuse.ktx2');
        treeState.treeBarkDiffuseTex.colorSpace = THREE.SRGBColorSpace;
        treeState.treeBarkDiffuseTex.wrapS = THREE.RepeatWrapping;
        treeState.treeBarkDiffuseTex.wrapT = THREE.RepeatWrapping;
        mapHost.log('[tree] bark diffuse loaded');
    } catch (e) {
        mapHost.log('[tree] bark diffuse load failed: ' + (e && e.message));
        treeState.treeBarkDiffuseTex = null;
    }
    try {
        treeState.treeBarkNormalTex = await ktx2.loadAsync(
            './vegetation/tree-bark-normal.ktx2');
        treeState.treeBarkNormalTex.wrapS = THREE.RepeatWrapping;
        treeState.treeBarkNormalTex.wrapT = THREE.RepeatWrapping;
        mapHost.log('[tree] bark normal loaded');
    } catch (e) {
        mapHost.log('[tree] bark normal load failed: ' + (e && e.message));
        treeState.treeBarkNormalTex = null;
    }
}
