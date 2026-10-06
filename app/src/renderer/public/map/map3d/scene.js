import * as THREE from "../three.webgpu.min.js";
import { sceneState } from './scene-state.js';
import { disposeTree } from './lifecycle.js';
import { BACKDROP_Y, RO_BACKDROP, WORLD_SCALE } from './scene-settings.js';
import { buildImages, buildMapAnnotations, buildTerrain, mapBounds, visibleLayerIds } from './map-content.js';
import { buildGrass } from './grass-polygons.js';
import { buildTrees } from './tree-tiles.js';
import { buildSplines } from './splines.js';
import { buildBuildings } from './building-meshes.js';

export function buildScene() {
    if (sceneState.sceneRoot) {
        sceneState.scene.remove(sceneState.sceneRoot);
        disposeTree(sceneState.sceneRoot);
    }
    sceneState.sceneRoot = new THREE.Group();
    sceneState.sceneRoot.scale.setScalar(WORLD_SCALE);
    const b = mapBounds();
    // Neutral backdrop plane under everything — dropped below the deepest
    // water basin so basins never poke through it.
    const w = Math.max(600, b.maxX - b.minX);
    const d = Math.max(600, b.maxY - b.minY);
    const ground = new THREE.Mesh(
        new THREE.PlaneGeometry(w * 1.8, d * 1.8),
        new THREE.MeshLambertMaterial({ color: 0x5f7a44, depthWrite: false })
    );
    ground.rotation.x = -Math.PI / 2;
    ground.position.set((b.minX + b.maxX) / 2, BACKDROP_Y, (b.minY + b.maxY) / 2);
    ground.renderOrder = RO_BACKDROP;
    ground.receiveShadow = true;
    sceneState.sceneRoot.add(ground);
    const visIds = visibleLayerIds();
    buildImages(sceneState.sceneRoot, visIds);
    buildTerrain(sceneState.sceneRoot, visIds);
    buildGrass(sceneState.sceneRoot, visIds);
    buildTrees(sceneState.sceneRoot, visIds);
    buildSplines(sceneState.sceneRoot, visIds);
    buildBuildings(sceneState.sceneRoot, visIds);
    buildMapAnnotations(sceneState.sceneRoot, visIds);
    sceneState.scene.add(sceneState.sceneRoot);
}
