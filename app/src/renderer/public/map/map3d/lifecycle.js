import { keys, sceneState } from './scene-state.js';
import { skyState } from './sky-state.js';
import { waterNormalsTex, waterState } from './water-state.js';
import { treeState } from './tree-state.js';

export function disposeTree(obj) {
    obj.traverse(function (o) {
        if (o.geometry) o.geometry.dispose();
        if (o.material) {
            const mats = Array.isArray(o.material) ? o.material : [o.material];
            for (const m of mats) {
                if (m.map) m.map.dispose();
                m.dispose();
            }
        }
    });
}

// Release every GPU resource owned by the 3D view so the next enter() can
// build a fresh renderer + scene from scratch. Required because re-using a
// stale WebGPU device / swap chain after hiding the canvas left the second
// session rendering one stale frame then stalling.
export function tearDown() {
    if (sceneState.rafId) cancelAnimationFrame(sceneState.rafId);
    sceneState.rafId = 0;
    if (document.pointerLockElement) document.exitPointerLock();
    if (sceneState.sceneRoot) {
        if (sceneState.scene) sceneState.scene.remove(sceneState.sceneRoot);
        disposeTree(sceneState.sceneRoot);
        sceneState.sceneRoot = null;
    }
    if (skyState.sky) {
        if (sceneState.scene) sceneState.scene.remove(skyState.sky);
        try { if (skyState.sky.geometry) skyState.sky.geometry.dispose(); } catch (error) { console.warn("map3d: tearDown failed", error instanceof Error ? error.name : typeof error); }
        try { if (skyState.sky.material) skyState.sky.material.dispose(); } catch (error) { console.warn("map3d: tearDown failed", error instanceof Error ? error.name : typeof error); }
        skyState.sky = null;
    }
    if (skyState.skyGui) {
        try { skyState.skyGui.destroy(); } catch (error) { console.warn("map3d: tearDown failed", error instanceof Error ? error.name : typeof error); }
        skyState.skyGui = null;
    }
    if (skyState.skyEnvRT) {
        try { skyState.skyEnvRT.dispose(); } catch (error) { console.warn("map3d: tearDown failed", error instanceof Error ? error.name : typeof error); }
        skyState.skyEnvRT = null;
    }
    if (sceneState.scene) {
        sceneState.scene.environment = null;
        for (let i = sceneState.scene.children.length - 1; i >= 0; i--) {
            sceneState.scene.remove(sceneState.scene.children[i]);
        }
    }
    for (const t of waterNormalsTex) {
        if (t) { try { t.dispose(); } catch (error) { console.warn("map3d: tearDown failed", error instanceof Error ? error.name : typeof error); } }
    }
    waterNormalsTex.length = 0;
    if (waterState._causticTex) { try { waterState._causticTex.dispose(); } catch (error) { console.warn("map3d: tearDown failed", error instanceof Error ? error.name : typeof error); } waterState._causticTex = null; }
    if (sceneState.renderer) { try { sceneState.renderer.dispose(); } catch (error) { console.warn("map3d: tearDown failed", error instanceof Error ? error.name : typeof error); } sceneState.renderer = null; }
    sceneState.scene = null;
    sceneState.camera = null;
    sceneState.sun = null;
    skyState.sunDirUniform = null;
    sceneState.rendererReady = false;
    // The tree geometry and its textures are shared into the scene, so
    // disposeTree() above has just freed them - but the "already loaded"
    // flag said otherwise, so the next enter() skipped the load and built
    // its forest out of released GPU resources. Which is why leaving 3D
    // and coming back was the way to get a view that never finished.
    treeState.treeAssetsLoaded = false;
    treeState.treeAssetsPromise = null;
    treeState.treeBarkGeo = null;
    treeState.treeCanopyGeo = null;
    treeState.treeCanopyTex = null;
    treeState.treeBarkDiffuseTex = null;
    treeState.treeBarkNormalTex = null;
    for (const k of Object.keys(keys)) delete keys[k];
}
