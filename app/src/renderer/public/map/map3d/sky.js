import * as THREE from "../three.webgpu.min.js";
import GUI from "../lil-gui.module.min.js";
import { skyParams, skyState } from './sky-state.js';
import { SkyMesh } from '../SkyMesh.js';
import {
    SKY_MIE_COEFFICIENT,
    SKY_MIE_DIRECTIONAL_G,
    SKY_RAYLEIGH,
    SKY_SCALE,
    SKY_TURBIDITY,
    SUN_DIR,
} from './sky-settings.js';
import { sceneState } from './scene-state.js';
import { uniform } from "../three.tsl.min.js";
import { WORLD_SCALE } from './scene-settings.js';

// Build the SkyMesh + PMREM env map. Sky replaces the solid background
// colour; the PMREM-encoded sky is plugged into `scene.environment` so any
// PBR material (none today, but future-proof) reflects sky tint. Water in
// this scene already picks up sky via screen-space reflection of the
// framebuffer, so PMREM is mainly insurance.
export function buildSky() {
    skyState.sky = new SkyMesh();
    skyState.sky.scale.setScalar(SKY_SCALE);
    skyState.sky.frustumCulled = false;
    skyState.sky.renderOrder = -1; // drawn before opaque so reflective passes can sample it
    sceneState.scene.add(skyState.sky);
    applySkyParams(); // pushes skyParams → sky uniforms + SUN_DIR
    buildSkyGui();
    // SkyMesh fills the framebuffer; clear the solid bg colour so it stops
    // wasting fill on the first pass.
    sceneState.scene.background = null;

    // PMREM env from a sky-only temp scene. Hide the sun disc during the
    // bake so the bright pixel doesn't blow out the env exposure.
    try {
        const pmrem = new THREE.PMREMGenerator(sceneState.renderer);
        const skyForEnv = new SkyMesh();
        skyForEnv.scale.setScalar(SKY_SCALE);
        skyForEnv.turbidity.value = SKY_TURBIDITY;
        skyForEnv.rayleigh.value = SKY_RAYLEIGH;
        skyForEnv.mieCoefficient.value = SKY_MIE_COEFFICIENT;
        skyForEnv.mieDirectionalG.value = SKY_MIE_DIRECTIONAL_G;
        skyForEnv.cloudCoverage.value = 0; // no clouds in the env bake
        skyForEnv.sunPosition.value.set(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z);
        skyForEnv.showSunDisc.value = 0;
        const envScene = new THREE.Scene();
        envScene.add(skyForEnv);
        skyState.skyEnvRT = pmrem.fromScene(envScene);
        sceneState.scene.environment = skyState.skyEnvRT.texture;
        pmrem.dispose();
        // Dispose the throwaway sky's GPU resources.
        if (skyForEnv.geometry) skyForEnv.geometry.dispose();
        if (skyForEnv.material) skyForEnv.material.dispose();
    } catch (e) {
        console.warn('[Map3D] PMREM env bake failed (sky still renders)', e);
    }
}

// Push current SUN_DIR + sky uniforms after the GUI mutates skyParams.
// Also handles elevation / azimuth → SUN_DIR via three.js spherical convention.
export function applySkyParams() {
    if (!skyState.sky) return;
    const phi = THREE.MathUtils.degToRad(90 - skyParams.elevation);
    const theta = THREE.MathUtils.degToRad(skyParams.azimuth);
    SUN_DIR.x = Math.sin(phi) * Math.cos(theta);
    SUN_DIR.y = Math.cos(phi);
    SUN_DIR.z = Math.sin(phi) * Math.sin(theta);
    skyState.sky.sunPosition.value.set(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z);
    // Mirror to the shared water-shader uniform so the water's forward
    // scatter / sun glare and shadow-overhead test track the slider.
    if (skyState.sunDirUniform) skyState.sunDirUniform.value.set(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z);
    skyState.sky.turbidity.value = skyParams.turbidity;
    skyState.sky.rayleigh.value = skyParams.rayleigh;
    skyState.sky.mieCoefficient.value = skyParams.mieCoefficient;
    skyState.sky.mieDirectionalG.value = skyParams.mieDirectionalG;
    skyState.sky.cloudCoverage.value = skyParams.cloudCoverage;
    skyState.sky.cloudDensity.value = skyParams.cloudDensity;
    skyState.sky.cloudElevation.value = skyParams.cloudElevation;
    skyState.sky.showSunDisc.value = skyParams.showSunDisc ? 1 : 0;
}

// Shared sun-direction uniform for any shader that needs to track the GUI
// sun. Lazy — created the first time a water material asks for it. Re-used
// across every WaterMesh so a single value update lights them all.
export function getSunDirUniform() {
    if (!skyState.sunDirUniform) {
        skyState.sunDirUniform = uniform(new THREE.Vector3(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z));
    }
    return skyState.sunDirUniform;
}

// lil-gui floating panel for the sky uniforms. Mirrors the controls from
// the three.js webgpu_sky demo. Built on enter(), destroyed on exit().
export function buildSkyGui() {
    if (skyState.skyGui) { try { skyState.skyGui.destroy(); } catch (error) { console.warn("map3d: buildSkyGui failed", error instanceof Error ? error.name : typeof error); } skyState.skyGui = null; }
    skyState.skyGui = new GUI({ title: 'Sky' });
    const onChange = () => applySkyParams();
    skyState.skyGui.add(skyParams, 'turbidity', 0, 20, 0.1).onChange(onChange);
    skyState.skyGui.add(skyParams, 'rayleigh', 0, 4, 0.001).onChange(onChange);
    skyState.skyGui.add(skyParams, 'mieCoefficient', 0, 0.1, 0.001).onChange(onChange);
    skyState.skyGui.add(skyParams, 'mieDirectionalG', 0, 1, 0.001).onChange(onChange);
    skyState.skyGui.add(skyParams, 'elevation', -10, 90, 0.1).onChange(onChange);
    skyState.skyGui.add(skyParams, 'azimuth', -180, 180, 0.1).onChange(onChange);
    const clouds = skyState.skyGui.addFolder('Clouds');
    clouds.add(skyParams, 'cloudCoverage', 0, 1, 0.001).onChange(onChange);
    clouds.add(skyParams, 'cloudDensity', 0, 1, 0.001).onChange(onChange);
    clouds.add(skyParams, 'cloudElevation', 0, 1, 0.001).onChange(onChange);
    skyState.skyGui.add(skyParams, 'showSunDisc').onChange(onChange);
    // Keep the panel away from the HUD label and out of pointer-lock drag
    // path. Default lil-gui is fixed top-right which is fine here.
}

// The directional light's shadow frustum FOLLOWS the camera — a map-wide
// frustum spreads the shadow map so thin that interior-scale detail is just
// a couple of texels (reads as light leaking through the roof). A camera-
// centred frustum keeps texel density high wherever you're looking. The
// light direction stays fixed (position + target move together).
export function updateSunShadow() {
    if (!sceneState.sun || !sceneState.camera) return;
    const dist = 600 * WORLD_SCALE;
    const half = 300 * WORLD_SCALE;
    const tx = sceneState.camera.position.x, tz = sceneState.camera.position.z;
    sceneState.sun.target.position.set(tx, 0, tz);
    sceneState.sun.position.set(tx + SUN_DIR.x * dist, SUN_DIR.y * dist, tz + SUN_DIR.z * dist);
    const cam = sceneState.sun.shadow.camera;
    cam.left = -half; cam.right = half;
    cam.top = half; cam.bottom = -half;
    cam.near = 1; cam.far = dist * 2.4;
    cam.updateProjectionMatrix();
    sceneState.sun.shadow.bias = -0.0004;
}
