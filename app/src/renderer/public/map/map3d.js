'use strict';
// 3D map view.
//
// Renders the map in 3D inside the same WebView2 using three.js + WebGPU.
// Terrain / splines (incl. reflective water) / buildings / pins are built from
// the live mapData. Loaded as an ES module so the three.js WebGPU build (which
// ships module-only) can be imported.
//
// All 3D logic lives here. map.html provides the <canvas id="stage3d">, the
// importmap + module <script> tags, and the host-driven Map3D.enter() / exit()
// toggle. map.html's classic inline script runs first, so the shared globals it
// declares (mapData, stage, hud, sendMessage, splineProfile, ...) are visible
// here through the realm's shared global scope.

import * as THREE from 'three';
import { MeshLambertNodeMaterial } from 'three/webgpu';
import { WaterMesh } from './Water2Mesh.js';
import { SkyMesh } from './SkyMesh.js';
import { GLTFLoader } from './GLTFLoader.js';
import { DRACOLoader } from './DRACOLoader.js';
import { KTX2Loader } from './KTX2Loader.js';
import GUI from 'lil-gui';
import {
    Fn, vec2, vec3, vec4, attribute, screenUV, viewportSafeUV,
    viewportSharedTexture, cameraPosition, positionWorld, positionLocal,
    normalize, mix, texture, time, uv, uniform, shadow, normalMap, float,
} from 'three/tsl';

(function () {
    const WORLD_SCALE = 1;     // whole scene scaled uniformly (all axes + height)
    const FLOOR_H = 20;        // world units per building floor (tunable — Phase 4)
    const IWALL_H = FLOOR_H - 0.5; // interior wall height — just under the ceiling slab
    const MOVE_SPEED = 120;    // world units / second
    const LOOK_SPEED = 0.0022; // radians / pixel
    const BOOST = 8;           // Shift multiplier

    // Water bodies don't carve real geometry — the depth is faked in the water
    // shader. Each water-surface vertex carries a baked `waterDepth` (0 at the
    // shore, 1 in the interior); the shader darkens by it and parallax-shifts
    // the refracted scene below by it, so the bed reads as a recessed basin.
    const WATER_SURFACE_Y = 0.55;  // water plane — just above terrain / roads
    // Scales the baked waterDepth attribute (0..1) before it's used in the
    // shader. > 1 stretches "deep" so that more of the lake reads as deep.
    const BASIN_DEPTH = 1.2;
    // ====== Water shader knobs (kept in sync with water-demo/main.js) ======
    const WATER_FALLBACK_COLOR = 0x4a7a9a;
    const WATER_DEEP_MULT = 0.2;
    const WATER_DEEP_TINT = 1.0;
    const WATER_RIPPLE = 15;
    const WATER_SHINE = 0.5;
    const WATER_NORMAL_SCALE = 3;
    const WATER_NORMAL_SCALE2 = 1.7;
    const WATER_REFLECT_DISTORT = 0.8;

    const WATER_FOAM_WIDTH = 0.022;
    const WATER_FOAM_SCALE = 60;
    const WATER_FOAM_DRIFT = 0.012;
    const WATER_FOAM_SPEED = 1.6;
    const WATER_FOAM_COLOR = 0xe6eef0;

    const WATER_SCATTER = 0.0;
    const WATER_SCATTER_POWER = 24;
    const WATER_SCATTER_COLOR = 0xd8eccd;

    const WATER_CAUSTIC = 10.8;
    const WATER_CAUSTIC_SCALE = 0.1;
    const WATER_CAUSTIC_SPEED = 0.04;
    const WATER_CAUSTIC_SHARP = 1.4;
    const WATER_CAUSTIC_COLOR = 0xfff5d0;
    const WATER_CAUSTIC_PARALLAX = 0.6;     // how much the bed shifts under view (with depth)
    const WATER_CAUSTIC_TINT = 1.0;          // how strongly caustics take the water's colour at depth
    const WATER_CAUSTIC_MIN_SCALE = 1.0;    // world-space tile size of the caustic pattern (uniform across depth)
    const WATER_CAUSTIC_DEPTH_SCALE = 1.0;   // depth fade exponent: 0 = caustics everywhere, 1 = linear, 2 = squared (shallow-biased), 4+ = thin rim only

    // World-space drop from water surface to the virtual bed used for the
    // shadow-bend (sun-tilt projects bedPos to a different shadow UV than the
    // surface, so building shadows bend into deeper water).
    const WATER_BED_DEPTH = 15;
    // Shadow strength on the water (0 = no shadow visible at all, 1 = full).
    // Affects BOTH the base-water darkening AND the caustic kill — so dialling
    // it down hides the shadow from every channel at once.
    const WATER_SHADOW_DARKEN = 0.25;
    // Depth fade exponent on shadow strength. 0 = shadow uniform at every
    // depth, 1 = linear fade (full at shore, none at full depth), 2+ = sharper.
    // Deep water tints itself so dark that a shadow on top reads wrong; this
    // fades the shadow back out at depth.
    const WATER_SHADOW_DEPTH_FADE = 2.0;
    // ====== Wave displacement (vertex tessellation + sin-sum waves) ========
    // Wave height in WORLD UNITS at the shore (d=0) and at full depth (d=1).
    // The shader sums three travelling sin waves at varying frequencies and
    // scales their amplitude by mix(SHALLOW, DEEP, dRaw) so banks ripple
    // gently and the open water rolls. 0 disables.
    const WATER_WAVE_SHALLOW = 0.01;
    const WATER_WAVE_DEEP = 0.4;
    // How fast the wave field travels (world-units / s of the time term).
    const WATER_WAVE_SPEED = 0.9;
    // Mesh density: levels of midpoint subdivision applied to the lake's
    // initial triangulation. 5 = 4^5 = 1024 sub-triangles per original (dense
    // enough that the sin-sum waves don't read as facets). LOD steps down for
    // distant or huge lakes.
    const WATER_LAKE_SUBDIV_BASE = 5;
    const WATER_LAKE_SUBDIV_FAR = 3;
    // Distance (in world units) past which a lake drops one subdivision level.
    // Cheap LOD: classify each lake at build time by camera distance.
    const WATER_LAKE_LOD_FAR = 600;
    // Per-axis river ribbon subdivision multiplier. 1 keeps the raw 7-wide
    // CROSS × N-long sampled rows; 2 doubles each axis (4x quads), 3 triples.
    const WATER_RIVER_SUBDIV = 5;
    // ====== Map-specific water geometry knobs (no equivalent in demo) ======
    // Fraction of the way from shore to the deepest interior point at which the
    // water reads fully deep. Lower = far more deep area, a thin shallow rim.
    const WATER_DEEP_REACH = 0.25;
    const WATER_FLOW = 5;          // river flow strength — how far the ripples scroll downstream
    const WATER_TURB = 2;           // curvature turbulence — cross-flow kick on river bends
    const WATER_FLOW_TAPER = 1.25;   // bank-to-centre speed falloff exponent
    const BACKDROP_Y = -2;          // neutral backdrop ground plane

    // ===== Sky (Preetham analytic atmosphere) =================================
    // Geometry size — has to wrap the camera, but the vertex node clamps depth
    // to camera.far, so the actual scale only needs to keep the box outside the
    // near plane and inside frustum culling. 50k is plenty for our world span.
    const SKY_SCALE = 50000;
    const SKY_TURBIDITY = 2;
    const SKY_RAYLEIGH = 1.2;
    const SKY_MIE_COEFFICIENT = 0.005;
    const SKY_MIE_DIRECTIONAL_G = 0.8;
    const SKY_CLOUD_COVERAGE = 0.45;
    const SKY_CLOUD_DENSITY = 0.4;
    const SKY_CLOUD_ELEVATION = 0.5;

    let renderer = null, scene = null, camera = null, sceneRoot = null, sun = null;
    let sky = null;            // SkyMesh instance — replaces solid bg colour
    let skyEnvRT = null;       // PMREM render target — feeds scene.environment
    let skyGui = null;         // lil-gui panel — only attached while 3D active
    let sunDirUniform = null;  // shared TSL uniform — water scatter / sky read it
    let canvas = null;
    let hud3dEl = null;
    let hudFps = 60;          // EMA-smoothed frames/sec for the HUD
    let hudLastUpdate = 0;    // last perf.now() when HUD text was refreshed
    let active = false;
    let rendererReady = false;  // WebGPU renderer + scene initialised
    let inputWired = false;     // wireInput registers global listeners — once only
    let rafId = 0, lastT = 0;
    let fallbackEl = null;      // shown when WebGPU is unavailable
    const waterNormalsTex = []; // procedural ripple normal maps, cached per variant
    const keys = {};
    let yaw = 0, pitch = -0.5;
    // Grass — polygons collected at scene build time, but blades are sampled
    // LAZILY per tile around the camera. A pre-sample over the whole map would
    // blow up on huge "one-big-grass" maps (single polygon × every tile × N
    // blades = millions of point-in-polygon tests up front, and the global cap
    // would chop off the far side). Now we keep the grass polygons + a tile
    // cache, build a few tiles per tick as the camera approaches them, and
    // toggle visibility on tiles already in cache.
    // The FULL visible terrain stack in render-order (index 0 = bottom, last
    // = top). Each entry is { poly, bb, color, isGrass }. The sampler walks
    // this stack top-down per candidate so a sand patch painted on top of a
    // grass plane carves out the grass, and a small grass patch painted on
    // top of a non-grass shape adds grass to that region.
    let grassPolygons = [];
    // Precomputed per-tile polygon bucket (built once per scene). Maps a tile
    // key "ix,iz" → { polys, hasGrass, hasOccluder, fastGrass } so the sampler
    // skips the broad-phase scan and the per-tile corner-test on every build.
    let grassPolyBuckets = new Map();
    let grassTileCache = new Map(); // key "ix,iz" → pool entry's mesh | null
    let grassMaterial = null;
    let grassRoot = null;       // scene group pool meshes are parented to
    let grassPrimed = false;    // first updateGrassVisibility primes everything synchronously
    // The InstancedMesh pool. Each entry has full-capacity instance buffers
    // (size GRASS_MAX_BLADES_PER_TILE) so a tile reassignment only writes into
    // existing GPU buffers — no allocation, no bind-group churn. tileKey ties
    // a slot to a cache entry; null means "free, on the free stack".
    let grassPool = [];               // [{ mesh, geo, rootsAttr, tintsAttr, phasesAttr, tileKey }]
    let grassFreeSlots = [];          // stack of pool indices currently unbound
    const grassPoolDummy = new THREE.Object3D(); // scratch matrix builder
    let grassStep = 1;                // jittered grid step (set at buildGrass)
    let grassEffectiveDensity = 1;    // GRASS_DENSITY clamped to MAX/tile_area
    // Manual frustum culling — three.js's per-mesh frustum cull on
    // InstancedMesh doesn't honour our manual geometry bbox in webgpu, so we
    // test each tile's centre sphere against the camera frustum directly.
    const grassFrustum = new THREE.Frustum();
    const grassProjMatrix = new THREE.Matrix4();
    const grassSphere = new THREE.Sphere();
    // Billboard LOD — far tiles render as a flat textured quad pre-rendered
    // from the actual blade shader. One InstancedMesh shared across all
    // billboard tiles; instance buffer rewritten each tick.
    let grassBillboardTex = null;
    let grassBillboardMesh = null;
    let grassBillboardTintAttr = null;
    const grassBillboardDummy = new THREE.Object3D();

    // ----- Tree system state -----
    let treePolygons = [];
    let treePolyBuckets = new Map();
    let treeTileCache = new Map();
    let treeMaterial = null;     // legacy procedural material (kept for billboard fallback)
    let treeTileGeometry = null; // procedural fallback geometry
    // ----- Loaded from revo-realms realm.glb (MIT, see LICENSES.md) -----
    let treeBarkGeo = null;
    let treeCanopyGeo = null;
    let treeBarkMaterial = null;
    let treeCanopyMaterial = null;
    let treeCanopyTex = null;
    let treeBarkDiffuseTex = null;
    let treeBarkNormalTex = null;
    let treeAssetsLoaded = false;
    let treeAssetsPromise = null;
    let treeRefScale = 1;        // GLB tree is authored at some scale; we normalise.
    let treeStep = 1;
    let treePool = [];
    let treeFreeSlots = [];
    let treePrimed = false;
    let treeBillboardTex = null;
    let treeBillboardMesh = null;
    let treeBillboardTintAttr = null;
    let treeBillboardAnchorAttr = null;
    const treeBillboardDummy = new THREE.Object3D();
    const treePoolDummy = new THREE.Object3D();
    // Static-mode mega-meshes: one for trunk geometry, one for canopy. Both
    // hold every tree on the map. Allocated once at buildTrees, never resized.
    let treeBarkStaticMesh = null;
    let treeCanopyStaticMesh = null;

    // Whether this WebView exposes WebGPU at all.
    function webgpuSupported() {
        return typeof navigator !== 'undefined' && !!navigator.gpu;
    }

    // Full-screen message shown when WebGPU is unavailable / fails to init —
    // beats a silent black canvas. Created lazily, reused.
    function showFallbackMessage() {
        if (!fallbackEl) {
            const el = document.createElement('div');
            el.id = 'map3dFallback';
            el.textContent = '3D map needs WebGPU, which is not available in '
                + 'this WebView. Update the WebView2 runtime, or keep using the '
                + '2D map.';
            el.style.cssText = 'position:fixed;inset:0;display:flex;'
                + 'align-items:center;justify-content:center;text-align:center;'
                + 'padding:48px;box-sizing:border-box;background:#1c2530;'
                + 'color:#e8e8e8;font:14px "Segoe UI",sans-serif;z-index:99999;';
            document.body.appendChild(el);
            fallbackEl = el;
        }
        fallbackEl.style.display = 'flex';
    }
    function hideFallbackMessage() {
        if (fallbackEl) fallbackEl.style.display = 'none';
    }

    // Create the WebGPU renderer + scene + lights on first enter. Async — the
    // WebGPU device is acquired asynchronously. Returns true once ready, false
    // if WebGPU is unavailable (a fallback message is shown in that case).
    async function ensureRenderer() {
        if (rendererReady) return true;
        if (renderer) return false; // a previous init attempt already failed
        canvas = document.getElementById('stage3d');
        if (!webgpuSupported()) {
            console.error('[Map3D] WebGPU not available in this WebView');
            showFallbackMessage();
            return false;
        }
        try {
            renderer = new THREE.WebGPURenderer({ canvas: canvas, antialias: true });
            renderer.setPixelRatio(window.devicePixelRatio || 1);
            renderer.shadowMap.enabled = true;
            renderer.shadowMap.type = THREE.PCFSoftShadowMap;
            renderer.toneMapping = THREE.ACESFilmicToneMapping;
            renderer.toneMappingExposure = 1.0;
            await renderer.init();
        } catch (e) {
            console.error('[Map3D] WebGPU init failed', e);
            renderer = null;
            showFallbackMessage();
            return false;
        }
        scene = new THREE.Scene();
        // No solid background colour — the SkyMesh below fills the framebuffer.
        // Kept as a fallback tint in case sky setup throws.
        scene.background = new THREE.Color(0x9ec7e0);
        camera = new THREE.PerspectiveCamera(60, 1, 1, 20000);
        // Light intensities are tuned for r184's physically-correct lighting +
        // ACES tone mapping — much higher numbers than the old WebGL pipeline.
        scene.add(new THREE.AmbientLight(0xffffff, 1.4));
        // Directional light from the upper-left — matches the 2D roof-shading
        // light vector so 3D and 2D read consistently. Casts shadows; its
        // position + shadow frustum are fitted to the map in updateSunShadow().
        sun = new THREE.DirectionalLight(0xffffff, 2.8);
        sun.castShadow = true;
        sun.shadow.mapSize.set(4096, 4096);
        scene.add(sun);
        scene.add(sun.target);
        // Force the shadow map render target to materialise before any water
        // is built — makeWater binds `sun.shadow.map.texture` into the water
        // material at construction time, so it has to exist by then.
        updateSunShadow();
        try { renderer.render(scene, camera); } catch (e) {}
        try {
            buildSky();
        } catch (e) {
            console.error('[Map3D] Sky setup failed — falling back to solid bg', e);
        }
        wireInput();
        rendererReady = true;
        return true;
    }

    // Build the SkyMesh + PMREM env map. Sky replaces the solid background
    // colour; the PMREM-encoded sky is plugged into `scene.environment` so any
    // PBR material (none today, but future-proof) reflects sky tint. Water in
    // this scene already picks up sky via screen-space reflection of the
    // framebuffer, so PMREM is mainly insurance.
    function buildSky() {
        sky = new SkyMesh();
        sky.scale.setScalar(SKY_SCALE);
        sky.frustumCulled = false;
        sky.renderOrder = -1; // drawn before opaque so reflective passes can sample it
        scene.add(sky);
        applySkyParams(); // pushes skyParams → sky uniforms + SUN_DIR
        buildSkyGui();
        // SkyMesh fills the framebuffer; clear the solid bg colour so it stops
        // wasting fill on the first pass.
        scene.background = null;

        // PMREM env from a sky-only temp scene. Hide the sun disc during the
        // bake so the bright pixel doesn't blow out the env exposure.
        try {
            const pmrem = new THREE.PMREMGenerator(renderer);
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
            skyEnvRT = pmrem.fromScene(envScene);
            scene.environment = skyEnvRT.texture;
            pmrem.dispose();
            // Dispose the throwaway sky's GPU resources.
            if (skyForEnv.geometry) skyForEnv.geometry.dispose();
            if (skyForEnv.material) skyForEnv.material.dispose();
        } catch (e) {
            console.warn('[Map3D] PMREM env bake failed (sky still renders)', e);
        }
    }

    // Unit direction the sunlight travels FROM (upper-left, slightly downward).
    // Mutable — the in-editor sky GUI rewrites this from elevation / azimuth
    // sliders. `updateSunShadow` and the SkyMesh `sunPosition` uniform both
    // read it, so updating it here drives shadow + sky glow together.
    const SUN_DIR = (function () {
        const v = { x: -0.55, y: 1.0, z: -0.84 };
        const l = Math.hypot(v.x, v.y, v.z);
        return { x: v.x / l, y: v.y / l, z: v.z / l };
    })();

    // Push current SUN_DIR + sky uniforms after the GUI mutates skyParams.
    // Also handles elevation / azimuth → SUN_DIR via three.js spherical convention.
    function applySkyParams() {
        if (!sky) return;
        const phi = THREE.MathUtils.degToRad(90 - skyParams.elevation);
        const theta = THREE.MathUtils.degToRad(skyParams.azimuth);
        SUN_DIR.x = Math.sin(phi) * Math.cos(theta);
        SUN_DIR.y = Math.cos(phi);
        SUN_DIR.z = Math.sin(phi) * Math.sin(theta);
        sky.sunPosition.value.set(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z);
        // Mirror to the shared water-shader uniform so the water's forward
        // scatter / sun glare and shadow-overhead test track the slider.
        if (sunDirUniform) sunDirUniform.value.set(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z);
        sky.turbidity.value = skyParams.turbidity;
        sky.rayleigh.value = skyParams.rayleigh;
        sky.mieCoefficient.value = skyParams.mieCoefficient;
        sky.mieDirectionalG.value = skyParams.mieDirectionalG;
        sky.cloudCoverage.value = skyParams.cloudCoverage;
        sky.cloudDensity.value = skyParams.cloudDensity;
        sky.cloudElevation.value = skyParams.cloudElevation;
        sky.showSunDisc.value = skyParams.showSunDisc ? 1 : 0;
    }

    // Shared sun-direction uniform for any shader that needs to track the GUI
    // sun. Lazy — created the first time a water material asks for it. Re-used
    // across every WaterMesh so a single value update lights them all.
    function getSunDirUniform() {
        if (!sunDirUniform) {
            sunDirUniform = uniform(new THREE.Vector3(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z));
        }
        return sunDirUniform;
    }

    // Default elevation / azimuth derived from the legacy SUN_DIR (~45° up,
    // ~−123° azimuth). Keeps the lighting matching what the rest of the app
    // was tuned against.
    const skyParams = {
        turbidity: SKY_TURBIDITY,
        rayleigh: SKY_RAYLEIGH,
        mieCoefficient: SKY_MIE_COEFFICIENT,
        mieDirectionalG: SKY_MIE_DIRECTIONAL_G,
        elevation: THREE.MathUtils.radToDeg(Math.asin(SUN_DIR.y)),
        azimuth: THREE.MathUtils.radToDeg(Math.atan2(SUN_DIR.z, SUN_DIR.x)),
        cloudCoverage: SKY_CLOUD_COVERAGE,
        cloudDensity: SKY_CLOUD_DENSITY,
        cloudElevation: SKY_CLOUD_ELEVATION,
        showSunDisc: true,
    };

    // lil-gui floating panel for the sky uniforms. Mirrors the controls from
    // the three.js webgpu_sky demo. Built on enter(), destroyed on exit().
    function buildSkyGui() {
        if (skyGui) { try { skyGui.destroy(); } catch (e) {} skyGui = null; }
        skyGui = new GUI({ title: 'Sky' });
        const onChange = () => applySkyParams();
        skyGui.add(skyParams, 'turbidity', 0, 20, 0.1).onChange(onChange);
        skyGui.add(skyParams, 'rayleigh', 0, 4, 0.001).onChange(onChange);
        skyGui.add(skyParams, 'mieCoefficient', 0, 0.1, 0.001).onChange(onChange);
        skyGui.add(skyParams, 'mieDirectionalG', 0, 1, 0.001).onChange(onChange);
        skyGui.add(skyParams, 'elevation', -10, 90, 0.1).onChange(onChange);
        skyGui.add(skyParams, 'azimuth', -180, 180, 0.1).onChange(onChange);
        const clouds = skyGui.addFolder('Clouds');
        clouds.add(skyParams, 'cloudCoverage', 0, 1, 0.001).onChange(onChange);
        clouds.add(skyParams, 'cloudDensity', 0, 1, 0.001).onChange(onChange);
        clouds.add(skyParams, 'cloudElevation', 0, 1, 0.001).onChange(onChange);
        skyGui.add(skyParams, 'showSunDisc').onChange(onChange);
        // Keep the panel away from the HUD label and out of pointer-lock drag
        // path. Default lil-gui is fixed top-right which is fine here.
    }

    // The directional light's shadow frustum FOLLOWS the camera — a map-wide
    // frustum spreads the shadow map so thin that interior-scale detail is just
    // a couple of texels (reads as light leaking through the roof). A camera-
    // centred frustum keeps texel density high wherever you're looking. The
    // light direction stays fixed (position + target move together).
    function updateSunShadow() {
        if (!sun || !camera) return;
        const dist = 600 * WORLD_SCALE;
        const half = 300 * WORLD_SCALE;
        const tx = camera.position.x, tz = camera.position.z;
        sun.target.position.set(tx, 0, tz);
        sun.position.set(tx + SUN_DIR.x * dist, SUN_DIR.y * dist, tz + SUN_DIR.z * dist);
        const cam = sun.shadow.camera;
        cam.left = -half; cam.right = half;
        cam.top = half; cam.bottom = -half;
        cam.near = 1; cam.far = dist * 2.4;
        cam.updateProjectionMatrix();
        sun.shadow.bias = -0.0004;
    }

    function disposeTree(obj) {
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
    function tearDown() {
        if (rafId) cancelAnimationFrame(rafId);
        rafId = 0;
        if (document.pointerLockElement) document.exitPointerLock();
        if (sceneRoot) {
            if (scene) scene.remove(sceneRoot);
            disposeTree(sceneRoot);
            sceneRoot = null;
        }
        if (sky) {
            if (scene) scene.remove(sky);
            try { if (sky.geometry) sky.geometry.dispose(); } catch (e) {}
            try { if (sky.material) sky.material.dispose(); } catch (e) {}
            sky = null;
        }
        if (skyGui) {
            try { skyGui.destroy(); } catch (e) {}
            skyGui = null;
        }
        if (skyEnvRT) {
            try { skyEnvRT.dispose(); } catch (e) {}
            skyEnvRT = null;
        }
        if (scene) {
            scene.environment = null;
            for (let i = scene.children.length - 1; i >= 0; i--) {
                scene.remove(scene.children[i]);
            }
        }
        for (const t of waterNormalsTex) {
            if (t) { try { t.dispose(); } catch (e) {} }
        }
        waterNormalsTex.length = 0;
        if (_causticTex) { try { _causticTex.dispose(); } catch (e) {} _causticTex = null; }
        if (renderer) { try { renderer.dispose(); } catch (e) {} renderer = null; }
        scene = null;
        camera = null;
        sun = null;
        sunDirUniform = null;
        rendererReady = false;
        for (const k of Object.keys(keys)) delete keys[k];
    }

    // Tileable ripple normal maps for the water shader, generated on a canvas
    // so no binary texture asset needs bundling. Height field is tileable
    // fractal value-noise (a stack of integer-frequency interpolated random
    // grids) — organic, no directional streaks and no obvious repeat, unlike a
    // sum of pure sines. `variant` (0 or 1) reseeds for a second distinct map.
    // Normals come from central differences. Built once per variant and cached.
    function waterNormalsTexture(variant) {
        if (waterNormalsTex[variant]) return waterNormalsTex[variant];
        const S = 256;
        // Deterministic per-variant RNG (LCG).
        let seed = variant ? 0x9e3779b9 : 0x1337c0de;
        const rand = () => {
            seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
            return seed / 4294967296;
        };
        // One tileable value-noise octave: a g x g grid of random values,
        // smoothstep-interpolated with wraparound (so it repeats seamlessly).
        function octave(g) {
            const grid = new Float32Array(g * g);
            for (let i = 0; i < grid.length; i++) grid[i] = rand();
            return function (u, v) {
                const gx = u * g, gy = v * g;
                const x0 = Math.floor(gx), y0 = Math.floor(gy);
                let fx = gx - x0, fy = gy - y0;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                const xa = x0 % g, xb = (x0 + 1) % g;
                const ya = (y0 % g) * g, yb = ((y0 + 1) % g) * g;
                const a = grid[ya + xa] + (grid[ya + xb] - grid[ya + xa]) * fx;
                const b = grid[yb + xa] + (grid[yb + xb] - grid[yb + xa]) * fx;
                return a + (b - a) * fy;
            };
        }
        // Fractal sum of octaves (each integer-frequency → the whole stack
        // tiles). Low octaves give swell, high octaves give chop.
        const octs = [
            { f: octave(3), a: 0.50 },
            { f: octave(7), a: 0.28 },
            { f: octave(13), a: 0.15 },
            { f: octave(29), a: 0.07 },
        ];
        function height(x, y) {
            const u = x / S, v = y / S;
            let h = 0;
            for (const o of octs) h += o.f(u, v) * o.a;
            return h;
        }
        const cvs = document.createElement('canvas');
        cvs.width = S; cvs.height = S;
        const ctx = cvs.getContext('2d');
        const img = ctx.createImageData(S, S);
        for (let y = 0; y < S; y++) {
            for (let x = 0; x < S; x++) {
                const nx = (height((x - 1 + S) % S, y) - height((x + 1) % S, y)) * WATER_RIPPLE;
                const ny = (height(x, (y - 1 + S) % S) - height(x, (y + 1) % S)) * WATER_RIPPLE;
                const len = Math.hypot(nx, ny, 1.0);
                const i = (y * S + x) * 4;
                img.data[i]     = Math.round((nx / len * 0.5 + 0.5) * 255);
                img.data[i + 1] = Math.round((ny / len * 0.5 + 0.5) * 255);
                img.data[i + 2] = Math.round((1.0 / len * 0.5 + 0.5) * 255);
                img.data[i + 3] = 255;
            }
        }
        ctx.putImageData(img, 0, 0);
        const tex = new THREE.CanvasTexture(cvs);
        tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
        tex.colorSpace = THREE.NoColorSpace; // normal data is linear, not sRGB
        waterNormalsTex[variant] = tex;
        return tex;
    }

    // Caustic texture — loaded once from caustics.jpg in this folder. Used
    // for the caustic web on the water bed instead of a procedural sine
    // pattern (sines never look quite right).
    let _causticTex = null;
    function causticTexture() {
        if (_causticTex) return _causticTex;
        const t = new THREE.TextureLoader().load('caustics.jpg');
        t.wrapS = t.wrapT = THREE.RepeatWrapping;
        t.colorSpace = THREE.SRGBColorSpace;
        _causticTex = t;
        return t;
    }

    // World units per normal-map tile. WaterMesh samples ripples from the
    // geometry's uv (times `scale`), so the uv must carry a sane world scaling
    // — without this, ShapeGeometry's default uv (raw world coords, hundreds of
    // units) tiles the normal map into invisibility and the water reads glassy.
    const WATER_UV_TILE = 28;

    // Rewrite a water surface geometry's uv from its local XY position so the
    // ripple normal map tiles every WATER_UV_TILE world units, regardless of
    // how big the river / lake is. Both the ribbon BufferGeometry and the lake
    // ShapeGeometry are authored in local XY (x = world x, y = -world z).
    function applyWaterUVs(geo) {
        const pos = geo.attributes.position;
        const uv = new Float32Array(pos.count * 2);
        for (let i = 0; i < pos.count; i++) {
            uv[i * 2]     = pos.getX(i) / WATER_UV_TILE;
            uv[i * 2 + 1] = pos.getY(i) / WATER_UV_TILE;
        }
        geo.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    }

    // WaterMesh (flow-map water from the three.js WebGPU example) for a river /
    // lake geometry — reflection + refraction + flowing normals, colour-tinted
    // by the spline's fill colour. The geometry must carry a baked `waterDepth`
    // attribute (0 at the shore, 1 in the interior); makeWater wraps the
    // material's colour node to fake a recessed basin from it: the scene below
    // is re-sampled with a view + depth parallax and darkened like deep water,
    // then blended under WaterMesh's own reflection / ripple by the depth.
    function makeWater(geometry) {
        applyWaterUVs(geometry);
        const water = new WaterMesh(geometry, {
            // Per-vertex tint comes from the `waterFill` / `waterRim`
            // attributes in the merged geometry — keep WaterMesh's own colour
            // uniform white so its reflection isn't double-tinted.
            color: new THREE.Color(1, 1, 1),
            scale: WATER_NORMAL_SCALE,
            scale2: WATER_NORMAL_SCALE2,
            reflectDistort: WATER_REFLECT_DISTORT,
            flowSpeed: 0.12,    // flow direction comes from the baked `flowDir` attribute
            reflectivity: 0.08,
            normalMap0: waterNormalsTexture(0),
            normalMap1: waterNormalsTexture(1),
            // Opaque water: surface output reflects sky/scene above but never
            // shows the landscape underneath. The wrapper provides its own
            // depth tint for the body colour.
            reflectionOnly: true,
        });
        // Vertex-displacement waves: sum of three travelling sin waves on the
        // local (worldX, -worldZ) plane, summed along local Z (vertical after
        // the -90deg X rotation of the mesh). Amplitude is the per-vertex
        // baked dRaw lerped between SHALLOW and DEEP, so shore vertices barely
        // move while open water rolls.
        if (WATER_WAVE_SHALLOW > 0 || WATER_WAVE_DEEP > 0) {
            const dWave = attribute('waterDepth').toVar();
            const ampWave = mix(WATER_WAVE_SHALLOW, WATER_WAVE_DEEP, dWave);
            const tWave = time.mul(WATER_WAVE_SPEED).toVar();
            // Local XY here is (worldX, -worldZ) since the lake/river geometry
            // is authored that way and laid flat by the mesh's -90deg rotation.
            const lx = positionLocal.x;
            const ly = positionLocal.y;
            const w1 = lx.mul(0.35).add(ly.mul(0.20)).add(tWave).sin().mul(0.55);
            const w2 = lx.mul(0.18).sub(ly.mul(0.32)).add(tWave.mul(0.83)).sin().mul(0.35);
            const w3 = lx.mul(0.07).add(ly.mul(0.12)).add(tWave.mul(0.41)).sin().mul(0.45);
            const dispZ = w1.add(w2).add(w3).mul(ampWave);
            water.material.positionNode = vec3(lx, ly, positionLocal.z.add(dispZ));
        }
        // Visible from above and from below (when flying underwater).
        water.material.side = THREE.DoubleSide;
        water.material.transparent = false;
        water.material.depthWrite = true;
        // Disable built-in shadow reception — we render only the custom
        // depth-aware shadow below. Otherwise three's auto path can also
        // apply a surface-projected shadow, layered on top of ours.
        water.receiveShadow = false;
        water.castShadow = false;
        water.material.lights = false;
        const surface = water.material.colorNode; // WaterMesh reflection + ripple
        // Per-vertex tints — read from the merged geometry's baked attributes.
        // Shallow = rim colour, deep = fill colour * WATER_DEEP_MULT. Vertex
        // attributes interpolate naturally across junctions, so two rivers of
        // different colours blend smoothly where their meshes meet.
        const shallowRGB = attribute('waterRim', 'vec3').toVar();
        const deepRGB = attribute('waterFill', 'vec3').mul(WATER_DEEP_MULT).toVar();
        const foamRGB = (function () {
            const c = toColor(WATER_FOAM_COLOR, 0xe6eef0);
            return vec3(c.r, c.g, c.b);
        })();
        const scatterRGB = (function () {
            const c = toColor(WATER_SCATTER_COLOR, 0xd8eccd);
            return vec3(c.r, c.g, c.b);
        })();
        const causticRGB = (function () {
            const c = toColor(WATER_CAUSTIC_COLOR, 0xfff5d0);
            return vec3(c.r, c.g, c.b);
        })();
        const sunDirVec = getSunDirUniform();
        const foamNoiseTex = texture(waterNormalsTexture(0));
        const causticTex = texture(causticTexture());
        // Tell three.js's shadow() to sample at a bed point under the water
        // (water surface dropped straight down by dRaw * WATER_BED_DEPTH).
        // With the sun tilted, that bed-point projects to a different shadow
        // UV than the surface — the building's shadow on the water then bends
        // into the deeper part of the lake, matching the demo's behaviour.
        // shadow() reads material.receivedShadowPositionNode and uses it in
        // place of the surface position for both UV and depth.
        const bedPosNode = vec3(
            positionWorld.x,
            positionWorld.y.sub(attribute('waterDepth').mul(WATER_BED_DEPTH)),
            positionWorld.z);
        water.material.receivedShadowPositionNode = bedPosNode;

        water.material.colorNode = Fn(() => {
            // Baked shore distance — raw for the foam + shadow bed projection,
            // scaled+clamped for the depth tint (BASIN_DEPTH stretches reach).
            const dRaw = attribute('waterDepth').toVar();
            const d = dRaw.mul(BASIN_DEPTH).clamp(0, 1).toVar();
            // Depth tint: shallow = fill colour, deep = WATER_DEEP_MULT
            // shade. WATER_DEEP_TINT controls how much of the scene under-
            // neath bleeds through at the shore (1 = opaque).
            const eye = normalize(cameraPosition.sub(positionWorld));
            const parUV = screenUV.sub(eye.xz.mul(d).mul(0.0));
            const bed = viewportSharedTexture(viewportSafeUV(parUV)).rgb;
            const shallow = mix(bed, shallowRGB, WATER_DEEP_TINT);
            const depthColor = mix(shallow, deepRGB, d);
            // Base: depth tint with reflection laid on top. Surface comes
            // back from WaterMesh's reflectionOnly path as
            //   mix(white, reflection, fresnel)
            // — multiplying by depthColor turns it into the same fresnel mix
            // but with the water's own tint as the base, so WATER_SHINE
            // controls "how reflective" without bleaching the water white.
            const tintedSurface = surface.rgb.mul(depthColor);
            let col = mix(depthColor, tintedSurface, WATER_SHINE);

            // Scatter halo: warm forward lobe toward the sun. Added later
            // (after shadow), so the sun glare reads across shadowed water.
            const fwd = eye.dot(sunDirVec).max(0);
            const scatter = fwd.pow(WATER_SCATTER_POWER).mul(d).mul(WATER_SCATTER);

            // --- Caustics: sampled from caustics.jpg, animated, world-anchored.
            // Parallax-shifted by view+depth. Pattern scale is depth-independent
            // (uniform world-space tile size) — depth attenuation lives in cMask
            // so DEPTH_SCALE controls a single, intuitive thing: how aggressively
            // caustics fade out at depth.
            const cParallaxShift = eye.xz.mul(d).mul(WATER_CAUSTIC_PARALLAX);
            const cScale = WATER_CAUSTIC_MIN_SCALE * WATER_CAUSTIC_SCALE;
            const cP = positionWorld.xz.sub(cParallaxShift).mul(cScale).toVar();
            const ct = time.mul(WATER_CAUSTIC_SPEED);
            const cUV0 = cP.add(vec2(ct, ct.mul(0.7)));
            const cUV1 = cP.mul(1.31).add(vec2(ct.mul(-0.83), ct.mul(0.91)));
            const c0 = causticTex.sample(cUV0).r;
            const c1 = causticTex.sample(cUV1).r;
            const caustic = c0.mul(c1).pow(WATER_CAUSTIC_SHARP).mul(6);
            // Depth fade: at d=0 caustics full bright, at d=1 they fade.
            // DEPTH_SCALE = exponent on (1 - d). 0 → uniform everywhere.
            // 1 → linear falloff. 2 → squared (default — shallow keeps most).
            // 4+ → caustics restricted to thin shallow rim. smoothstep(0,0.03)
            // kills caustics in the literal foam zone so they don't fight foam.
            const cMask = d.smoothstep(0, 0.03)
                .mul(d.oneMinus().pow(WATER_CAUSTIC_DEPTH_SCALE));
            const sunOverhead = sunDirVec.y.max(0);

            // Shadow mask via three.js's built-in TSL shadow(). The sampling
            // position is overridden on the material below (receivedShadow-
            // PositionNode = bedPos), so this samples the shadow at the
            // virtual bed under the water — building shadows bend into the
            // deeper part as in the demo.
            const shadowMask = sun && sun.castShadow ? shadow(sun) : sunOverhead.mul(0).add(1);

            // Shadow strength = DARKEN * depth-fade. With DARKEN=0 the shadow
            // is invisible everywhere — both the base water darken below AND
            // the caustic kill further down scale by `shadowStr`, so the knob
            // is a single shut-off valve. DEPTH_FADE controls how fast the
            // shadow fades with water depth (deep water self-tints dark, a
            // shadow on top reads wrong otherwise).
            const shadowFade = d.oneMinus().pow(WATER_SHADOW_DEPTH_FADE);
            const shadowStr = shadowFade.mul(WATER_SHADOW_DARKEN);
            // shadowMask = 1 means lit (no shadow on this fragment). The
            // amount we darken is (1 - shadowMask) * shadowStr.
            const shadowAmount = shadowMask.oneMinus().mul(shadowStr);
            col = col.mul(shadowAmount.oneMinus());

            // Scatter halo on top of shadow — sun glare reads across shadow
            // (it's a specular-ish surface effect, not occluded by bed shadow).
            col = col.add(scatterRGB.mul(scatter));

            // Caustics added LAST, ON TOP. Shadow kill on caustics uses
            // WATER_SHADOW_DARKEN directly (NOT depth-faded — caustics in deep
            // water under a shadow should still die; the depth-fade is only
            // about the base-water darken which would otherwise muddy already
            // dark deep water). At DARKEN=0 caustics ignore shadow; at
            // DARKEN=1 shadowed water has zero caustics. Caustic colour
            // absorbs water colour with depth (Beer-Lambert-ish).
            const causticTinted = mix(causticRGB, shallowRGB, d.mul(WATER_CAUSTIC_TINT));
            const causticShadow = mix(shadowMask.mul(0).add(1), shadowMask, 1);
            col = col.add(causticTinted.mul(caustic).mul(cMask)
                .mul(causticShadow).mul(sunOverhead).mul(WATER_CAUSTIC));

            // --- Shoreline foam: white lapping band at the rim, animated.
            const foamUV = uv().mul(WATER_FOAM_SCALE)
                .add(vec3(time, time, 0).xy.mul(WATER_FOAM_DRIFT));
            const fNoise = foamNoiseTex.sample(foamUV).r;
            const sway = time.mul(WATER_FOAM_SPEED).add(fNoise.mul(6.283)).sin()
                .mul(0.5).add(0.5);
            const foamEdge = sway.mul(WATER_FOAM_WIDTH * 0.6)
                .add(WATER_FOAM_WIDTH * 0.4);
            const foam = dRaw.smoothstep(0.0, foamEdge).oneMinus()
                .mul(fNoise.mul(0.6).add(0.4));

            return vec4(mix(col, foamRGB, foam.clamp(0, 1)), 1.0);
        })();
        return water;
    }

    // --- Water bodies: surface geometry with baked, faked depth ------------

    function smoothstep01(t) {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return t * t * (3 - 2 * t);
    }

    // Signed area (shoelace) of a polygon [{x,y}].
    function polygonArea(poly) {
        let a = 0;
        for (let i = 0, j = poly.length - 1; i < poly.length; j = i++)
            a += (poly[j].x + poly[i].x) * (poly[j].y - poly[i].y);
        return a / 2;
    }

    // Shortest distance from point (px,py) to segment (x1,y1)-(x2,y2).
    function distToSeg(px, py, x1, y1, x2, y2) {
        const dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy;
        let t = l2 ? ((px - x1) * dx + (py - y1) * dy) / l2 : 0;
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return Math.hypot(px - (x1 + t * dx), py - (y1 + t * dy));
    }

    // Shortest distance from a point to a polygon's boundary.
    function distToPolygon(px, py, poly) {
        let min = Infinity;
        for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
            const d = distToSeg(px, py, poly[j].x, poly[j].y, poly[i].x, poly[i].y);
            if (d < min) min = d;
        }
        return min;
    }

    // Even-odd ray cast — is (px, py) inside the polygon (whatever winding)?
    function pointInPolygon(px, py, poly) {
        let inside = false;
        for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
            const xi = poly[i].x, yi = poly[i].y;
            const xj = poly[j].x, yj = poly[j].y;
            const intersect = ((yi > py) !== (yj > py))
                && (px < (xj - xi) * (py - yi) / (yj - yi + 1e-9) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    // For a sampled centreline, build the closed ribbon outline (left bank
    // forward + right bank reversed). `extraHalf` is added to s.w/2 so road
    // casings can be expanded to their outer edge for grass occlusion.
    function ribbonOutlinePolygon(samples, extraHalf) {
        const n = samples.length;
        if (n < 2) return [];
        const tan = [];
        for (let i = 0; i < n; i++) {
            const prev = samples[Math.max(0, i - 1)];
            const next = samples[Math.min(n - 1, i + 1)];
            const dx = next.x - prev.x, dy = next.y - prev.y;
            const len = Math.hypot(dx, dy) || 1;
            tan.push({ x: dx / len, y: dy / len });
        }
        const left = [], right = [];
        for (let i = 0; i < n; i++) {
            const s = samples[i], t = tan[i];
            const nx = -t.y, ny = t.x, hw = (s.w || 4) / 2 + (extraHalf || 0);
            left.push({ x: s.x + nx * hw, y: s.y + ny * hw });
            right.push({ x: s.x - nx * hw, y: s.y - ny * hw });
        }
        return left.concat(right.reverse());
    }

    function riverOutlinePolygon(samples) {
        return ribbonOutlinePolygon(samples, 0);
    }

    // Midpoint-subdivide a flat triangle list (9 floats per triangle) `levels`
    // times — each pass turns one triangle into four. Used to give the lake bed
    // enough interior vertices to actually curve.
    function subdivideTris(tris, levels) {
        let cur = tris;
        for (let l = 0; l < levels; l++) {
            const next = [];
            for (let i = 0; i < cur.length; i += 9) {
                const ax = cur[i], ay = cur[i + 1], az = cur[i + 2];
                const bx = cur[i + 3], by = cur[i + 4], bz = cur[i + 5];
                const cx = cur[i + 6], cy = cur[i + 7], cz = cur[i + 8];
                const abx = (ax + bx) / 2, aby = (ay + by) / 2, abz = (az + bz) / 2;
                const bcx = (bx + cx) / 2, bcy = (by + cy) / 2, bcz = (bz + cz) / 2;
                const cax = (cx + ax) / 2, cay = (cy + ay) / 2, caz = (cz + az) / 2;
                next.push(ax, ay, az, abx, aby, abz, cax, cay, caz);
                next.push(abx, aby, abz, bx, by, bz, bcx, bcy, bcz);
                next.push(cax, cay, caz, bcx, bcy, bcz, cx, cy, cz);
                next.push(abx, aby, abz, bcx, bcy, bcz, cax, cay, caz);
            }
            cur = next;
        }
        return cur;
    }

    // Closed water body → a flat surface mesh in local XY (x = world x, y =
    // -world z, so a -90deg X rotation lays it flat with +Z up). The outline is
    // triangulated and subdivided for interior vertices; each vertex carries a
    // baked `waterDepth` (smoothstep of its distance to the shore) and a
    // `flowDir` (a gentle uniform drift — lakes have no current).
    function buildLakeWaterGeo(outline) {
        if (!outline || outline.length < 3) return null;
        if (Math.abs(polygonArea(outline)) < 1) return null;
        const shape = new THREE.Shape();
        shape.moveTo(outline[0].x, outline[0].y);
        for (let i = 1; i < outline.length; i++)
            shape.lineTo(outline[i].x, outline[i].y);
        shape.closePath();
        const sg = new THREE.ShapeGeometry(shape).toNonIndexed();
        let tris = Array.from(sg.attributes.position.array); // (worldX, worldZ, 0)
        sg.dispose();
        if (tris.length === 0) return null;
        // LOD: drop one subdivision level for lakes whose centroid sits past
        // WATER_LAKE_LOD_FAR from the current camera position. Cheap, evaluated
        // once at build; rebuildScene re-runs when the camera shifts enough.
        let cx = 0, cy = 0, n = 0;
        for (let i = 0; i < tris.length; i += 3) {
            cx += tris[i]; cy += tris[i + 1]; n++;
        }
        cx /= n; cy /= n;
        let camDist = 0;
        if (camera) {
            const dx = cx - camera.position.x, dz = cy - camera.position.z;
            camDist = Math.hypot(dx, dz);
        }
        const subdivLevels = camDist > WATER_LAKE_LOD_FAR
            ? WATER_LAKE_SUBDIV_FAR : WATER_LAKE_SUBDIV_BASE;
        tris = subdivideTris(tris, subdivLevels);
        // First pass: position + raw shore distance, tracking the deepest point
        // so the depth fade is normalised to THIS lake's shape (an elongated
        // lake's middle is far closer to shore than an equal-area circle's).
        const pos = [], dist = [];
        let maxD = 1e-6;
        for (let i = 0; i < tris.length; i += 3) {
            const wx = tris[i], wz = tris[i + 1];
            pos.push(wx, -wz, 0); // local XY for the -90deg X rotation
            const dd = distToPolygon(wx, wz, outline);
            dist.push(dd);
            if (dd > maxD) maxD = dd;
        }
        // Basin curve: gentle shallow SHELF along the outer rim, then a smooth
        // ramp all the way to the centre. NO flatline plateau — a bird's-eye
        // top-down lake view shows the centre across most of the screen, so a
        // plateau there makes most of the visible water read as one saturated
        // depth (caustics + tint look uniform). Smooth ramp keeps the dRaw
        // gradient continuous across the whole surface; only the very centre
        // reaches d=1 after BASIN_DEPTH scaling.
        const reach = maxD * WATER_DEEP_REACH;
        const SHELF = 0.10;
        function basinCurve(m) {
            if (m <= 0) return 0;
            if (m >= 1) return 1;
            if (m < SHELF) {
                const t = m / SHELF;
                return 0.12 * t * t * (3 - 2 * t); // shelf up to ~0.12
            }
            const t = (m - SHELF) / (1 - SHELF);
            return 0.12 + 0.88 * t * t * (3 - 2 * t); // smooth ramp to 1 at centre
        }
        const depth = dist.map(dd => basinCurve(dd / reach));
        // Lakes have no current — a gentle uniform drift keeps ripples alive.
        const flow = [];
        for (let i = 0; i < depth.length; i++) flow.push(0.5, 0.35);
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        geo.setAttribute('waterDepth', new THREE.Float32BufferAttribute(depth, 1));
        geo.setAttribute('flowDir', new THREE.Float32BufferAttribute(flow, 2));
        geo.computeVertexNormals();
        return geo;
    }

    // Open river → a flat ribbon surface in local XY with cross-section rows.
    // `waterDepth` runs 0 at the banks to 1 along the centreline. `flowDir`
    // follows the centreline tangent, plus a curvature-driven cross-flow kick
    // on bends (turbulence) and a center-fast / bank-slow speed taper.
    function buildRiverWaterGeo(samples) {
        if (!samples || samples.length < 2) return null;
        // Build CROSS via cubic-spaced taper, optionally densified by
        // WATER_RIVER_SUBDIV (inserting K-1 evenly-spaced steps between each
        // pair). The d=0 banks must stay exactly at f=±1 so foam still lines
        // up with the rim.
        const BASE_CROSS = [-1, -0.6, -0.25, 0, 0.25, 0.6, 1];
        const sub = Math.max(1, WATER_RIVER_SUBDIV | 0);
        const CROSS = [];
        for (let i = 0; i < BASE_CROSS.length - 1; i++) {
            const a = BASE_CROSS[i], b = BASE_CROSS[i + 1];
            for (let k = 0; k < sub; k++) CROSS.push(a + (b - a) * k / sub);
        }
        CROSS.push(BASE_CROSS[BASE_CROSS.length - 1]);
        // Lengthwise: linearly interpolate extra samples between each pair so
        // there are sub-1 in-between rows per gap. Each new sample inherits a
        // smoothed tangent so the local flow direction stays sensible.
        let denseSamples;
        if (sub === 1) {
            denseSamples = samples;
        } else {
            denseSamples = [];
            for (let i = 0; i < samples.length - 1; i++) {
                const a = samples[i], b = samples[i + 1];
                for (let k = 0; k < sub; k++) {
                    const t = k / sub;
                    denseSamples.push({
                        x: a.x + (b.x - a.x) * t,
                        y: a.y + (b.y - a.y) * t,
                        w: (a.w || 4) + ((b.w || 4) - (a.w || 4)) * t,
                    });
                }
            }
            denseSamples.push(samples[samples.length - 1]);
        }
        samples = denseSamples;
        const n = samples.length;
        // Per-sample unit tangent (smoothed) along the centreline.
        const tan = [];
        for (let i = 0; i < n; i++) {
            const prev = samples[Math.max(0, i - 1)];
            const next = samples[Math.min(n - 1, i + 1)];
            const dx = next.x - prev.x, dy = next.y - prev.y;
            const len = Math.hypot(dx, dy) || 1;
            tan.push({ x: dx / len, y: dy / len });
        }
        const rows = [];
        for (let i = 0; i < n; i++) {
            const s = samples[i];
            const t = tan[i];
            const nx = -t.y, ny = t.x, hw = (s.w || 4) / 2;
            // Curvature: signed turn between the tangents two samples either
            // side (~sin of the bend angle). Drives a cross-flow so the water
            // churns on bends instead of sliding straight through.
            const tp = tan[Math.max(0, i - 2)], tn = tan[Math.min(n - 1, i + 2)];
            const turn = tp.x * tn.y - tp.y * tn.x;
            // World-space flow = tangent + curvature cross-flow, renormalised.
            let fwx = t.x + nx * turn * WATER_TURB;
            let fwy = t.y + ny * turn * WATER_TURB;
            const fl = Math.hypot(fwx, fwy) || 1;
            fwx /= fl; fwy /= fl;
            const row = [];
            for (const f of CROSS) {
                // Mid-channel runs fast, banks creep — steep falloff so the
                // speed difference actually reads (a linear taper looks uniform
                // across most of the width).
                const speed = 0.05 + 0.95 * Math.pow(1 - Math.abs(f), WATER_FLOW_TAPER);
                const mag = WATER_FLOW * speed;
                row.push({
                    x: s.x + nx * f * hw,
                    z: s.y + ny * f * hw,
                    d: smoothstep01((1 - Math.abs(f)) / WATER_DEEP_REACH),
                    // Flow in geometry's local XY (x = world x, y = -world z).
                    // WaterNode's setup() does `flow.x *= -1` internally; the
                    // sampler then offsets uv BY flow*offset, so the pattern
                    // appears to move OPPOSITE the flow vector. To get the
                    // texture to scroll downstream (along +tangent in world),
                    // we feed flow = +tangent (no negation); WaterNode flips
                    // x, the negative uv offset moves the pattern toward +x.
                    // Y axis: local y = -world z, and WaterNode doesn't flip
                    // y, so to scroll the pattern toward +worldZ we feed
                    // flow.y = +world_tangent_y (because uv.y INCREASING
                    // makes pattern visually move toward -y_local = +worldZ).
                    fx: fwx * mag, fy: fwy * mag,
                });
            }
            rows.push(row);
        }
        const pos = [], depth = [], flow = [];
        const v = p => {
            pos.push(p.x, -p.z, 0);
            depth.push(p.d);
            flow.push(p.fx, p.fy);
        };
        for (let i = 0; i < rows.length - 1; i++) {
            const A = rows[i], B = rows[i + 1];
            for (let j = 0; j < CROSS.length - 1; j++) {
                const a = A[j], b = A[j + 1], c = B[j + 1], d = B[j];
                v(a); v(b); v(c);
                v(a); v(c); v(d);
            }
        }
        if (pos.length === 0) return null;
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        geo.setAttribute('waterDepth', new THREE.Float32BufferAttribute(depth, 1));
        geo.setAttribute('flowDir', new THREE.Float32BufferAttribute(flow, 2));
        geo.computeVertexNormals();
        return geo;
    }

    // Concatenate position + normal of several non-indexed geometries into one
    // (drops uv — merged building geometry is untextured).
    function mergeGeometries(geoms) {
        let total = 0;
        for (const g of geoms) total += g.attributes.position.count;
        const pos = new Float32Array(total * 3);
        const nrm = new Float32Array(total * 3);
        let off = 0;
        for (const g of geoms) {
            const p = g.attributes.position.array;
            pos.set(p, off * 3);
            if (g.attributes.normal) nrm.set(g.attributes.normal.array, off * 3);
            off += g.attributes.position.count;
        }
        const out = new THREE.BufferGeometry();
        out.setAttribute('position', new THREE.BufferAttribute(pos, 3));
        out.setAttribute('normal', new THREE.BufferAttribute(nrm, 3));
        return out;
    }

    // Group meshes by shared material and merge each group into a single mesh
    // (bakes each mesh's transform into the geometry). Cuts draw calls hard for
    // floor-plan-heavy buildings. Single-mesh groups pass through untouched.
    function mergeByMaterial(meshes) {
        const groups = new Map();
        for (const m of meshes) {
            if (!groups.has(m.material)) groups.set(m.material, []);
            groups.get(m.material).push(m);
        }
        const out = [];
        groups.forEach((list, mat) => {
            if (list.length === 1) { out.push(list[0]); return; }
            const geoms = list.map(m => {
                m.updateMatrix();
                const g = m.geometry.index
                    ? m.geometry.toNonIndexed() : m.geometry.clone();
                g.applyMatrix4(m.matrix);
                return g;
            });
            const merged = new THREE.Mesh(mergeGeometries(geoms), mat);
            for (const g of geoms) g.dispose();
            out.push(merged);
        });
        return out;
    }

    // Bounds over all current map content. Used to size the ground plane and to
    // frame the camera on enter.
    function mapBounds() {
        let minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
        function acc(x, y) {
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y;
        }
        try {
            (function walk(nodes) {
                for (const n of nodes || []) {
                    for (const im of n.images || []) {
                        acc(im.x, im.y);
                        acc(im.x + (im.width || 0), im.y + (im.height || 0));
                    }
                    for (const sp of n.splines || [])
                        for (const p of sp.points || []) acc(p.x, p.y);
                    for (const sh of n.shapes || [])
                        for (const p of sh.points || []) acc(p.x, p.y);
                    for (const bl of n.buildings || [])
                        for (const p of bl.footprint || []) acc(p.x, p.y);
                    for (const pn of n.pins || []) acc(pn.x, pn.y);
                    walk(n.children);
                }
            })((mapData && (mapData.layers || mapData.groups)) || []);
        } catch (e) { /* fall through to default bounds */ }
        if (minX > maxX) { minX = 0; minY = 0; maxX = 600; maxY = 600; }
        return { minX: minX, minY: minY, maxX: maxX, maxY: maxY };
    }

    const DEG = Math.PI / 180;

    // renderOrder bands for the flat ground stack (all depthWrite:false, painted
    // back-to-front). Buildings (Phase 4) render above this with normal depth.
    const RO_BACKDROP = 0;
    const RO_IMAGE = 100;
    const RO_TERRAIN = 1000;
    const RO_SPLINE = 5000;   // casing band, fill band (+2000), water band (+4000)
    const RO_BUILDING = 20000; // 3D volumes — above the flat ground stack, normal depth
    const RO_BILLBOARD = 30000; // label sprites / pins — drawn over everything

    function mapLayers() {
        return (mapData && (mapData.layers || mapData.groups)) || [];
    }

    // Hex (#rrggbb or number) → THREE.Color, with a fallback.
    function toColor(hex, fallback) {
        try {
            if (hex == null || hex === '') return new THREE.Color(fallback);
            return new THREE.Color(hex);
        } catch (e) { return new THREE.Color(fallback); }
    }

    // Set of layer-node ids that are currently visible: parent `hidden` cascades,
    // and a connected-set node shows only its active child. Per the locked
    // decision, opacity and per-element / per-layer zoom ranges are ignored.
    function visibleLayerIds() {
        const ids = new Set();
        (function recurse(nodes, parentVisible) {
            for (const n of nodes || []) {
                const visible = parentVisible && !n.hidden;
                if (visible) ids.add(n.id);
                const isConnected = n.isConnectedSet && n.children && n.children.length;
                const activeChildId = isConnected
                    ? (n.defaultMemberLayerId || (n.children[0] && n.children[0].id))
                    : null;
                for (const c of n.children || [])
                    recurse([c], visible && (!isConnected || c.id === activeChildId));
            }
        })(mapLayers(), true);
        return ids;
    }

    // Whether an element of the given kind / id on the given layer renders.
    // While anything is isolated, ONLY the isolated element shows.
    function elementShown(kind, id, layerId, visIds) {
        if (typeof isolatedId !== 'undefined' && isolatedId)
            return isolatedKind === kind && isolatedId === id;
        if (!layerId) return true; // unassigned (legacy) → ignores the layer cascade
        return visIds.has(layerId);
    }

    // Terrain shapes → flat triangulated polygons on the ground. Each shape gets
    // a tiny y offset by draw order so overlapping shapes (grass under forest)
    // don't z-fight.
    function buildTerrain(root, visIds) {
        let order = 0;
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const sh of n.shapes || []) {
                    if (!elementShown('shape', sh.id, n.id, visIds)) continue;
                    const pts = sh.points || [];
                    if (pts.length < 3) continue;
                    const shape = new THREE.Shape();
                    shape.moveTo(pts[0].x, pts[0].y);
                    for (let i = 1; i < pts.length; i++) shape.lineTo(pts[i].x, pts[i].y);
                    shape.closePath();
                    const geo = new THREE.ShapeGeometry(shape);
                    const mesh = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({
                        color: toColor(sh.color, 0x6f8f4f),
                        side: THREE.DoubleSide,
                        // Ground stack: no depth writes + a strict renderOrder
                        // painter's stack. Sidesteps depth-buffer precision
                        // entirely, so nothing in the flat ground layer can
                        // z-fight at any distance.
                        depthWrite: false,
                    }));
                    // Shape is authored in XY; rotation.x = +90° lays it on XZ
                    // with our (x, y_up, z=south) convention.
                    mesh.rotation.x = Math.PI / 2;
                    mesh.position.y = 0.02;
                    mesh.renderOrder = RO_TERRAIN + order;
                    mesh.receiveShadow = true;
                    order++;
                    root.add(mesh);
                }
                walk(n.children);
            }
        })(mapLayers());
    }

    // ===== Procedural grass =====
    // Sampling is LAZY and tile-based around the camera. buildGrass() only
    // records the grass polygons; tiles are sampled and added to the scene on
    // demand from updateGrassVisibility(). A shader-side fade shrinks blade
    // height to 0 near the cull radius so tile pop is invisible.
    const GRASS_DENSITY = 4.0;           // blades per world unit² at full density
    const GRASS_TILE_SIZE = 20;          // world units per cached tile
    const GRASS_NEAR_RADIUS = 1;        // full density inside this from camera
    const GRASS_RENDER_RADIUS = 1200;     // density fades to 0 at this radius
    // Falloff shape between NEAR and RENDER. Higher = sharper drop.
    //   2  → gentle quadratic
    //   4  → recommended: noticeable steepness without a hard ring
    //   8+ → tight ring of dense grass, nearly empty past mid-radius
    const GRASS_FALLOFF_POWER = 16;
    // Beyond this distance from the camera, a tile drops the per-blade
    // InstancedMesh and instead renders a flat textured quad (the texture is
    // pre-rendered once from the actual blade shader). Cheap on GPU, lossless
    // visually at the distances where individual blades don't read anyway.
    const GRASS_BILLBOARD_DISTANCE = 2000;
    // Max simultaneous billboard tiles. Bounded by tiles in (BILLBOARD_DISTANCE
    // .. RENDER_RADIUS) ring: ceil(π × (R² − B²) / TILE²) + headroom.
    const GRASS_BILLBOARD_CAPACITY = 0;
    const GRASS_BILLBOARD_TEX_RES = 512; // texture resolution per side

    // ===== Procedural trees =====
    // Same pool / tile / cache pattern as grass, but with a low-poly tree
    // mesh (cylinder trunk + cone foliage) and a standing camera-facing
    // billboard quad as the far-LOD substitute.
    const TREE_DENSITY = 0.00125;             // trees per world unit² (raise = more dense forest)
    const TREE_SCALE = 7;                   // base scale multiplier on top of GLB normalisation
    const TREE_TILE_SIZE = 40;
    const TREE_NEAR_RADIUS = 1;             // full density inside this
    const TREE_RENDER_RADIUS = 600;         // density fades to 0 here
    const TREE_FALLOFF_POWER = 2;
    const TREE_BILLBOARD_DISTANCE = 200;    // beyond this: standing billboard
    const TREE_BILLBOARD_CAPACITY = 2000;
    const TREE_BILLBOARD_TEX_RES = 256;
    const TREE_POOL_SIZE = 200;
    const TREE_MAX_PER_TILE = 128;          // 40² × 0.05 = 80, +headroom
    const TREE_TRUNK_HEIGHT = 3;
    const TREE_TRUNK_RADIUS = 0.25;
    const TREE_FOLIAGE_HEIGHT = 4;
    const TREE_FOLIAGE_RADIUS = 2;
    const TREE_TRUNK_COLOR = 0x6b4423;
    const TREE_FOLIAGE_COLOR = 0x3c6b2a;
    const TREE_TICK_BUDGET_MS = 3;
    // Trees are sparse enough (0.05/m²) that the entire forest fits in one
    // InstancedMesh per material — no streaming, no LOD, no billboard. Set
    // false to fall back to the pool / tile streaming code.
    const TREE_STATIC_MODE = true;
    // Pool: pre-allocate N max-sized InstancedMesh objects on scene entry.
    // sampleGrassTile reuses one of these by overwriting its instance buffers
    // (typed-array .set + needsUpdate=true), avoiding the per-new-tile GPU
    // resource allocation / bind-group setup that was costing ~30 ms PER NEW
    // TILE inside renderer.render() in the previous design. When the pool is
    // exhausted, the furthest-from-camera slot is evicted to make room.
    const GRASS_POOL_SIZE = 500;            // covers RENDER_RADIUS with headroom
    const GRASS_MAX_BLADES_PER_TILE = 6400; // 40² × 1.28 — safe upper bound
    // Sampling is bounded by time, not count. The prime tick on first entry
    // builds every tile in the radius synchronously (one entry hitch is fine);
    // every subsequent tick stops sampling new tiles once it has used this
    // many ms, so movement-driven tile spawns can never spike a frame.
    // Higher = backlog clears faster (less visible "missing tile" gaps when
    // moving fast); lower = more FPS headroom. With pool reuse the per-sample
    // cost is dominated by JS-side PiP tests, not GPU resource creation, so
    // this can be generous.
    const GRASS_TICK_BUDGET_MS = 2;
    const GRASS_DEBUG = true;            // dumps [grass] metrics to console once/sec

    const grassDebug = {
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
    function grassDebugReport(now) {
        if (!GRASS_DEBUG) return;
        if (grassDebug.lastReport === 0) { grassDebug.lastReport = now; return; }
        if (now - grassDebug.lastReport < 1000) return;
        let drawn = 0, cached = 0;
        for (const t of grassTileCache.values()) {
            cached++;
            if (t && t.visible && t.count > 0) drawn++;
        }
        const meanSample = grassDebug.sampleCount > 0
            ? (grassDebug.sampleMs / grassDebug.sampleCount).toFixed(2) : '0';
        const meanUpdate = grassDebug.updateCalls > 0
            ? (grassDebug.updateMs / grassDebug.updateCalls).toFixed(2) : '0';
        const meanVisLoop = grassDebug.updateCalls > 0
            ? (grassDebug.visibilityIterMs / grassDebug.updateCalls).toFixed(2) : '0';
        // Route through map.html's global log() so the line reaches the C#
        // host (Console.Error as "[MapJS] ..."). console.log alone would only
        // surface in the WebView2 DevTools, which isn't accessible here.
        const meanRender = grassDebug.renderFrames > 0
            ? (grassDebug.renderMs / grassDebug.renderFrames).toFixed(2) : '0';
        const sceneChildren = grassRoot ? grassRoot.children.length : 0;
        log('[grass]'
            + ' fps=' + grassDebug.renderFrames
            + ' avgRender=' + meanRender + 'ms'
            + ' maxRender=' + grassDebug.renderMaxMs.toFixed(1) + 'ms'
            + ' avgUpdate=' + meanUpdate + 'ms'
            + ' maxUpdate=' + grassDebug.updateMaxMs.toFixed(1) + 'ms'
            + ' avgVisLoop=' + meanVisLoop + 'ms'
            + ' newTiles=' + grassDebug.newTiles
            + ' sampled=' + grassDebug.sampleCount
            + ' (fast=' + grassDebug.sampleFast + ')'
            + ' avgSample=' + meanSample + 'ms'
            + ' cached=' + cached
            + ' drawn=' + drawn
            + ' camXZ=(' + camera.position.x.toFixed(0) + ',' + camera.position.z.toFixed(0) + ')'
            + ' poolFree=' + grassFreeSlots.length
            + ' evictions=' + grassDebug.evictions
            + ' buckets=' + grassPolyBuckets.size
        );
        grassDebug.lastReport = now;
        grassDebug.updateMs = 0;
        grassDebug.updateCalls = 0;
        grassDebug.sampleMs = 0;
        grassDebug.sampleCount = 0;
        grassDebug.sampleFast = 0;
        grassDebug.newTiles = 0;
        grassDebug.visibilityIterMs = 0;
        grassDebug.renderMs = 0;
        grassDebug.renderFrames = 0;
        grassDebug.renderMaxCalls = 0;
        grassDebug.renderMaxMs = 0;
        grassDebug.updateMaxMs = 0;
        grassDebug.evictions = 0;
    }
    const GRASS_BLADE_HEIGHT = 1.0;
    const GRASS_BLADE_WIDTH = 0.3;
    const GRASS_WIND_AMPL = 0.45;
    const GRASS_WIND_FREQ = 0.6;
    const GRASS_FALLBACK_COLOR = 0x6f8f4f;

    // 4-quad tapered ribbon, two columns of verts (left/right), five rows
    // (root → tip). UV.y runs 0 at the root, 1 at the tip, so the wind shader
    // can pivot the sway about the root.
    function makeGrassBladeGeometry() {
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
    function makeGrassMaterial() {
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
    function buildGrassPool() {
        grassPool = [];
        grassFreeSlots = [];
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
            const mesh = new THREE.InstancedMesh(geo, grassMaterial, GRASS_MAX_BLADES_PER_TILE);
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
            grassRoot.add(mesh);
            grassPool.push({
                mesh, geo, rootsAttr, tintsAttr, phasesAttr, tileKey: null,
            });
            grassFreeSlots.push(i);
        }
    }

    // Evict the pool slot whose bound tile is furthest from the camera, free
    // its cache entry, and return the slot's index. The caller will
    // immediately rebind it to a new tile. Used when the free stack is empty.
    function evictFurthestPoolSlot() {
        const cx = camera.position.x, cz = camera.position.z;
        let furthestIdx = -1, furthestD2 = -1;
        for (let i = 0; i < grassPool.length; i++) {
            const e = grassPool[i];
            if (e.tileKey === null) return i; // free slot somehow
            const c = e.mesh.userData.grassCenter;
            const dx = c.x - cx, dz = c.z - cz;
            const d2 = dx * dx + dz * dz;
            if (d2 > furthestD2) { furthestD2 = d2; furthestIdx = i; }
        }
        if (furthestIdx < 0) return -1;
        const ev = grassPool[furthestIdx];
        if (ev.tileKey !== null) {
            grassTileCache.delete(ev.tileKey);
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
    function generateGrassBillboardTexture() {
        if (!renderer || !grassMaterial) return null;
        const W = GRASS_BILLBOARD_TEX_RES, H = GRASS_BILLBOARD_TEX_RES;
        const PATCH = 8; // world units (square half-extent doubled below)

        const subScene = new THREE.Scene();
        // Match main-scene lighting roughly so the texture's shading reads
        // consistent with nearby live blades.
        subScene.add(new THREE.AmbientLight(0xffffff, 0.6));
        const sun = new THREE.DirectionalLight(0xffffcc, 0.85);
        sun.position.set(0.4, 1, 0.3);
        subScene.add(sun);

        const count = Math.max(200, Math.round(PATCH * PATCH * grassEffectiveDensity));
        const geo = makeGrassBladeGeometry();
        const roots = new Float32Array(count * 2);
        const tints = new Float32Array(count * 3);
        const phases = new Float32Array(count);
        const baseColor = new THREE.Color(GRASS_FALLBACK_COLOR);
        const dummy = new THREE.Object3D();
        geo.setAttribute('grassRoot', new THREE.InstancedBufferAttribute(roots, 2));
        geo.setAttribute('grassTint', new THREE.InstancedBufferAttribute(tints, 3));
        geo.setAttribute('grassPhase', new THREE.InstancedBufferAttribute(phases, 1));
        const mesh = new THREE.InstancedMesh(geo, grassMaterial, count);
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

        const RT = THREE.WebGPURenderTarget || THREE.RenderTarget;
        const rt = new RT(W, H, {
            type: THREE.UnsignedByteType,
            format: THREE.RGBAFormat,
            depthBuffer: true,
        });
        rt.texture.wrapS = THREE.RepeatWrapping;
        rt.texture.wrapT = THREE.RepeatWrapping;
        rt.texture.colorSpace = THREE.SRGBColorSpace;

        const oldRT = renderer.getRenderTarget();
        const oldClear = new THREE.Color();
        renderer.getClearColor(oldClear);
        const oldAlpha = renderer.getClearAlpha();
        try {
            // Clear the RT to fully-transparent so non-blade pixels stay
            // alpha=0 (the billboard quad alpha-tests them out and the grass
            // shape underneath shows through).
            renderer.setClearColor(0x000000, 0);
            renderer.setRenderTarget(rt);
            renderer.clear();
            renderer.render(subScene, cam);
        } catch (e) {
            log('[grass] billboard texture render failed: ' + (e && e.message));
        } finally {
            renderer.setRenderTarget(oldRT);
            renderer.setClearColor(oldClear, oldAlpha);
        }

        geo.dispose();
        return rt.texture;
    }

    // Shared billboard mesh: one InstancedMesh whose instance buffer is
    // rewritten each tick from updateGrassBillboards(). The geometry is a
    // GRASS_TILE_SIZE × GRASS_TILE_SIZE quad laid flat on +Y. Per-instance
    // tint multiplies the texture sample so each tile keeps its shape colour.
    function buildGrassBillboardMesh() {
        if (grassBillboardMesh) {
            grassRoot.remove(grassBillboardMesh);
            try { grassBillboardMesh.geometry.dispose(); } catch (e) {}
            try { grassBillboardMesh.material.dispose(); } catch (e) {}
        }
        const geo = new THREE.PlaneGeometry(GRASS_TILE_SIZE, GRASS_TILE_SIZE);
        geo.rotateX(-Math.PI / 2); // flat on XZ
        grassBillboardTintAttr = new THREE.InstancedBufferAttribute(
            new Float32Array(GRASS_BILLBOARD_CAPACITY * 3), 3);
        geo.setAttribute('grassTint', grassBillboardTintAttr);

        const mat = new MeshLambertNodeMaterial({
            side: THREE.DoubleSide,
            transparent: true,
            alphaTest: 0.4,
        });
        mat.colorNode = Fn(() => {
            const tint = attribute('grassTint').toVar();
            const samp = texture(grassBillboardTex, uv()).toVar();
            return vec3(samp.x.mul(tint.x), samp.y.mul(tint.y), samp.z.mul(tint.z));
        })();
        mat.opacityNode = Fn(() => texture(grassBillboardTex, uv()).w)();

        grassBillboardMesh = new THREE.InstancedMesh(geo, mat, GRASS_BILLBOARD_CAPACITY);
        grassBillboardMesh.frustumCulled = false;
        grassBillboardMesh.castShadow = false;
        grassBillboardMesh.receiveShadow = false;
        grassBillboardMesh.matrixAutoUpdate = false;
        grassBillboardMesh.matrixWorldAutoUpdate = false;
        grassBillboardMesh.visible = true;
        grassBillboardMesh.count = 0;
        grassBillboardMesh.updateMatrix();
        grassBillboardMesh.updateMatrixWorld(true);
        grassRoot.add(grassBillboardMesh);
    }

    function buildGrass(root, visIds) {
        // Fresh per scene build — old tiles were disposed via disposeTree.
        grassPolygons = [];
        grassPolyBuckets = new Map();
        grassTileCache = new Map();
        grassMaterial = null;
        grassRoot = root;
        grassPrimed = false;
        grassPool = [];
        grassFreeSlots = [];

        function bboxOf(poly) {
            let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
            for (const p of poly) {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }
            return { minX, maxX, minY, maxY };
        }
        function pushOccluder(poly) {
            if (!poly || poly.length < 3) return;
            grassPolygons.push({
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
                    grassPolygons.push({
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
            grassPolygons = [];
            return;
        }

        // 2) Spline ribbons — rivers/lakes (RO_SPLINE) and road/trail/track
        // ribbons. All splines render ABOVE every terrain shape, so they go
        // after the shape stack as universal occluders. Open rivers use the
        // water ribbon outline; closed water bodies are the sample polygon
        // directly; roads/trails/tracks use the casing extent (w/2 + extra).
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const sp of n.splines || []) {
                    if (!elementShown('spline', sp.id, n.id, visIds)) continue;
                    const pts = sp.points || [];
                    if (pts.length < 2) continue;
                    const prof = splineProfile(sp.kind, sp.preset);
                    const samples = sampleSpline(pts, !!prof.straight, !!sp.closed);
                    if (samples.length < 2) continue;
                    let outline = null;
                    if (sp.kind === 'river') {
                        if (sp.closed && samples.length >= 3) {
                            outline = samples.map(s => ({ x: s.x, y: s.y }));
                        } else {
                            outline = ribbonOutlinePolygon(samples, 0);
                        }
                    } else {
                        const extra = (prof.casing && prof.casing.extra) || 0;
                        outline = ribbonOutlinePolygon(samples, extra);
                    }
                    pushOccluder(outline);
                }
                walk(n.children);
            }
        })(mapLayers());

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

        // 4) Precompute per-tile polygon buckets. The scene is static once
        // built, so the per-tile broad-phase + corner test that sampleGrassTile
        // used to repeat on every build is folded into one pass here.
        for (const p of grassPolygons) {
            const ix0 = Math.floor(p.bb.minX / GRASS_TILE_SIZE);
            const ix1 = Math.floor(p.bb.maxX / GRASS_TILE_SIZE);
            const iz0 = Math.floor(p.bb.minY / GRASS_TILE_SIZE);
            const iz1 = Math.floor(p.bb.maxY / GRASS_TILE_SIZE);
            for (let iz = iz0; iz <= iz1; iz++) {
                for (let ix = ix0; ix <= ix1; ix++) {
                    const k = ix + ',' + iz;
                    let b = grassPolyBuckets.get(k);
                    if (!b) {
                        b = { polys: [], hasGrass: false, hasOccluder: false, fastGrass: null };
                        grassPolyBuckets.set(k, b);
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
        for (const [key, b] of grassPolyBuckets) {
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

        // Clamp the effective density so that a fully-grass tile produces at
        // most MAX_BLADES_PER_TILE candidates. Without this, a high
        // GRASS_DENSITY setting CLAMPS THE FULL TILE but not partially-
        // occluded tiles, making per-m² density INCREASE with occluder
        // coverage (the opposite of intent). Derive the jitter step once.
        const maxFitDensity = GRASS_MAX_BLADES_PER_TILE
            / (GRASS_TILE_SIZE * GRASS_TILE_SIZE);
        grassEffectiveDensity = Math.min(GRASS_DENSITY, maxFitDensity);
        grassStep = 1 / Math.sqrt(grassEffectiveDensity);

        grassMaterial = makeGrassMaterial();
        buildGrassPool();
        // Billboard texture must be generated AFTER grassMaterial exists (it
        // renders blades using that material). Then the billboard mesh wraps
        // it; instance data is written per tick from updateGrassBillboards.
        grassBillboardTex = generateGrassBillboardTexture();
        if (grassBillboardTex) buildGrassBillboardMesh();
        if (GRASS_DEBUG) {
            let nGrassPolys = 0, nOccluders = 0, nGrassBuckets = 0;
            for (const p of grassPolygons) {
                if (p.isGrass) nGrassPolys++; else nOccluders++;
            }
            for (const b of grassPolyBuckets.values()) {
                if (b.hasGrass) nGrassBuckets++;
            }
            log('[grass] build complete:'
                + ' polygons=' + grassPolygons.length
                + ' grass=' + nGrassPolys
                + ' occluders=' + nOccluders
                + ' buckets=' + grassPolyBuckets.size
                + ' grassBuckets=' + nGrassBuckets
                + ' pool=' + grassPool.length
                + ' configDensity=' + GRASS_DENSITY
                + ' effectiveDensity=' + grassEffectiveDensity.toFixed(2));
        }
    }

    // Sample one tile in-place. Uses the precomputed per-tile bucket
    // (polygons that intersect, hasGrass/hasOccluder flags, fastGrass for
    // tiles fully inside one grass polygon). Returns the InstancedMesh, or
    // null if no blades landed.
    function sampleGrassTile(ix, iz) {
        const tSample0 = GRASS_DEBUG ? performance.now() : 0;
        const bucket = grassPolyBuckets.get(ix + ',' + iz);
        if (!bucket || !bucket.hasGrass) return null;
        const x0 = ix * GRASS_TILE_SIZE, x1 = x0 + GRASS_TILE_SIZE;
        const z0 = iz * GRASS_TILE_SIZE, z1 = z0 + GRASS_TILE_SIZE;
        const polysIn = bucket.polys;
        const fastGrass = bucket.fastGrass;
        if (GRASS_DEBUG) {
            grassDebug.sampleCount++;
            if (fastGrass) grassDebug.sampleFast++;
        }

        // Use the SCENE-CLAMPED step so per-m² density stays uniform across
        // tiles regardless of occluder coverage.
        const step = grassStep;
        // Collect blades as objects so we can shuffle them into a random order
        // before flattening to typed arrays. Random order matters because
        // updateGrassVisibility uses inst.count = totalBlades × density(dist)
        // to thin tiles by distance — taking the first N of a SHUFFLED array
        // is an unbiased subsample (taking the first N of row-major-sampled
        // blades would draw only the "bottom-left strip" of the tile).
        const cands = [];
        for (let y = z0; y < z1; y += step) {
            for (let x = x0; x < x1; x += step) {
                const jx = x + (Math.random() - 0.5) * step;
                const jy = y + (Math.random() - 0.5) * step;
                let hit;
                if (fastGrass) {
                    hit = fastGrass;
                } else {
                    hit = null;
                    // Top-down: highest index = topmost render order.
                    for (let i = polysIn.length - 1; i >= 0; i--) {
                        const p = polysIn[i];
                        if (pointInPolygon(jx, jy, p.poly)) { hit = p; break; }
                    }
                    if (!hit || !hit.isGrass) continue;
                }
                cands.push({
                    jx, jy,
                    tr: hit.color.r, tg: hit.color.g, tb: hit.color.b,
                    phase: Math.random() * Math.PI * 2,
                    yaw: Math.random() * Math.PI * 2,
                    scale: 0.7 + Math.random() * 0.6,
                });
            }
        }
        const n = cands.length;
        if (n === 0) {
            if (GRASS_DEBUG) grassDebug.sampleMs += performance.now() - tSample0;
            return null;
        }
        // Fisher–Yates.
        for (let i = n - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            const tmp = cands[i]; cands[i] = cands[j]; cands[j] = tmp;
        }

        // Cap to pool buffer capacity. If a tile would have more blades than
        // we can fit, drop the tail (cands is already shuffled, so dropping
        // the tail is an unbiased subsample).
        const cap = Math.min(cands.length, GRASS_MAX_BLADES_PER_TILE);

        // Acquire a pool slot. Free stack first; if empty, evict the slot
        // bound to the furthest tile from the camera.
        let slotIdx = grassFreeSlots.length > 0 ? grassFreeSlots.pop() : evictFurthestPoolSlot();
        if (slotIdx < 0) {
            if (GRASS_DEBUG) grassDebug.sampleMs += performance.now() - tSample0;
            return null;
        }
        const entry = grassPool[slotIdx];
        const arrRoot = entry.rootsAttr.array;
        const arrTint = entry.tintsAttr.array;
        const arrPhase = entry.phasesAttr.array;
        for (let i = 0; i < cap; i++) {
            const c = cands[i];
            arrRoot[i * 2] = c.jx; arrRoot[i * 2 + 1] = c.jy;
            arrTint[i * 3] = c.tr; arrTint[i * 3 + 1] = c.tg; arrTint[i * 3 + 2] = c.tb;
            arrPhase[i] = c.phase;
        }
        // Mark sub-ranges dirty — three.js webgpu writes only the used region
        // via queue.writeBuffer, which is much cheaper than buffer creation.
        entry.rootsAttr.needsUpdate = true;
        entry.tintsAttr.needsUpdate = true;
        entry.phasesAttr.needsUpdate = true;

        for (let i = 0; i < cap; i++) {
            const c = cands[i];
            grassPoolDummy.position.set(c.jx, 0.02, c.jy);
            grassPoolDummy.rotation.set(0, c.yaw, 0);
            grassPoolDummy.scale.setScalar(c.scale);
            grassPoolDummy.updateMatrix();
            entry.mesh.setMatrixAt(i, grassPoolDummy.matrix);
        }
        entry.mesh.instanceMatrix.needsUpdate = true;

        // Update bbox / sphere so frustum culling uses the actual tile extent.
        const tileCx = (ix + 0.5) * GRASS_TILE_SIZE;
        const tileCz = (iz + 0.5) * GRASS_TILE_SIZE;
        const half = GRASS_TILE_SIZE * 0.5;
        entry.geo.boundingBox.min.set(tileCx - half, 0, tileCz - half);
        entry.geo.boundingBox.max.set(tileCx + half, GRASS_BLADE_HEIGHT, tileCz + half);
        entry.geo.boundingSphere.center.set(tileCx, GRASS_BLADE_HEIGHT * 0.5, tileCz);
        entry.geo.boundingSphere.radius = Math.hypot(half, half, GRASS_BLADE_HEIGHT * 0.5);

        entry.mesh.count = cap;
        entry.mesh.userData.totalBlades = cap;
        entry.mesh.userData.grassCenter = { x: tileCx, z: tileCz };
        // Representative grass colour for the billboard LOD — first grass
        // polygon covering the tile wins (matches what most blades would tint
        // to anyway).
        let tileColor = null;
        for (const p of polysIn) {
            if (p.isGrass) { tileColor = p.color; break; }
        }
        entry.mesh.userData.tileColor = tileColor;
        entry.mesh.visible = true;
        entry.tileKey = ix + ',' + iz;
        if (GRASS_DEBUG) grassDebug.sampleMs += performance.now() - tSample0;
        return entry.mesh;
    }

    // Distance-based density LOD. Returns the fraction of a tile's blades to
    // actually draw given the distance from camera to tile center.
    // - dist ≤ NEAR_RADIUS → 1 (full density)
    // - dist ≥ RENDER_RADIUS → 0 (cut)
    // - in between → quadratic falloff (steeper than linear so far tiles
    //   shed blades quickly while the near zone keeps its visual mass)
    function grassDensityFraction(dist) {
        if (dist <= GRASS_NEAR_RADIUS) return 1;
        if (dist >= GRASS_RENDER_RADIUS) return 0;
        const t = (GRASS_RENDER_RADIUS - dist)
            / (GRASS_RENDER_RADIUS - GRASS_NEAR_RADIUS);
        return Math.pow(t, GRASS_FALLOFF_POWER);
    }

    // Each tick: determine the set of tiles within the render radius around
    // the camera. Sample any missing ones (with a per-frame cap after the
    // first call, which primes everything synchronously to avoid an empty
    // first frame), and for each visible tile set inst.count from its
    // distance-derived density fraction so distant tiles draw fewer blades.
    function updateGrassVisibility() {
        if (grassPolygons.length === 0) return;
        const tUpdate0 = GRASS_DEBUG ? performance.now() : 0;
        const cx = camera.position.x, cz = camera.position.z;
        const ctx = Math.floor(cx / GRASS_TILE_SIZE);
        const ctz = Math.floor(cz / GRASS_TILE_SIZE);
        const radTiles = Math.ceil(GRASS_RENDER_RADIUS / GRASS_TILE_SIZE);
        const half = GRASS_TILE_SIZE * 0.5 * Math.SQRT2;
        const cullR = GRASS_RENDER_RADIUS + half;
        const cullR2 = cullR * cullR;

        const needed = new Map(); // key → squared distance
        for (let dz = -radTiles; dz <= radTiles; dz++) {
            for (let dx = -radTiles; dx <= radTiles; dx++) {
                const ix = ctx + dx, iz = ctz + dz;
                const tcx = (ix + 0.5) * GRASS_TILE_SIZE;
                const tcz = (iz + 0.5) * GRASS_TILE_SIZE;
                const ddx = tcx - cx, ddz = tcz - cz;
                const d2 = ddx * ddx + ddz * ddz;
                if (d2 > cullR2) continue;
                needed.set(ix + ',' + iz, d2);
            }
        }

        // Prime tick: build everything synchronously (one entry hitch). Later
        // ticks: stop once the per-frame time budget is exhausted so movement
        // can never trigger a multi-tile sampling spike. Order tiles by
        // distance ascending so the closest missing tiles get built first —
        // matters when moving fast: you see the in-front tiles fill before the
        // ones you're leaving behind.
        const t0 = performance.now();
        const budget = grassPrimed ? GRASS_TICK_BUDGET_MS : Infinity;
        const sorted = Array.from(needed.entries())
            .filter(([k]) => !grassTileCache.has(k))
            .sort((a, b) => a[1] - b[1]);
        for (const [key] of sorted) {
            if (performance.now() - t0 >= budget) break;
            const [ixStr, izStr] = key.split(',');
            const tile = sampleGrassTile(parseInt(ixStr, 10), parseInt(izStr, 10));
            grassTileCache.set(key, tile); // null marks "sampled, no blades here"
            // Pool meshes are already parented at buildGrass time. sampleGrassTile
            // either bound one to this tile or returned null (no blades).
            if (tile && GRASS_DEBUG) grassDebug.newTiles++;
        }
        grassPrimed = true;

        // Build frustum once per tick from the camera's current view-proj.
        grassProjMatrix.multiplyMatrices(
            camera.projectionMatrix, camera.matrixWorldInverse);
        grassFrustum.setFromProjectionMatrix(grassProjMatrix);
        const tileHalfDiag = Math.hypot(
            GRASS_TILE_SIZE * 0.5, GRASS_TILE_SIZE * 0.5,
            GRASS_BLADE_HEIGHT * 0.5);

        const tVis0 = GRASS_DEBUG ? performance.now() : 0;
        const billboardTints = grassBillboardTintAttr
            ? grassBillboardTintAttr.array : null;
        let billboardCount = 0;
        for (const [key, tile] of grassTileCache) {
            if (!tile) continue;
            const d2 = needed.get(key);
            if (d2 === undefined) {
                tile.visible = false;
                continue;
            }
            const dist = Math.sqrt(d2);
            const cc = tile.userData.grassCenter;
            grassSphere.center.set(cc.x, GRASS_BLADE_HEIGHT * 0.5, cc.z);
            grassSphere.radius = tileHalfDiag;
            const inFrustum = grassFrustum.intersectsSphere(grassSphere);

            if (dist >= GRASS_BILLBOARD_DISTANCE) {
                // Billboard LOD — flat textured quad replaces the blade mesh.
                tile.visible = false;
                if (!inFrustum) continue;
                if (!grassBillboardMesh || billboardCount >= GRASS_BILLBOARD_CAPACITY) continue;
                grassBillboardDummy.position.set(cc.x, 0.05, cc.z);
                grassBillboardDummy.rotation.set(0, 0, 0);
                grassBillboardDummy.scale.set(1, 1, 1);
                grassBillboardDummy.updateMatrix();
                grassBillboardMesh.setMatrixAt(billboardCount, grassBillboardDummy.matrix);
                const tc = tile.userData.tileColor;
                if (tc && billboardTints) {
                    billboardTints[billboardCount * 3] = tc.r;
                    billboardTints[billboardCount * 3 + 1] = tc.g;
                    billboardTints[billboardCount * 3 + 2] = tc.b;
                } else if (billboardTints) {
                    billboardTints[billboardCount * 3] = 1;
                    billboardTints[billboardCount * 3 + 1] = 1;
                    billboardTints[billboardCount * 3 + 2] = 1;
                }
                billboardCount++;
            } else {
                // Blade LOD — instanced blade mesh with density falloff.
                const f = grassDensityFraction(dist);
                const drawCount = Math.ceil(tile.userData.totalBlades * f);
                if (drawCount === 0 || !inFrustum) {
                    tile.visible = false;
                    continue;
                }
                tile.count = drawCount;
                tile.visible = true;
            }
        }
        if (grassBillboardMesh) {
            grassBillboardMesh.count = billboardCount;
            grassBillboardMesh.instanceMatrix.needsUpdate = true;
            if (grassBillboardTintAttr) grassBillboardTintAttr.needsUpdate = true;
        }
        if (GRASS_DEBUG) {
            const now = performance.now();
            grassDebug.visibilityIterMs += now - tVis0;
            const updateDt = now - tUpdate0;
            grassDebug.updateMs += updateDt;
            grassDebug.updateCalls++;
            if (updateDt > grassDebug.updateMaxMs) grassDebug.updateMaxMs = updateDt;
            grassDebugReport(now);
        }
    }

    // =========================================================================
    // Procedural trees (forest shapes). Mirrors the grass pool / bucket / LOD
    // architecture: a tile-grid InstancedMesh pool for close trees, a shared
    // camera-facing billboard mesh for the far ring.
    // =========================================================================

    // Build the merged trunk + foliage geometry as a single BufferGeometry
    // with a per-vertex `color` attribute. One geometry shared by every pool
    // tile mesh (instance buffers carry tile placement).
    // Layered conifer: trunk cylinder + N stacked, overlapping cones that
    // narrow as they go up — gives a pine silhouette instead of one peak.
    // The cones overlap so the layers read as drooping branches rather than a
    // smooth gradient. Top cone is slightly darker for shading depth.
    function makeTreeGeometry() {
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
    function makeTreeMaterial() {
        const mat = new MeshLambertNodeMaterial({
            side: THREE.DoubleSide,
            vertexColors: true,
        });
        return mat;
    }

    // Pool of tile-meshes — same shape as grass pool. Each entry holds up to
    // TREE_MAX_PER_TILE tree instances; we overwrite the instance matrices
    // when a tile binds.
    // Async-load the GLB + canopy texture once per session. Resolves with
    // `true` on success, `false` if anything's missing — caller falls back to
    // the procedural tree mesh.
    function loadTreeAssets() {
        if (treeAssetsLoaded) return Promise.resolve(true);
        if (treeAssetsPromise) return treeAssetsPromise;
        const loader = new GLTFLoader();
        // revo-realms's realm.glb uses Draco compression on its mesh data.
        // Attach a DRACOLoader pointed at the bundled wasm decoder.
        const draco = new DRACOLoader();
        draco.setDecoderPath('./draco/');
        draco.setDecoderConfig({ type: 'wasm' });
        loader.setDRACOLoader(draco);
        const texLoader = new THREE.TextureLoader();
        treeAssetsPromise = (async () => {
            try {
                log('[tree] loading sekai.glb (Draco)...');
                const gltf = await loader.loadAsync('./vegetation/sekai.glb');
                log('[tree] sekai.glb parsed');
                const barkMesh = gltf.scene.getObjectByName('pine_tree_bark');
                const canopyMesh = gltf.scene.getObjectByName('pine_tree_canopy');
                if (!barkMesh || !canopyMesh) {
                    log('[tree] GLB missing pine_tree_bark / pine_tree_canopy');
                    return false;
                }
                treeBarkGeo = barkMesh.geometry;
                treeCanopyGeo = canopyMesh.geometry;
                log('[tree] geometries extracted'
                    + ' (bark verts=' + treeBarkGeo.attributes.position.count
                    + ', canopy verts=' + treeCanopyGeo.attributes.position.count + ')');
                treeBarkGeo.computeBoundingBox();
                const bb = treeBarkGeo.boundingBox;
                const glbHeight = Math.max(0.1, bb.max.y - bb.min.y);
                const targetHeight = TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT;
                treeRefScale = targetHeight / glbHeight;
                log('[tree] refScale=' + treeRefScale.toFixed(3)
                    + ' (glbHeight=' + glbHeight.toFixed(2)
                    + ', target=' + targetHeight + ')');
                log('[tree] loading KTX2 canopy texture...');
                const ktx2 = new KTX2Loader();
                ktx2.setTranscoderPath('./basis/');
                try {
                    ktx2.detectSupport(renderer);
                    log('[tree] KTX2 detectSupport ok');
                } catch (e) {
                    log('[tree] KTX2 detectSupport failed: ' + (e && e.message));
                }
                try {
                    treeCanopyTex = await ktx2.loadAsync(
                        './vegetation/pine-canopy-diffuse.ktx2');
                    log('[tree] KTX2 canopy loaded');
                } catch (e) {
                    log('[tree] KTX2 load failed, falling back to PNG: '
                        + (e && e.stack ? e.stack : e && e.message ? e.message : e));
                    treeCanopyTex = await texLoader.loadAsync(
                        './vegetation/pine-canopy-diffuse.png');
                    log('[tree] PNG canopy loaded as fallback');
                }
                treeCanopyTex.colorSpace = THREE.SRGBColorSpace;
                treeCanopyTex.wrapS = THREE.ClampToEdgeWrapping;
                treeCanopyTex.wrapT = THREE.ClampToEdgeWrapping;

                // Bark — diffuse + normal, both KTX2 (UASTC). The bark tiles
                // around the trunk, so wrap repeats and we don't need to
                // pre-set UV scale on the texture itself; the material picks
                // a UV multiplier per-fragment.
                log('[tree] loading bark KTX2 textures...');
                try {
                    treeBarkDiffuseTex = await ktx2.loadAsync(
                        './vegetation/tree-bark-diffuse.ktx2');
                    treeBarkDiffuseTex.colorSpace = THREE.SRGBColorSpace;
                    treeBarkDiffuseTex.wrapS = THREE.RepeatWrapping;
                    treeBarkDiffuseTex.wrapT = THREE.RepeatWrapping;
                    log('[tree] bark diffuse loaded');
                } catch (e) {
                    log('[tree] bark diffuse load failed: ' + (e && e.message));
                    treeBarkDiffuseTex = null;
                }
                try {
                    treeBarkNormalTex = await ktx2.loadAsync(
                        './vegetation/tree-bark-normal.ktx2');
                    treeBarkNormalTex.wrapS = THREE.RepeatWrapping;
                    treeBarkNormalTex.wrapT = THREE.RepeatWrapping;
                    log('[tree] bark normal loaded');
                } catch (e) {
                    log('[tree] bark normal load failed: ' + (e && e.message));
                    treeBarkNormalTex = null;
                }

                treeAssetsLoaded = true;
                return true;
            } catch (e) {
                log('[tree] asset load failed: '
                    + (e && e.stack ? e.stack : e && e.message ? e.message : e));
                return false;
            }
        })();
        return treeAssetsPromise;
    }

    function makeTreeMaterialsFromGlb() {
        // Bark — solid brown Lambert. (We don't ship the bark KTX2 texture.)
        treeBarkMaterial = new MeshLambertNodeMaterial({
            side: THREE.DoubleSide,
            vertexColors: false,
        });
        // Bark — match revo-realms PineTreeBarkMaterial: diffuse × ~3.5,
        // UV scale 3 (tiles around trunk), optional normal map with scale 3.
        const BARK_UV_SCALE = 3;
        const BARK_DIFFUSE_SCALE = 3.5;
        const BARK_NORMAL_SCALE = 3;
        if (treeBarkDiffuseTex) {
            treeBarkMaterial.colorNode = Fn(() => {
                const tUv = uv().mul(BARK_UV_SCALE);
                const samp = texture(treeBarkDiffuseTex, tUv).toVar();
                return vec3(samp.x, samp.y, samp.z).mul(BARK_DIFFUSE_SCALE);
            })();
        } else {
            treeBarkMaterial.colorNode = Fn(() => vec3(0.42, 0.26, 0.13))();
        }
        if (treeBarkNormalTex) {
            treeBarkMaterial.normalNode = Fn(() => {
                const tUv = uv().mul(BARK_UV_SCALE);
                return normalMap(
                    texture(treeBarkNormalTex, tUv), float(BARK_NORMAL_SCALE));
            })();
        }

        // Canopy — alpha-tested needles. Matches revo-realms
        // PineTreeCanopyMaterial: opaque, alphaTest, no blending. Setting
        // `transparent: true` here would re-enable depth sorting (and the
        // ordered alpha blend) which breaks the cross-quad foliage — the
        // mesh has dozens of overlapping quads that depend on z-test + a
        // hard discard, not blending.
        treeCanopyMaterial = new MeshLambertNodeMaterial({
            side: THREE.DoubleSide,
            transparent: false,
            // Higher alphaTest = crisper silhouette for both main render AND
            // the auto-derived shadow depth pass. Too high eats needle tips;
            // too low merges card edges into solid shadow blobs.
            alphaTest: 0.5,
            forceSinglePass: true,
        });
        treeCanopyMaterial.colorNode = Fn(() => {
            const samp = texture(treeCanopyTex, uv()).toVar();
            return vec3(samp.x, samp.y, samp.z).mul(0.6);
        })();
        treeCanopyMaterial.opacityNode = Fn(() => texture(treeCanopyTex, uv()).w)();
    }

    // Two InstancedMesh per pool slot: one for bark, one for canopy. Both
    // share the same instance matrices (one tree = one matrix applied to both
    // meshes). updateTreeVisibility writes the same count / visible to both.
    function buildTreePool() {
        treePool = [];
        treeFreeSlots = [];
        // If GLB load failed for any reason, fall back to the procedural
        // layered-cone mesh so trees still render.
        const useGlb = treeAssetsLoaded && treeBarkGeo && treeCanopyGeo;
        if (!useGlb && !treeTileGeometry) treeTileGeometry = makeTreeGeometry();

        const bbMin = new THREE.Vector3(-3, 0, -3);
        const bbMax = new THREE.Vector3(3, 12, 3);

        for (let i = 0; i < TREE_POOL_SIZE; i++) {
            const meshes = [];
            if (useGlb) {
                const barkMesh = new THREE.InstancedMesh(
                    treeBarkGeo, treeBarkMaterial, TREE_MAX_PER_TILE);
                const canopyMesh = new THREE.InstancedMesh(
                    treeCanopyGeo, treeCanopyMaterial, TREE_MAX_PER_TILE);
                meshes.push(barkMesh, canopyMesh);
            } else {
                const geo = treeTileGeometry.clone();
                geo.boundingBox = new THREE.Box3(bbMin.clone(), bbMax.clone());
                geo.boundingSphere = new THREE.Sphere(
                    new THREE.Vector3(0, 6, 0), 8);
                meshes.push(new THREE.InstancedMesh(
                    geo, treeMaterial, TREE_MAX_PER_TILE));
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
                grassRoot.add(m);
            }
            // First mesh acts as the "primary" — userData lives on it so the
            // visibility loop has a single source for totalTrees / center /
            // treePositions.
            const primary = meshes[0];
            primary.userData.totalTrees = 0;
            primary.userData.treeCenter = { x: 0, z: 0 };
            primary.userData.treePositions = [];
            treePool.push({ meshes, primary, tileKey: null });
            treeFreeSlots.push(i);
        }
    }

    function evictFurthestTreeSlot() {
        const cx = camera.position.x, cz = camera.position.z;
        let furthestIdx = -1, furthestD2 = -1;
        for (let i = 0; i < treePool.length; i++) {
            const e = treePool[i];
            if (e.tileKey === null) return i;
            const c = e.primary.userData.treeCenter;
            const dx = c.x - cx, dz = c.z - cz;
            const d2 = dx * dx + dz * dz;
            if (d2 > furthestD2) { furthestD2 = d2; furthestIdx = i; }
        }
        if (furthestIdx < 0) return -1;
        const ev = treePool[furthestIdx];
        if (ev.tileKey !== null) {
            treeTileCache.delete(ev.tileKey);
            ev.tileKey = null;
        }
        for (const m of ev.meshes) { m.visible = false; m.count = 0; }
        return furthestIdx;
    }

    // Pre-render the standing-tree billboard texture by drawing a single tree
    // mesh from a side-on ortho camera. Result: a tree silhouette w/ alpha=0
    // background. Used by the camera-facing far-LOD quads.
    function generateTreeBillboardTexture() {
        if (!renderer || !treeMaterial || !treeTileGeometry) return null;
        const W = TREE_BILLBOARD_TEX_RES, H = TREE_BILLBOARD_TEX_RES;

        const subScene = new THREE.Scene();
        subScene.add(new THREE.AmbientLight(0xffffff, 0.7));
        const sun = new THREE.DirectionalLight(0xffffcc, 0.85);
        sun.position.set(0.4, 1, 0.3);
        subScene.add(sun);

        const mesh = new THREE.Mesh(treeTileGeometry, treeMaterial);
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

        const RT = THREE.WebGPURenderTarget || THREE.RenderTarget;
        const rt = new RT(W, H, {
            type: THREE.UnsignedByteType,
            format: THREE.RGBAFormat,
            depthBuffer: true,
        });
        rt.texture.colorSpace = THREE.SRGBColorSpace;
        rt.texture.wrapS = THREE.ClampToEdgeWrapping;
        rt.texture.wrapT = THREE.ClampToEdgeWrapping;

        const oldRT = renderer.getRenderTarget();
        const oldClear = new THREE.Color();
        renderer.getClearColor(oldClear);
        const oldAlpha = renderer.getClearAlpha();
        try {
            renderer.setClearColor(0x000000, 0);
            renderer.setRenderTarget(rt);
            renderer.clear();
            renderer.render(subScene, cam);
        } catch (e) {
            log('[tree] billboard texture render failed: ' + (e && e.message));
        } finally {
            renderer.setRenderTarget(oldRT);
            renderer.setClearColor(oldClear, oldAlpha);
        }
        return rt.texture;
    }

    // Far-LOD billboard mesh. Each tree becomes a STANDING camera-facing
    // quad: in the vertex shader we build a right-vector perpendicular to
    // (anchor → camera) on the XZ plane and emit the quad's local x along it,
    // keeping the trunk straight up. Per-instance anchor + size carried as
    // instance attributes; instanceMatrix is identity.
    function buildTreeBillboardMesh() {
        if (treeBillboardMesh) {
            grassRoot.remove(treeBillboardMesh);
            try { treeBillboardMesh.geometry.dispose(); } catch (e) {}
            try { treeBillboardMesh.material.dispose(); } catch (e) {}
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

        treeBillboardAnchorAttr = new THREE.InstancedBufferAttribute(
            new Float32Array(TREE_BILLBOARD_CAPACITY * 3), 3);
        treeBillboardTintAttr = new THREE.InstancedBufferAttribute(
            new Float32Array(TREE_BILLBOARD_CAPACITY * 3), 3);
        const sizeAttr = new THREE.InstancedBufferAttribute(
            new Float32Array(TREE_BILLBOARD_CAPACITY * 2), 2);
        geo.setAttribute('treeAnchor', treeBillboardAnchorAttr);
        geo.setAttribute('treeTint', treeBillboardTintAttr);
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
            const samp = texture(treeBillboardTex, uv()).toVar();
            return vec3(samp.x.mul(tint.x), samp.y.mul(tint.y), samp.z.mul(tint.z));
        })();
        mat.opacityNode = Fn(() => texture(treeBillboardTex, uv()).w)();

        treeBillboardMesh = new THREE.InstancedMesh(geo, mat, TREE_BILLBOARD_CAPACITY);
        treeBillboardMesh.frustumCulled = false;
        treeBillboardMesh.castShadow = false;
        treeBillboardMesh.receiveShadow = false;
        treeBillboardMesh.matrixAutoUpdate = false;
        treeBillboardMesh.matrixWorldAutoUpdate = false;
        treeBillboardMesh.visible = true;
        treeBillboardMesh.count = 0;
        treeBillboardMesh.updateMatrix();
        treeBillboardMesh.updateMatrixWorld(true);
        grassRoot.add(treeBillboardMesh);
    }

    function buildTrees(root, visIds) {
        treePolygons = [];
        treePolyBuckets = new Map();
        treeTileCache = new Map();
        treeMaterial = null;
        treePrimed = false;
        treePool = [];
        treeFreeSlots = [];

        function bboxOf(poly) {
            let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
            for (const p of poly) {
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }
            return { minX, maxX, minY, maxY };
        }
        function pushOccluder(poly) {
            if (!poly || poly.length < 3) return;
            treePolygons.push({
                poly, bb: bboxOf(poly),
                color: null, isForest: false,
            });
        }

        // 1) Terrain shapes — collect forest shapes + every shape as occluder
        // (a sand or road shape painted on a forest carves out the trees).
        let sawForest = false;
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const sh of n.shapes || []) {
                    if (!elementShown('shape', sh.id, n.id, visIds)) continue;
                    const poly = sh.points || [];
                    if (poly.length < 3) continue;
                    const isForest = sh.type === 'forest';
                    if (isForest) sawForest = true;
                    treePolygons.push({
                        poly, bb: bboxOf(poly),
                        color: new THREE.Color(0x4a6b30),
                        isForest,
                    });
                }
                walk(n.children);
            }
        })(mapLayers());

        if (!sawForest) {
            treePolygons = [];
            return;
        }

        // 2) Spline ribbons (roads + water) — sit on top of every shape, mask
        // trees the same way they mask grass.
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const sp of n.splines || []) {
                    if (!elementShown('spline', sp.id, n.id, visIds)) continue;
                    const pts = sp.points || [];
                    if (pts.length < 2) continue;
                    const prof = splineProfile(sp.kind, sp.preset);
                    const samples = sampleSpline(pts, !!prof.straight, !!sp.closed);
                    if (samples.length < 2) continue;
                    let outline = null;
                    if (sp.kind === 'river') {
                        if (sp.closed && samples.length >= 3) {
                            outline = samples.map(s => ({ x: s.x, y: s.y }));
                        } else {
                            outline = ribbonOutlinePolygon(samples, 0);
                        }
                    } else {
                        const extra = (prof.casing && prof.casing.extra) || 0;
                        outline = ribbonOutlinePolygon(samples, extra);
                    }
                    pushOccluder(outline);
                }
                walk(n.children);
            }
        })(mapLayers());

        // 3) Building footprints.
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const b of n.buildings || []) {
                    if (!elementShown('building', b.id, n.id, visIds)) continue;
                    pushOccluder(b.footprint || []);
                }
                walk(n.children);
            }
        })(mapLayers());

        // Bucket per tile.
        for (const p of treePolygons) {
            const ix0 = Math.floor(p.bb.minX / TREE_TILE_SIZE);
            const ix1 = Math.floor(p.bb.maxX / TREE_TILE_SIZE);
            const iz0 = Math.floor(p.bb.minY / TREE_TILE_SIZE);
            const iz1 = Math.floor(p.bb.maxY / TREE_TILE_SIZE);
            for (let iz = iz0; iz <= iz1; iz++) {
                for (let ix = ix0; ix <= ix1; ix++) {
                    const k = ix + ',' + iz;
                    let b = treePolyBuckets.get(k);
                    if (!b) {
                        b = { polys: [], hasForest: false, hasOccluder: false };
                        treePolyBuckets.set(k, b);
                    }
                    b.polys.push(p);
                    if (p.isForest) b.hasForest = true; else b.hasOccluder = true;
                }
            }
        }

        treeStep = 1 / Math.sqrt(TREE_DENSITY);
        treeMaterial = makeTreeMaterial(); // legacy / fallback
        if (treeAssetsLoaded) makeTreeMaterialsFromGlb();

        if (TREE_STATIC_MODE) {
            buildStaticTreeMeshes();
            return;
        }

        buildTreePool();
        treeBillboardTex = generateTreeBillboardTexture();
        if (treeBillboardTex) buildTreeBillboardMesh();
    }

    // Sample every forest tile in the map once, write to two InstancedMesh
    // (bark + canopy) sized exactly for the result, add to scene. No tile
    // cache, no pool, no billboard — three.js draws them all every frame.
    // Works well because trees are sparse (TREE_DENSITY = 0.05/m²) so even a
    // huge map produces a few hundred thousand instances at worst.
    function buildStaticTreeMeshes() {
        if (!treeAssetsLoaded || !treeBarkGeo || !treeCanopyGeo
            || !treeBarkMaterial || !treeCanopyMaterial) {
            log('[tree] static mode: tree assets missing, skipping');
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
        for (const [key, bucket] of treePolyBuckets) {
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
                        * treeRefScale * TREE_SCALE,
                });
            }
        }
        const sampleMs = performance.now() - t0;
        log('[tree] static-mode sampled ' + trees.length
            + ' trees in ' + sampleMs.toFixed(0) + 'ms');
        if (trees.length === 0) return;

        treeBarkStaticMesh = new THREE.InstancedMesh(
            treeBarkGeo, treeBarkMaterial, trees.length);
        treeCanopyStaticMesh = new THREE.InstancedMesh(
            treeCanopyGeo, treeCanopyMaterial, trees.length);
        for (const m of [treeBarkStaticMesh, treeCanopyStaticMesh]) {
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
        treeBarkStaticMesh.castShadow = true;
        treeBarkStaticMesh.receiveShadow = true;
        // Canopy — cross-quad cards. three.js webgpu auto-derives the shadow
        // depth shader from the main material (alphaTest + opacityNode),
        // so no customDepthMaterial is needed.
        treeCanopyStaticMesh.castShadow = true;
        treeCanopyStaticMesh.receiveShadow = true;
        const dummy = new THREE.Object3D();
        for (let i = 0; i < trees.length; i++) {
            const t = trees[i];
            dummy.position.set(t.x, 0, t.z);
            dummy.rotation.set(0, t.yaw, 0);
            dummy.scale.setScalar(t.scale);
            dummy.updateMatrix();
            treeBarkStaticMesh.setMatrixAt(i, dummy.matrix);
            treeCanopyStaticMesh.setMatrixAt(i, dummy.matrix);
        }
        treeBarkStaticMesh.instanceMatrix.needsUpdate = true;
        treeCanopyStaticMesh.instanceMatrix.needsUpdate = true;
        grassRoot.add(treeBarkStaticMesh);
        grassRoot.add(treeCanopyStaticMesh);
    }

    function sampleTreeTile(ix, iz) {
        const bucket = treePolyBuckets.get(ix + ',' + iz);
        if (!bucket || !bucket.hasForest) return null;
        const x0 = ix * TREE_TILE_SIZE, x1 = x0 + TREE_TILE_SIZE;
        const z0 = iz * TREE_TILE_SIZE, z1 = z0 + TREE_TILE_SIZE;
        const polysIn = bucket.polys;
        const cands = [];
        for (let y = z0; y < z1; y += treeStep) {
            for (let x = x0; x < x1; x += treeStep) {
                const jx = x + (Math.random() - 0.5) * treeStep;
                const jy = y + (Math.random() - 0.5) * treeStep;
                let hit = null;
                for (let i = polysIn.length - 1; i >= 0; i--) {
                    const p = polysIn[i];
                    if (pointInPolygon(jx, jy, p.poly)) { hit = p; break; }
                }
                if (!hit || !hit.isForest) continue;
                cands.push({
                    jx, jy,
                    yaw: Math.random() * Math.PI * 2,
                    scale: 0.85 + Math.random() * 0.45,
                });
            }
        }
        if (cands.length === 0) return null;
        // Shuffle for unbiased subsample.
        for (let i = cands.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            const tmp = cands[i]; cands[i] = cands[j]; cands[j] = tmp;
        }
        const n = Math.min(cands.length, TREE_MAX_PER_TILE);

        let slotIdx = treeFreeSlots.length > 0
            ? treeFreeSlots.pop()
            : evictFurthestTreeSlot();
        if (slotIdx < 0) return null;
        const entry = treePool[slotIdx];
        // Same instance matrices written to every mesh in the slot (bark +
        // canopy share placement). treeRefScale normalises the GLB's
        // authored size to roughly the same height as our procedural fallback.
        for (let i = 0; i < n; i++) {
            const c = cands[i];
            treePoolDummy.position.set(c.jx, 0, c.jy);
            treePoolDummy.rotation.set(0, c.yaw, 0);
            treePoolDummy.scale.setScalar(c.scale * treeRefScale);
            treePoolDummy.updateMatrix();
            for (const m of entry.meshes) m.setMatrixAt(i, treePoolDummy.matrix);
        }
        for (const m of entry.meshes) {
            m.instanceMatrix.needsUpdate = true;
            m.count = n;
        }
        entry.primary.userData.totalTrees = n;
        entry.primary.userData.treeCenter = {
            x: (ix + 0.5) * TREE_TILE_SIZE,
            z: (iz + 0.5) * TREE_TILE_SIZE,
        };
        entry.primary.userData.treePositions = cands.slice(0, n)
            .map(c => ({ x: c.jx, z: c.jy, scale: c.scale }));
        entry.primary.userData.poolEntry = entry;
        for (const m of entry.meshes) m.visible = true;
        entry.tileKey = ix + ',' + iz;
        // Return primary so the cache + visibility loop have a single handle.
        return entry.primary;
    }

    function treeDensityFraction(dist) {
        if (dist <= TREE_NEAR_RADIUS) return 1;
        if (dist >= TREE_RENDER_RADIUS) return 0;
        const t = (TREE_RENDER_RADIUS - dist)
            / (TREE_RENDER_RADIUS - TREE_NEAR_RADIUS);
        return Math.pow(t, TREE_FALLOFF_POWER);
    }

    function updateTreeVisibility() {
        if (treePolygons.length === 0) return;
        // Static mode: trees are baked into mega-meshes at buildTrees, no
        // per-frame visibility / pool work needed.
        if (TREE_STATIC_MODE) return;
        const cx = camera.position.x, cz = camera.position.z;
        const ctx = Math.floor(cx / TREE_TILE_SIZE);
        const ctz = Math.floor(cz / TREE_TILE_SIZE);
        const radTiles = Math.ceil(TREE_RENDER_RADIUS / TREE_TILE_SIZE);
        const half = TREE_TILE_SIZE * 0.5 * Math.SQRT2;
        const cullR = TREE_RENDER_RADIUS + half;
        const cullR2 = cullR * cullR;

        const needed = new Map();
        for (let dz = -radTiles; dz <= radTiles; dz++) {
            for (let dx = -radTiles; dx <= radTiles; dx++) {
                const ix = ctx + dx, iz = ctz + dz;
                const tcx = (ix + 0.5) * TREE_TILE_SIZE;
                const tcz = (iz + 0.5) * TREE_TILE_SIZE;
                const ddx = tcx - cx, ddz = tcz - cz;
                const d2 = ddx * ddx + ddz * ddz;
                if (d2 > cullR2) continue;
                needed.set(ix + ',' + iz, d2);
            }
        }

        const t0 = performance.now();
        const budget = treePrimed ? TREE_TICK_BUDGET_MS : Infinity;
        const sorted = Array.from(needed.entries())
            .filter(([k]) => !treeTileCache.has(k))
            .sort((a, b) => a[1] - b[1]);
        for (const [key] of sorted) {
            if (performance.now() - t0 >= budget) break;
            const [ixStr, izStr] = key.split(',');
            const tile = sampleTreeTile(parseInt(ixStr, 10), parseInt(izStr, 10));
            treeTileCache.set(key, tile);
        }
        treePrimed = true;

        // Per-cached-tile: mesh visibility + billboard population.
        const tileHalfDiag = Math.hypot(
            TREE_TILE_SIZE * 0.5, TREE_TILE_SIZE * 0.5,
            (TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT) * 0.5);
        const billboardAnchors = treeBillboardAnchorAttr
            ? treeBillboardAnchorAttr.array : null;
        const billboardTints = treeBillboardTintAttr
            ? treeBillboardTintAttr.array : null;
        let billboardCount = 0;
        for (const [key, tile] of treeTileCache) {
            if (!tile) continue;
            const entry = tile.userData.poolEntry;
            const setMeshes = (visible, count) => {
                if (!entry) { tile.visible = visible; tile.count = count; return; }
                for (const m of entry.meshes) {
                    m.visible = visible;
                    if (count !== undefined) m.count = count;
                }
            };
            const d2 = needed.get(key);
            if (d2 === undefined) {
                setMeshes(false);
                continue;
            }
            const dist = Math.sqrt(d2);
            const cc = tile.userData.treeCenter;
            grassSphere.center.set(cc.x,
                (TREE_TRUNK_HEIGHT + TREE_FOLIAGE_HEIGHT) * 0.5, cc.z);
            grassSphere.radius = tileHalfDiag;
            const inFrustum = grassFrustum.intersectsSphere(grassSphere);

            if (dist >= TREE_BILLBOARD_DISTANCE) {
                setMeshes(false);
                if (!inFrustum) continue;
                if (!treeBillboardMesh) continue;
                const positions = tile.userData.treePositions || [];
                for (const pos of positions) {
                    if (billboardCount >= TREE_BILLBOARD_CAPACITY) break;
                    if (billboardAnchors) {
                        billboardAnchors[billboardCount * 3] = pos.x;
                        billboardAnchors[billboardCount * 3 + 1] = 0;
                        billboardAnchors[billboardCount * 3 + 2] = pos.z;
                    }
                    if (billboardTints) {
                        billboardTints[billboardCount * 3] = 1;
                        billboardTints[billboardCount * 3 + 1] = 1;
                        billboardTints[billboardCount * 3 + 2] = 1;
                    }
                    billboardCount++;
                }
            } else {
                const f = treeDensityFraction(dist);
                const drawCount = Math.ceil(tile.userData.totalTrees * f);
                if (drawCount === 0 || !inFrustum) {
                    setMeshes(false);
                    continue;
                }
                setMeshes(true, drawCount);
            }
        }
        if (treeBillboardMesh) {
            treeBillboardMesh.count = billboardCount;
            // The anchor / tint / size attributes are static-write each tick;
            // mark them dirty so three.js uploads.
            if (treeBillboardAnchorAttr) treeBillboardAnchorAttr.needsUpdate = true;
            if (treeBillboardTintAttr) treeBillboardTintAttr.needsUpdate = true;
        }
    }

    // Base map images → flat textured ground quads, just below terrain.
    function buildImages(root, visIds) {
        const base = (typeof imageBaseUrl !== 'undefined' && imageBaseUrl) || '';
        const loader = new THREE.TextureLoader();
        let order = 0;
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const img of n.images || []) {
                    if (!elementShown('image', img.id, n.id, visIds)) continue;
                    const w = img.width || 1, h = img.height || 1;
                    const encoded = String(img.path || '')
                        .split('/').map(encodeURIComponent).join('/');
                    const tex = loader.load(base + encoded);
                    tex.colorSpace = THREE.SRGBColorSpace;
                    const mesh = new THREE.Mesh(
                        new THREE.PlaneGeometry(w, h),
                        new THREE.MeshBasicMaterial({
                            map: tex, side: THREE.DoubleSide, depthWrite: false,
                        })
                    );
                    mesh.renderOrder = RO_IMAGE + order;
                    order++;
                    // Lay flat then yaw about world Y by the image's 2D rotation.
                    mesh.rotation.order = 'YXZ';
                    mesh.rotation.x = Math.PI / 2;
                    mesh.rotation.y = -(img.rotation || 0) * DEG;
                    mesh.position.set((img.x || 0) + w / 2, -0.05, (img.y || 0) + h / 2);
                    root.add(mesh);
                }
                walk(n.children);
            }
        })(mapLayers());
    }

    // Flat ground ribbon along a sampled spline centreline. `samples` are the
    // {x,y,w} points from sampleSpline (x/y are 2D world; y here is the south
    // axis). halfWidthFn(i) → half-width at sample i. yPos lifts the ribbon
    // above the terrain; po is the polygonOffset factor.
    function ribbonMesh(samples, halfWidthFn, yPos, color, renderOrder, opacity) {
        const n = samples.length;
        if (n < 2) return null;
        const L = [], R = [];
        for (let i = 0; i < n; i++) {
            const prev = samples[Math.max(0, i - 1)];
            const next = samples[Math.min(n - 1, i + 1)];
            let dx = next.x - prev.x, dy = next.y - prev.y;
            const len = Math.hypot(dx, dy) || 1;
            dx /= len; dy /= len;
            const nx = -dy, ny = dx, hw = halfWidthFn(i);
            L.push({ x: samples[i].x + nx * hw, z: samples[i].y + ny * hw });
            R.push({ x: samples[i].x - nx * hw, z: samples[i].y - ny * hw });
        }
        const pos = [];
        for (let i = 0; i < n - 1; i++) {
            const a = L[i], b = R[i], c = R[i + 1], d = L[i + 1];
            pos.push(a.x, yPos, a.z, b.x, yPos, b.z, c.x, yPos, c.z);
            pos.push(a.x, yPos, a.z, c.x, yPos, c.z, d.x, yPos, d.z);
        }
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        geo.computeVertexNormals();
        const mat = new THREE.MeshLambertMaterial({
            color: toColor(color, 0xcccccc), side: THREE.DoubleSide,
            depthWrite: false,
        });
        if (opacity != null && opacity < 1) { mat.transparent = true; mat.opacity = opacity; }
        const mesh = new THREE.Mesh(geo, mat);
        mesh.renderOrder = renderOrder;
        mesh.receiveShadow = true;
        return mesh;
    }

    // Roads / trails / tracks → flat ground ribbons (casing + fill). Rivers and
    // closed water bodies → a reflective WaterMesh surface that fakes basin
    // depth in-shader. Lane markings / rails are deferred to a later phase.
    function buildSplines(root, visIds) {
        let order = 0;
        // All river/lake geometries collected here, then merged into ONE
        // WaterMesh at the end. Shared flow, shared waves, no junction seams.
        // `waterSources` keeps each contributing spline's 2D outline plus its
        // fill / rim colour — used after merging to RE-BAKE the per-vertex
        // waterDepth + waterFill + waterRim attributes from the union, so
        // overlapping ribbons read as one body of water (no foam streaks down
        // the middle of a wider river where a tributary ended).
        const waterGeos = [];
        const waterSources = [];
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const sp of n.splines || []) {
                    if (!elementShown('spline', sp.id, n.id, visIds)) continue;
                    const pts = sp.points || [];
                    if (pts.length < 2) continue;
                    const prof = splineProfile(sp.kind, sp.preset);
                    const straight = !!prof.straight;
                    const samples = sampleSpline(pts, straight, !!sp.closed);
                    if (samples.length < 2) continue;
                    const extra = (prof.casing && prof.casing.extra) || 0;
                    const fillColor = sp.fillColor
                        || (prof.bands && prof.bands[0] && prof.bands[0].color)
                        || '#cccccc';
                    const casingColor = sp.casingColor
                        || (prof.casing && prof.casing.color) || '#555555';
                    const isRiver = sp.kind === 'river';
                    const ro = order;
                    order++;
                    if (isRiver) {
                        let geo = null;
                        let outline = null;
                        if (sp.closed && samples.length >= 3) {
                            outline = samples.map(s => ({ x: s.x, y: s.y }));
                            geo = buildLakeWaterGeo(outline);
                        } else {
                            outline = riverOutlinePolygon(samples);
                            geo = buildRiverWaterGeo(samples);
                        }
                        if (geo) {
                            // Bake initial per-spline attributes so the merge
                            // has something for sources that DON'T overlap.
                            // The post-merge rebake then overwrites for the
                            // vertices that DO have overlapping sources.
                            bakeColorAttribute(geo, 'waterFill', fillColor);
                            bakeColorAttribute(geo, 'waterRim', casingColor);
                            waterGeos.push(geo);
                            waterSources.push({
                                outline,
                                fill: toColor(fillColor, WATER_FALLBACK_COLOR),
                                rim:  toColor(casingColor, WATER_FALLBACK_COLOR),
                                maxD: 1, // filled in by rebake
                            });
                        }
                    } else {
                        // Roads / trails / tracks keep the casing-under-fill look.
                        const casing = ribbonMesh(samples, i => samples[i].w / 2 + extra,
                            0.45, casingColor, RO_SPLINE + ro);
                        if (casing) root.add(casing);
                        const fill = ribbonMesh(samples, i => samples[i].w / 2,
                            0.5, fillColor, RO_SPLINE + 2000 + ro, 1);
                        if (fill) root.add(fill);
                    }
                }
                walk(n.children);
            }
        })(mapLayers());
        if (waterGeos.length > 0) {
            const merged = mergeWaterGeometries(waterGeos);
            if (merged) {
                rebakeMergedWaterAttrs(merged, waterSources);
                const water = makeWater(merged);
                water.rotation.x = -Math.PI / 2;
                water.position.y = WATER_SURFACE_Y;
                water.renderOrder = RO_SPLINE + 4000 + order;
                root.add(water);
            }
        }
    }

    // Walk every vertex of the merged water mesh and recompute waterDepth +
    // waterFill + waterRim from the UNION of the source polygons. For each
    // vertex collect the distance-to-bank from EVERY source that contains it,
    // then take the max — at a junction interior the vertex is inside both
    // ribbons and the larger of the two distances reads as deep, so foam +
    // depth tint don't streak across the seam. Colour is blended by those
    // same distances so the dominant (deeper-containing) river wins.
    function rebakeMergedWaterAttrs(merged, sources) {
        const pos = merged.attributes.position.array;
        const n = merged.attributes.position.count;
        const depthAttr = merged.attributes.waterDepth.array;
        const fillAttr = merged.attributes.waterFill.array;
        const rimAttr = merged.attributes.waterRim.array;
        // First sweep: for every merged vertex, see which source polygons
        // contain it, and update each containing source's maxD with the
        // vertex's distance-to-bank. Gives a per-source normalisation that
        // matches how the per-spline bake worked before (deepest interior
        // point of that source maps to depth=1).
        for (const s of sources) s.maxD = 1e-6;
        for (let i = 0; i < n; i++) {
            const wx = pos[i * 3], wy = -pos[i * 3 + 1];
            for (const s of sources) {
                if (!pointInPolygon(wx, wy, s.outline)) continue;
                const dd = distToPolygon(wx, wy, s.outline);
                if (dd > s.maxD) s.maxD = dd;
            }
        }
        // SHELF / DROP from buildLakeWaterGeo's basin curve — keep the same
        // profile here so the depth tint matches what individual lakes had.
        const SHELF = 0.10;
        const basinCurve = (m) => {
            if (m <= 0) return 0;
            if (m >= 1) return 1;
            if (m < SHELF) {
                const t = m / SHELF;
                return 0.12 * t * t * (3 - 2 * t);
            }
            const t = (m - SHELF) / (1 - SHELF);
            return 0.12 + 0.88 * t * t * (3 - 2 * t);
        };
        for (let i = 0; i < n; i++) {
            // Mesh local XY is (worldX, -worldZ); source polygons are in
            // (worldX, worldY=worldZ-as-map-coord). Convert.
            const lx = pos[i * 3];
            const ly = pos[i * 3 + 1];
            const wx = lx, wy = -ly;
            let bestNorm = 0;
            let weightSum = 0;
            let fr = 0, fg = 0, fb = 0, rr = 0, rg = 0, rb = 0;
            for (const s of sources) {
                if (!pointInPolygon(wx, wy, s.outline)) continue;
                const dd = distToPolygon(wx, wy, s.outline);
                const norm = dd / (s.maxD * WATER_DEEP_REACH);
                if (norm > bestNorm) bestNorm = norm;
                const w = dd; // weight = how interior this vertex is in this source
                weightSum += w;
                fr += s.fill.r * w; fg += s.fill.g * w; fb += s.fill.b * w;
                rr += s.rim.r * w;  rg += s.rim.g * w;  rb += s.rim.b * w;
            }
            if (weightSum > 0) {
                depthAttr[i] = basinCurve(Math.min(1, bestNorm));
                fillAttr[i * 3]     = fr / weightSum;
                fillAttr[i * 3 + 1] = fg / weightSum;
                fillAttr[i * 3 + 2] = fb / weightSum;
                rimAttr[i * 3]     = rr / weightSum;
                rimAttr[i * 3 + 1] = rg / weightSum;
                rimAttr[i * 3 + 2] = rb / weightSum;
            }
            // else: vertex isn't inside any source polygon — keep the
            // per-spline values baked earlier (rim vertex outside any
            // polygon is essentially on the boundary anyway).
        }
        merged.attributes.waterDepth.needsUpdate = true;
        merged.attributes.waterFill.needsUpdate = true;
        merged.attributes.waterRim.needsUpdate = true;
    }

    // Bake a flat per-vertex rgb attribute named `name` from a hex / css colour
    // string. Lets the merged water mesh tint each contributing spline's
    // vertices independently with no GPU branching.
    function bakeColorAttribute(geo, name, color) {
        const c = toColor(color, WATER_FALLBACK_COLOR);
        const n = geo.attributes.position.count;
        const arr = new Float32Array(n * 3);
        for (let i = 0; i < n; i++) {
            arr[i * 3]     = c.r;
            arr[i * 3 + 1] = c.g;
            arr[i * 3 + 2] = c.b;
        }
        geo.setAttribute(name, new THREE.Float32BufferAttribute(arr, 3));
    }

    // Concatenate several water surface BufferGeometries (each carrying
    // position + waterDepth + flowDir + waterFill + waterRim) into ONE
    // BufferGeometry. Single WaterMesh wraps it; junctions read as merged
    // because there are no separate meshes to z-fight or seam.
    function mergeWaterGeometries(geos) {
        let total = 0;
        for (const g of geos) total += g.attributes.position.count;
        if (total === 0) return null;
        const pos   = new Float32Array(total * 3);
        const depth = new Float32Array(total);
        const flow  = new Float32Array(total * 2);
        const fill  = new Float32Array(total * 3);
        const rim   = new Float32Array(total * 3);
        let off = 0;
        for (const g of geos) {
            const n = g.attributes.position.count;
            pos.set(g.attributes.position.array,   off * 3);
            depth.set(g.attributes.waterDepth.array, off);
            flow.set(g.attributes.flowDir.array,    off * 2);
            fill.set(g.attributes.waterFill.array,  off * 3);
            rim.set(g.attributes.waterRim.array,    off * 3);
            off += n;
            g.dispose();
        }
        const merged = new THREE.BufferGeometry();
        merged.setAttribute('position',   new THREE.Float32BufferAttribute(pos,   3));
        merged.setAttribute('waterDepth', new THREE.Float32BufferAttribute(depth, 1));
        merged.setAttribute('flowDir',    new THREE.Float32BufferAttribute(flow,  2));
        merged.setAttribute('waterFill',  new THREE.Float32BufferAttribute(fill,  3));
        merged.setAttribute('waterRim',   new THREE.Float32BufferAttribute(rim,   3));
        merged.computeVertexNormals();
        return merged;
    }

    // Horizontal polygon cap at height y (floor ledge / ceiling). Optional
    // `holes` (array of {x,y} point arrays) are cut out — used for stairwells.
    function polyCapMesh(pts, y, mat, holes) {
        const shape = new THREE.Shape();
        shape.moveTo(pts[0].x, pts[0].y);
        for (let i = 1; i < pts.length; i++) shape.lineTo(pts[i].x, pts[i].y);
        shape.closePath();
        for (const h of holes || []) {
            if (!h || h.length < 3) continue;
            const path = new THREE.Path();
            path.moveTo(h[0].x, h[0].y);
            for (let i = 1; i < h.length; i++) path.lineTo(h[i].x, h[i].y);
            path.closePath();
            shape.holes.push(path);
        }
        const mesh = new THREE.Mesh(new THREE.ShapeGeometry(shape), mat);
        mesh.rotation.x = Math.PI / 2;
        mesh.position.y = y;
        return mesh;
    }

    // The rotated rectangle footprint of a staircase, as a {x,y} polygon.
    function stairRectPoly(st) {
        const rot = (st.rotation || 0) * DEG, c = Math.cos(rot), s = Math.sin(rot);
        const hw = (st.width || 10) / 2, hl = (st.length || 18) / 2;
        return [[-hw, -hl], [hw, -hl], [hw, hl], [-hw, hl]].map(p => ({
            x: st.x + p[0] * c - p[1] * s,
            y: st.y + p[0] * s + p[1] * c,
        }));
    }

    // Real 3D roof volume on the top floor. gable / hip = eave planes rising to
    // a ridge (hip pulls the ridge ends in); flat = a plain cap. Built off the
    // top outline's oriented bounding box (ridge axis = longest edge).
    function buildRoof3D(b, outline, baseY, mat) {
        const kind = (b.roof && b.roof.kind) || 'gable';
        if (kind === 'flat' || outline.length < 3)
            return polyCapMesh(outline, baseY, mat);
        const pitch = (b.roof && b.roof.pitch != null) ? b.roof.pitch : 0.5;
        const c = polyCentroid(outline);
        const d = footprintRidgeDir(outline);
        const nx = -d.y, ny = d.x;
        let aLo = 1e9, aHi = -1e9, pLo = 1e9, pHi = -1e9;
        for (const q of outline) {
            const px = q.x - c.x, py = q.y - c.y;
            const a = px * d.x + py * d.y, p = px * nx + py * ny;
            aLo = Math.min(aLo, a); aHi = Math.max(aHi, a);
            pLo = Math.min(pLo, p); pHi = Math.max(pHi, p);
        }
        const aSpan = aHi - aLo, pSpan = pHi - pLo;
        const W = (a, p) => ({ x: c.x + d.x * a + nx * p, y: c.y + d.y * a + ny * p });
        const ridgeH = Math.max(2, pitch * (pSpan / 2));
        const e = kind === 'hip' ? Math.min(pSpan * 0.5, aSpan * 0.42) : 0;
        const A = W(aLo, pLo), B = W(aHi, pLo), Cc = W(aHi, pHi), D = W(aLo, pHi);
        const R0 = W(aLo + e, 0), R1 = W(aHi - e, 0);
        const top = baseY + ridgeH;
        const pos = [];
        function tri(p1, y1, p2, y2, p3, y3) {
            pos.push(p1.x, y1, p1.y, p2.x, y2, p2.y, p3.x, y3, p3.y);
        }
        function quad(p1, y1, p2, y2, p3, y3, p4, y4) {
            tri(p1, y1, p2, y2, p3, y3); tri(p1, y1, p3, y3, p4, y4);
        }
        quad(A, baseY, B, baseY, R1, top, R0, top);   // eave plane, pLo side
        quad(D, baseY, Cc, baseY, R1, top, R0, top);  // eave plane, pHi side
        tri(A, baseY, D, baseY, R0, top);             // end / hip, aLo
        tri(B, baseY, Cc, baseY, R1, top);            // end / hip, aHi
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        g.computeVertexNormals();
        return new THREE.Mesh(g, mat);
    }

    // A box for a wall segment (x1,y1)-(x2,y2) in 2D world, from yLo to yHi.
    function wallBox(x1, y1, x2, y2, thickness, yLo, yHi, mat) {
        const dx = x2 - x1, dy = y2 - y1, L = Math.hypot(dx, dy);
        if (L < 1e-3 || yHi <= yLo) return null;
        const m = new THREE.Mesh(new THREE.BoxGeometry(L, yHi - yLo, thickness), mat);
        m.position.set((x1 + x2) / 2, (yLo + yHi) / 2, (y1 + y2) / 2);
        m.rotation.y = Math.atan2(-dy, dx);
        m.renderOrder = RO_BUILDING;
        return m;
    }

    // Gap content for one opening, hosted on segment (hx1,hy1)-(hx2,hy2).
    // window → sill + lintel + translucent pane; door → a thin slab. `wallH` is
    // the host wall's height (defaults to an interior-wall height). Pushes
    // meshes into `out` (merged later by material).
    function addOpening(out, hx1, hy1, hx2, hy2, op, thickness, yBase, mats, wallH) {
        wallH = wallH || IWALL_H;
        const dx = hx2 - hx1, dy = hy2 - hy1, L = Math.hypot(dx, dy) || 1;
        const half = (op.width || 8) / 2 / L;
        const t0 = Math.max(0, op.t - half), t1 = Math.min(1, op.t + half);
        const A = { x: hx1 + dx * t0, y: hy1 + dy * t0 };
        const B = { x: hx1 + dx * t1, y: hy1 + dy * t1 };
        if (op.kind === 'window') {
            const sillTop = yBase + wallH * 0.35;
            const lintelBot = yBase + wallH * 0.70;
            const sill = wallBox(A.x, A.y, B.x, B.y, thickness, yBase, sillTop, mats.wall);
            if (sill) out.push(sill);
            const lintel = wallBox(A.x, A.y, B.x, B.y, thickness, lintelBot, yBase + wallH, mats.wall);
            if (lintel) out.push(lintel);
            const pane = wallBox(A.x, A.y, B.x, B.y, thickness * 0.35, sillTop, lintelBot, mats.pane);
            if (pane) out.push(pane);
        } else {
            const slab = wallBox(A.x, A.y, B.x, B.y, thickness * 0.5, yBase, yBase + wallH * 0.92, mats.door);
            if (slab) out.push(slab);
        }
    }

    // Build a building's shared materials.
    function makeBuildingMats(def) {
        return {
            ext: new THREE.MeshLambertMaterial({
                color: toColor(lerpColor(def.roofColor, '#ffffff', 0.6), 0xddd6c6),
                side: THREE.DoubleSide,
            }),
            roof: new THREE.MeshLambertMaterial({
                color: toColor(def.roofColor, 0xc08552), side: THREE.DoubleSide,
            }),
            wall: new THREE.MeshLambertMaterial({ color: 0x6b6258, side: THREE.DoubleSide }),
            pane: new THREE.MeshLambertMaterial({
                color: 0x9fc4dd, transparent: true, opacity: 0.4,
                side: THREE.DoubleSide, depthWrite: false,
            }),
            door: new THREE.MeshLambertMaterial({
                color: toColor(def.outline, 0x6e4626), side: THREE.DoubleSide,
            }),
            stair: new THREE.MeshLambertMaterial({ color: 0xb9ad97 }),
            floor: new THREE.MeshLambertMaterial({ color: 0xb8af9d, side: THREE.DoubleSide }),
        };
    }

    // Exterior walls — one box per footprint edge per floor, with real gaps cut
    // for exterior openings (wallIndex < 0 → footprint edge -wallIndex-1) so you
    // can actually see in / out through windows and doors.
    function addExteriorWalls(out, b, mats) {
        const fp = b.footprint || [];
        const n = fp.length;
        if (n < 3) return;
        const floors = Math.max(0, b.floorCount || 0);
        const TH = 5; // exterior wall thickness
        // sill/lintel of an exterior opening use the exterior wall colour.
        const extMats = { wall: mats.ext, pane: mats.pane, door: mats.door };
        for (let fi = 0; fi < floors; fi++) {
            const floor = (b.floors || [])[fi] || {};
            const yBase = fi * FLOOR_H, yTop = yBase + FLOOR_H;
            for (let i = 0; i < n; i++) {
                const P1 = fp[i], P2 = fp[(i + 1) % n];
                const x1 = P1.x, y1 = P1.y, x2 = P2.x, y2 = P2.y;
                const ops = (floor.openings || [])
                    .filter(o => o.wallIndex < 0 && (-o.wallIndex - 1) === i)
                    .sort((a, c) => a.t - c.t);
                if (ops.length === 0) {
                    const box = wallBox(x1, y1, x2, y2, TH, yBase, yTop, mats.ext);
                    if (box) out.push(box);
                    continue;
                }
                const L = Math.hypot(x2 - x1, y2 - y1) || 1;
                const pt = t => ({ x: x1 + (x2 - x1) * t, y: y1 + (y2 - y1) * t });
                let cursor = 0;
                for (const op of ops) {
                    const half = (op.width || 8) / 2 / L;
                    const o0 = Math.max(0, op.t - half), o1 = Math.min(1, op.t + half);
                    if (o0 > cursor) {
                        const A = pt(cursor), B = pt(o0);
                        const box = wallBox(A.x, A.y, B.x, B.y, TH, yBase, yTop, mats.ext);
                        if (box) out.push(box);
                    }
                    cursor = Math.max(cursor, o1);
                    addOpening(out, x1, y1, x2, y2, op, TH, yBase, extMats, FLOOR_H);
                }
                if (cursor < 1) {
                    const A = pt(cursor), B = pt(1);
                    const box = wallBox(A.x, A.y, B.x, B.y, TH, yBase, yTop, mats.ext);
                    if (box) out.push(box);
                }
            }
        }
    }

    // Interior wall: solid spans between its openings + the openings' gap content.
    function addInteriorWall(out, w, openings, yBase, mats) {
        const x1 = w.x1, y1 = w.y1, x2 = w.x2, y2 = w.y2;
        const th = w.thickness || 3;
        const L = Math.hypot(x2 - x1, y2 - y1) || 1;
        const pt = t => ({ x: x1 + (x2 - x1) * t, y: y1 + (y2 - y1) * t });
        const ops = openings.slice().sort((a, b) => a.t - b.t);
        let cursor = 0;
        function solid(a, b) {
            if (b - a <= 1e-4) return;
            const A = pt(a), B = pt(b);
            const box = wallBox(A.x, A.y, B.x, B.y, th, yBase + IWALL_H * 0.022, yBase + IWALL_H + IWALL_H * 0.02, mats.wall);
            if (box) out.push(box);
        }
        for (const op of ops) {
            const half = (op.width || 8) / 2 / L;
            const o0 = Math.max(0, op.t - half), o1 = Math.min(1, op.t + half);
            if (o0 > cursor) solid(cursor, o0);
            cursor = Math.max(cursor, o1);
            addOpening(out, x1, y1, x2, y2, op, th, yBase + IWALL_H * 0.022, mats);
        }
        if (cursor < 1) solid(cursor, 1);
    }

    // A staircase: solid stepped mass anchored to the floor, spanning one floor
    // height ('up' rises above yBase, 'down' descends below it). Pushes step
    // boxes into `out`.
    function buildStair(out, st, yBase, mat) {
        const rot = (st.rotation || 0) * DEG;
        const len = st.length || 18, wid = st.width || 10;
        // 2D stair rect long axis = local +Y; rotated by `rot`.
        const rdx = -Math.sin(rot), rdy = Math.cos(rot);
        const sx = st.x - rdx * len / 2, sy = st.y - rdy * len / 2;
        const N = 9, up = st.direction === 'down' ? -1 : 1;
        for (let s = 0; s < N; s++) {
            const frac = (s + 0.5) / N;
            const cx = sx + rdx * len * frac, cy = sy + rdy * len * frac;
            const h = FLOOR_H * (s + 1) / N; // each step solid up to its tread
            const box = new THREE.Mesh(
                new THREE.BoxGeometry(wid, h, len / N), mat);
            box.position.set(cx, yBase + up * h / 2, cy);
            box.rotation.y = Math.atan2(rdx, rdy);
            box.renderOrder = RO_BUILDING;
            out.push(box);
        }
    }

    // Camera-facing text label (canvas texture sprite). Just the text — a
    // subtle light outline gives legibility on any background, no opaque box.
    // `fontSize` (the 2D label's size, world units) drives the world height.
    function makeLabelSprite(text, hexColor, fontSize) {
        text = text || '';
        const fontPx = 128; // high-res render → crisp when scaled in 3D
        const measure = document.createElement('canvas').getContext('2d');
        measure.font = fontPx + 'px "Segoe UI", sans-serif';
        const tw = Math.max(8, measure.measureText(text).width);
        const pad = fontPx * 0.3;
        const cv = document.createElement('canvas');
        cv.width = Math.ceil(tw + pad * 2);
        cv.height = Math.ceil(fontPx + pad * 2);
        const ctx = cv.getContext('2d');
        ctx.font = fontPx + 'px "Segoe UI", sans-serif';
        ctx.textBaseline = 'middle';
        ctx.lineJoin = 'round';
        ctx.lineWidth = fontPx * 0.16;
        ctx.strokeStyle = 'rgba(0,0,0,0.9)';
        ctx.strokeText(text, pad, cv.height / 2);
        ctx.fillStyle = hexColor || '#1c1a18';
        ctx.fillText(text, pad, cv.height / 2);
        const tex = new THREE.CanvasTexture(cv);
        tex.colorSpace = THREE.SRGBColorSpace;
        tex.minFilter = THREE.LinearFilter; // no mip blur
        tex.generateMipmaps = false;
        // depthTest on (walls occlude it — shows only when actually visible);
        // depthWrite off so the transparent quad doesn't block anything.
        const spr = new THREE.Sprite(new THREE.SpriteMaterial({
            map: tex, depthTest: true, depthWrite: false, transparent: true,
        }));
        const wh = (fontSize && fontSize > 0 ? fontSize : 8) * 0.5;
        spr.scale.set(wh * cv.width / cv.height, wh, 1);
        spr.renderOrder = RO_BILLBOARD;
        return spr;
    }

    // An upright pin marker (stem + head) + optional camera-facing label above.
    function makePinMarker(hexColor, label) {
        const g = new THREE.Group();
        const mat = new THREE.MeshLambertMaterial({ color: toColor(hexColor, 0xf9c46a) });
        const stem = new THREE.Mesh(new THREE.CylinderGeometry(0.6, 0.6, 8, 8), mat);
        stem.position.y = 4;
        const head = new THREE.Mesh(new THREE.SphereGeometry(2.4, 12, 10), mat);
        head.position.y = 9;
        g.add(stem); g.add(head);
        if (label) {
            const spr = makeLabelSprite(label, '#1c1a18');
            spr.position.y = 14;
            g.add(spr);
        }
        return g;
    }

    // Per-floor interiors: walls + openings + stairs + floor-scoped labels/pins.
    // All floors render stacked (a 3D building shows every floor at once — the
    // 2D "active floor only" rule is a 2D limitation). Visible by flying inside.
    // Solid interior geometry → `out` (merged by material later); camera-facing
    // labels / pins → `root` (can't be merged).
    function buildBuildingInteriors(out, root, b, mats) {
        const floors = Math.max(0, b.floorCount || 0);
        const fp = b.footprint || [];
        for (let fi = 0; fi < floors; fi++) {
            const floor = (b.floors || [])[fi] || {};
            const yBase = fi * FLOOR_H;
            // Floor slab (= ceiling of the level below), with stairwell holes
            // cut only for staircases that actually pass through THIS slab:
            // 'up' stairs on the level below, and 'down' stairs on this level.
            if (fp.length >= 3) {
                const below = (fi > 0 && (b.floors || [])[fi - 1]) || {};
                const upBelow = (below.stairs || [])
                    .filter(s => (s.direction || 'up') !== 'down');
                const downHere = (floor.stairs || [])
                    .filter(s => s.direction === 'down');
                const holes = upBelow.concat(downHere).map(stairRectPoly);
                const slab = polyCapMesh(fp, yBase, mats.floor, holes);
                slab.renderOrder = RO_BUILDING;
                out.push(slab);
            }
            const walls = floor.walls || [];
            walls.forEach((w, wi) => {
                const ops = (floor.openings || []).filter(o => o.wallIndex === wi);
                addInteriorWall(out, w, ops, yBase, mats);
            });
            // Openings on the outer outline (wallIndex < 0) are handled by
            // addExteriorWalls — it cuts real gaps in the exterior walls.
            (floor.stairs || []).forEach(st => buildStair(out, st, yBase, mats.stair));
            (floor.labels || []).forEach(l => {
                if (!l.text) return;
                const spr = makeLabelSprite(l.text, l.color, l.fontSize);
                spr.position.set(l.x, yBase + FLOOR_H * 0.5, l.y);
                root.add(spr);
            });
            (floor.pins || []).forEach(p => {
                const m = makePinMarker(p.color, p.label);
                m.position.set(p.x, yBase, p.y);
                root.add(m);
            });
        }
    }

    // Buildings → a full-footprint wall prism + a real 3D roof, plus per-floor
    // interiors (walls / openings / stairs / floor-scoped labels & pins).
    function buildBuildings(root, visIds) {
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const b of n.buildings || []) {
                    if (!elementShown('building', b.id, n.id, visIds)) continue;
                    const fp = b.footprint || [];
                    if (fp.length < 3) continue;
                    const def = buildingTypeDef(b.type);
                    const mats = makeBuildingMats(def);
                    const floors = Math.max(0, b.floorCount || 0);
                    if (floors < 1) {
                        // playground / pad — a flat coloured surface.
                        const pad = polyCapMesh(fp, 0.3, mats.roof);
                        pad.renderOrder = RO_BUILDING;
                        root.add(pad);
                        continue;
                    }
                    // Exterior massing: walls go straight up the full footprint
                    // for every floor — only the roof slopes. Exterior walls are
                    // gapped at openings so windows / doors see through.
                    const wallTop = floors * FLOOR_H;
                    const bMeshes = [];
                    addExteriorWalls(bMeshes, b, mats);
                    bMeshes.push(polyCapMesh(fp, wallTop, mats.roof));
                    bMeshes.push(buildRoof3D(b, fp, wallTop, mats.roof));
                    buildBuildingInteriors(bMeshes, root, b, mats);
                    // Merge this building's solid geometry by material — one draw
                    // call per material instead of one per box / slab / step.
                    for (const m of mergeByMaterial(bMeshes)) {
                        m.renderOrder = RO_BUILDING;
                        // Transparent geometry (window panes) must NOT cast
                        // shadows — the shadow map treats it as opaque, so the
                        // window would block all light. Let light through.
                        m.castShadow = !m.material.transparent;
                        m.receiveShadow = true;
                        root.add(m);
                    }
                }
                walk(n.children);
            }
        })(mapLayers());
    }

    // Map-wide pins & labels (flat mapData.pins / mapData.labels, layer-bound by
    // their layerId). Pins sit on the ground; labels float just above it.
    function buildMapAnnotations(root, visIds) {
        for (const p of (mapData.pins || [])) {
            if (!elementShown('pin', p.id, p.layerId || '', visIds)) continue;
            const m = makePinMarker(p.color, p.label);
            m.position.set(p.x, 0, p.y);
            root.add(m);
        }
        for (const l of (mapData.labels || [])) {
            if (!l.text) continue;
            if (!elementShown('label', l.id, l.layerId || '', visIds)) continue;
            const spr = makeLabelSprite(l.text, l.color, l.fontSize);
            spr.position.set(l.x, 1, l.y);
            root.add(spr);
        }
    }

    function buildScene() {
        if (sceneRoot) {
            scene.remove(sceneRoot);
            disposeTree(sceneRoot);
        }
        sceneRoot = new THREE.Group();
        sceneRoot.scale.setScalar(WORLD_SCALE);
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
        sceneRoot.add(ground);
        const visIds = visibleLayerIds();
        buildImages(sceneRoot, visIds);
        buildTerrain(sceneRoot, visIds);
        buildGrass(sceneRoot, visIds);
        buildTrees(sceneRoot, visIds);
        buildSplines(sceneRoot, visIds);
        buildBuildings(sceneRoot, visIds);
        buildMapAnnotations(sceneRoot, visIds);
        scene.add(sceneRoot);
    }

    function resize() {
        if (!renderer) return;
        const w = window.innerWidth, h = window.innerHeight;
        renderer.setSize(w, h, false);
        camera.aspect = w / h || 1;
        camera.updateProjectionMatrix();
    }

    // Skip WASD / Q / E when focus is in a typeable field (e.g. lil-gui number
    // input). Without this the camera moves while you type slider values.
    function isTypingTarget(el) {
        if (!el) return false;
        const tag = el.tagName;
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
    }

    function wireInput() {
        if (inputWired) return;
        inputWired = true;
        canvas.addEventListener('mousedown', function () {
            if (active) canvas.requestPointerLock();
        });
        document.addEventListener('mousemove', function (e) {
            if (!active || document.pointerLockElement !== canvas) return;
            yaw -= e.movementX * LOOK_SPEED;
            pitch -= e.movementY * LOOK_SPEED;
            const lim = Math.PI / 2 - 0.05;
            pitch = Math.max(-lim, Math.min(lim, pitch));
        });
        window.addEventListener('keydown', function (e) {
            if (!active) return;
            // Ignore keystrokes that the user is typing into the lil-gui sky
            // panel — without this, hitting "a" in a number field strafes the
            // camera and the value never reaches the input.
            if (isTypingTarget(e.target)) return;
            keys[e.code] = true;
            // Esc releases pointer lock (browser already does this); swallow so
            // it doesn't bubble to the 2D editor's Esc handlers.
            if (e.code === 'Escape') e.stopPropagation();
        });
        window.addEventListener('keyup', function (e) {
            if (isTypingTarget(e.target)) return;
            keys[e.code] = false;
        });
        window.addEventListener('resize', function () { if (active) resize(); });
    }

    // Camera forward vector from yaw / pitch. yaw rotates around +y; x = sin,
    // z = cos so frameCamera()'s atan2(x, z) stays consistent.
    function camDir() {
        return new THREE.Vector3(
            Math.sin(yaw) * Math.cos(pitch),
            Math.sin(pitch),
            Math.cos(yaw) * Math.cos(pitch)
        );
    }

    // Force-allocate GPU resources (pipeline, bind groups, instance buffers)
    // for every pool mesh up front so the FIRST visibility of a slot during
    // movement doesn't stall renderer.render(). Runs once after buildScene.
    async function preWarmGrass() {
        if (!renderer || grassPool.length === 0) return;
        // Pre-compile material pipelines. Lazy bind-group / buffer allocation
        // on first visibility is unavoidable per-slot, but compileAsync gets
        // the shader compile out of the way before the tick loop starts.
        // (An earlier attempt to force-allocate by rendering all pool meshes
        // visible blacked out the WebGPU renderer — keep it simple.)
        if (renderer.compileAsync) {
            try { await renderer.compileAsync(scene, camera); } catch (e) {}
        }
    }

    function frameCamera() {
        const b = mapBounds();
        // mapBounds is in unscaled map units; the scene is scaled by WORLD_SCALE.
        const s = WORLD_SCALE;
        const cx = (b.minX + b.maxX) / 2 * s, cz = (b.minY + b.maxY) / 2 * s;
        const span = Math.max(600, b.maxX - b.minX, b.maxY - b.minY) * s;
        camera.position.set(cx, span * 0.8, cz + span * 0.9);
        const to = new THREE.Vector3(cx, 0, cz).sub(camera.position).normalize();
        pitch = Math.asin(to.y);
        yaw = Math.atan2(to.x, to.z);
    }

    // Refresh the top-left 3D HUD (FPS + grass GPU memory estimate + pool /
    // cache stats + JS heap). Throttled to ~5 Hz so text doesn't strobe.
    function updateHud3d(now) {
        if (!hud3dEl) return;
        if (now - hudLastUpdate < 200) return;
        hudLastUpdate = now;
        // Per-instance buffer cost: 16-float instance matrix + grassRoot (2)
        // + grassTint (3) + grassPhase (1) at 4 bytes each.
        const bytesPerInstance = (16 + 2 + 3 + 1) * 4;
        const grassBytes = grassPool.length
            * GRASS_MAX_BLADES_PER_TILE * bytesPerInstance;
        const grassMB = grassBytes / (1024 * 1024);
        const poolUsed = grassPool.length - grassFreeSlots.length;
        let drawnTiles = 0;
        for (const tile of grassTileCache.values()) {
            if (tile && tile.visible && tile.count > 0) drawnTiles++;
        }
        const billboardCount = grassBillboardMesh ? grassBillboardMesh.count : 0;
        const lines = [
            'FPS: ' + hudFps.toFixed(0),
            'Grass VRAM (est): ' + grassMB.toFixed(0) + ' MB',
            'Grass: ' + poolUsed + '/' + grassPool.length
                + ' (' + drawnTiles + ' blade, ' + billboardCount + ' billboard)',
        ];
        if (TREE_STATIC_MODE) {
            const treeCount = treeBarkStaticMesh ? treeBarkStaticMesh.count : 0;
            lines.push('Trees: ' + treeCount + ' (static)');
        } else {
            let drawnTreeTiles = 0;
            for (const t of treeTileCache.values()) {
                if (t && t.visible && t.count > 0) drawnTreeTiles++;
            }
            const treePoolUsed = treePool.length - treeFreeSlots.length;
            const treeBbCount = treeBillboardMesh ? treeBillboardMesh.count : 0;
            lines.push('Trees: ' + treePoolUsed + '/' + treePool.length
                + ' (' + drawnTreeTiles + ' tile, ' + treeBbCount + ' billboard)');
        }
        const perf = (typeof performance !== 'undefined') ? performance : null;
        if (perf && perf.memory) {
            const heapMB = perf.memory.usedJSHeapSize / (1024 * 1024);
            lines.push('JS heap: ' + heapMB.toFixed(0) + ' MB');
        }
        hud3dEl.textContent = lines.join('\n');
    }

    function tick(t) {
        if (!active) return;
        rafId = requestAnimationFrame(tick);
        const dt = Math.min(0.05, (t - lastT) / 1000 || 0);
        lastT = t;
        // EMA-smoothed FPS for the HUD.
        if (dt > 0) hudFps = hudFps * 0.9 + (1 / dt) * 0.1;
        const fwd = camDir();
        const right = new THREE.Vector3()
            .crossVectors(fwd, new THREE.Vector3(0, 1, 0)).normalize();
        const move = new THREE.Vector3();
        if (keys['KeyW']) move.add(fwd);
        if (keys['KeyS']) move.sub(fwd);
        if (keys['KeyD']) move.add(right);
        if (keys['KeyA']) move.sub(right);
        if (keys['KeyE']) move.y += 1;
        if (keys['KeyQ']) move.y -= 1;
        if (move.lengthSq() > 0) {
            const sp = MOVE_SPEED * WORLD_SCALE * dt
                * ((keys['ShiftLeft'] || keys['ShiftRight']) ? BOOST : 1);
            camera.position.addScaledVector(move.normalize(), sp);
        }
        // Keep the camera above the ground. Looking down and pressing W would
        // otherwise push the camera below Y=0; from underground every blade,
        // building, and the backdrop renders from the wrong side and the
        // scene visually "stops moving" until the user pitches back up.
        if (camera.position.y < 1) camera.position.y = 1;
        camera.lookAt(camera.position.clone().add(camDir()));
        updateSunShadow(); // shadow frustum tracks the camera
        updateGrassVisibility(); // hide grass tiles outside the render radius
        updateTreeVisibility();
        updateHud3d(t);
        // WaterMesh advances its own flow animation via the renderer's
        // updateBefore hook — no manual time stepping needed here.
        const tRender0 = GRASS_DEBUG ? performance.now() : 0;
        renderer.render(scene, camera);
        if (GRASS_DEBUG) {
            const renderDt = performance.now() - tRender0;
            grassDebug.renderMs += renderDt;
            grassDebug.renderFrames++;
            if (renderDt > grassDebug.renderMaxMs) grassDebug.renderMaxMs = renderDt;
            // Capture per-frame GPU draw count BEFORE three.js auto-resets it
            // on the next render — but for clarity also fold this into the
            // running max-per-frame so the per-second log can show worst case.
            const info = renderer.info && renderer.info.render;
            if (info) {
                if (info.calls > grassDebug.renderMaxCalls) grassDebug.renderMaxCalls = info.calls;
            }
        }
    }

    window.Map3D = {
        isActive: function () { return active; },
        enter: async function () {
            const ok = await ensureRenderer();
            if (!ok) {
                // WebGPU unavailable — still swap to the (fallback-covered) 3D
                // canvas so the message shows, but don't start a render loop.
                if (canvas) canvas.style.display = 'block';
                if (typeof stage !== 'undefined') stage.style.display = 'none';
                if (typeof hud !== 'undefined' && hud) hud.style.display = 'none';
                try { sendMessage({ type: 'map3dEntered' }); } catch (e) {}
                return;
            }
            hideFallbackMessage();
            active = true;
            canvas.style.display = 'block';
            if (typeof stage !== 'undefined') stage.style.display = 'none';
            if (typeof hud !== 'undefined' && hud) hud.style.display = 'none';
            if (!hud3dEl) hud3dEl = document.getElementById('hud3d');
            if (hud3dEl) hud3dEl.style.display = 'block';
            resize();
            // Wrap the whole entry sequence so any thrown error surfaces in
            // the host C# console instead of leaving a black canvas.
            try {
                log('[Map3D.enter] start');
                try { sendMessage({ type: 'map3dStep', step: 'before-build' }); } catch (e) {}
                log('[Map3D.enter] loading tree assets...');
                const treesOk = await loadTreeAssets();
                log('[Map3D.enter] tree assets loaded: ' + treesOk);
                try { sendMessage({ type: 'map3dStep', step: 'after-tree-assets' }); } catch (e) {}
                log('[Map3D.enter] buildScene()...');
                buildScene();
                log('[Map3D.enter] buildScene done');
                try { sendMessage({ type: 'map3dStep', step: 'after-build' }); } catch (e) {}
                frameCamera();
                try { sendMessage({ type: 'map3dStep', step: 'after-frame' }); } catch (e) {}
                lastT = performance.now();
                rafId = requestAnimationFrame(tick);
                log('[Map3D.enter] tick scheduled');
                try { sendMessage({ type: 'map3dEntered' }); } catch (e) {}
            } catch (err) {
                const msg = err && err.message ? err.message : String(err);
                log('[Map3D.enter] EXCEPTION: ' + (err && err.stack ? err.stack : err));
                active = false;
                try { sendMessage({ type: 'map3dError', message: msg }); } catch (e) {}
            }
        },
        exit: function () {
            active = false;
            if (canvas) canvas.style.display = 'none';
            if (typeof stage !== 'undefined') stage.style.display = '';
            if (typeof hud !== 'undefined' && hud) hud.style.display = '';
            if (hud3dEl) hud3dEl.style.display = 'none';
            // Full teardown: dispose renderer / scene / textures so the next
            // enter() rebuilds from scratch. Re-using a stale WebGPU device
            // after the canvas was hidden caused a stale-frame + stall on
            // reopen.
            tearDown();
            try { sendMessage({ type: 'map3dExited' }); } catch (e) {}
        },
        // Full rebuild — called by the host / map.html on mapChanged while 3D
        // is active. Phase 1 only rebuilds the ground plane.
        rebuild: function () {
            if (!active || !renderer) return;
            buildScene();
        },
    };
})();
