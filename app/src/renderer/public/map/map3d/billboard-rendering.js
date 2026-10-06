import * as THREE from "../three.webgpu.min.js";
import { sceneState } from './scene-state.js';

export function renderBillboardTexture(target, scene, camera, kind) {
    const oldTarget = sceneState.renderer.getRenderTarget();
    const oldColor = sceneState.renderer.getClearColor(new THREE.Color());
    const oldAlpha = sceneState.renderer.getClearAlpha();
    try {
        sceneState.renderer.setClearColor(0x000000, 0);
        sceneState.renderer.setRenderTarget(target);
        sceneState.renderer.clear();
        sceneState.renderer.render(scene, camera);
    } catch (error) {
        console.warn('Map3D billboard render failed', kind, error instanceof Error ? error.name : typeof error);
    } finally {
        sceneState.renderer.setRenderTarget(oldTarget);
        sceneState.renderer.setClearColor(oldColor, oldAlpha);
    }
}
