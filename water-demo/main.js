// Standalone water-shader demo. Runs the same WaterMesh + colorNode wrapper
// the map view uses, on a flat plane, with cubes that cast shadows on the
// water. Lets us iterate the caustic / foam / scatter / shadow logic without
// rebuilding the Avalonia app each time.
//
// To run: serve the repo root over HTTP (file:// blocks module loading) e.g.
//   python -m http.server 8000
// then open http://localhost:8000/water-demo/index.html

import * as THREE from 'three';
import { WaterMesh } from '../Novalist.Desktop/Assets/Map/Water2Mesh.js';
import {
    Fn, vec2, vec3, vec4, attribute, screenUV, viewportSafeUV,
    viewportSharedTexture, cameraPosition, positionWorld, normalize, mix,
    texture, time, uv, uniform, lightShadowMatrix,
} from 'three/tsl';

// === Tunables (kept in sync with map3d.js) =================================
const WATER_FALLBACK_COLOR = 0x4a7a9a;
const WATER_DEEP_MULT = 0.02;     // deep colour = fill * this; near-zero = full-black deepest point
const WATER_DEEP_TINT = 1.0;
const BASIN_DEPTH = 1.2;
const WATER_RIPPLE = 10;
const WATER_SHINE = 0.5;
const WATER_NORMAL_SCALE = 3;
const WATER_NORMAL_SCALE2 = 1.7;
const WATER_REFLECT_DISTORT = 0.2;
const WATER_UV_TILE = 28;

const WATER_FOAM_WIDTH = 0.022;
const WATER_FOAM_SCALE = 60;
const WATER_FOAM_DRIFT = 0.012;
const WATER_FOAM_SPEED = 1.6;
const WATER_FOAM_COLOR = 0xe6eef0;

const WATER_SCATTER = 0.6; 
const WATER_SCATTER_POWER = 8;
const WATER_SCATTER_COLOR = 0xd8eccd;

const WATER_CAUSTIC = 10.8;
const WATER_CAUSTIC_SCALE = 0.06;
const WATER_CAUSTIC_SPEED = 0.04;
const WATER_CAUSTIC_SHARP = 1.4;
const WATER_CAUSTIC_COLOR = 0xfff5d0;
const WATER_CAUSTIC_PARALLAX = 0.6;     // how much the bed shifts under view (with depth)
const WATER_CAUSTIC_TINT = 1.0;          // how strongly caustics take the water's colour at depth
const WATER_CAUSTIC_MIN_SCALE = 2.0;    // pattern scale at the shore (d=0); raise to keep shallow caustics from stretching huge
const WATER_CAUSTIC_DEPTH_SCALE = 2.0;   // extra scale added with depth — final = (MIN + d * DEPTH_SCALE) * WATER_CAUSTIC_SCALE
const WATER_SHADOW_BIAS = 0.0008;
const WATER_BED_DEPTH = 15;          // world units the fake bed sits below the water surface (shifts the shadow sample by depth)
const WATER_SHADOW_DARKEN = 0.5;    // how strongly shadow pulls colour toward shadow-floor (0 = no shadow, 1 = full shadow-floor)
const WATER_SHADOW_BLUR = 0.012;    // PCF kernel growth with depth (shadow softens as it falls deeper)
const WATER_SHADOW_FLOOR_MULT = 10.0; // shadow-floor colour = deepRGB * this (never pure black)
const WATER_SHADOW_DEPTH_FADE = 1.0;  // 0 = shadow uniform at all depths, 1 = shadow vanishes at full depth

// Debug switches. Cycle through to isolate which stage kills caustics.
//   'raw'      - single c0 sample (texture verification)
//   'pattern'  - c0*c1, powered, brightened (no masks)
//   'depth'    - cMask (depth bell-curve mask only)
//   'shadow'   - shadowMask (the shadow sample only)
//   'shadow-uv'- shadow uv as red/green (sanity-check projection)
//   'shadow-d' - sDepth as grayscale (sanity-check depth z/w)
//   'shadow-y' - shadowMask with sUV.y flipped (WebGPU texture y-down)
//   'sun'      - sunOverhead (sun-elevation only)
//   'no-shadow'- full caustics path BUT with shadowMask forced to 1
//   null       - normal water shader
const DEBUG_CAUSTIC = null;

// === Procedural ripple normal maps =========================================
const waterNormalsTex = [];
function waterNormalsTexture(variant) {
    if (waterNormalsTex[variant]) return waterNormalsTex[variant];
    const S = 256;
    let seed = variant ? 0x9e3779b9 : 0x1337c0de;
    const rand = () => {
        seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
        return seed / 4294967296;
    };
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
    tex.colorSpace = THREE.NoColorSpace;
    waterNormalsTex[variant] = tex;
    return tex;
}

let _causticTex = null;
function causticTexture() {
    if (_causticTex) return _causticTex;
    const t = new THREE.TextureLoader().load('../Novalist.Desktop/Assets/Map/caustics.jpg');
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.colorSpace = THREE.SRGBColorSpace;
    _causticTex = t;
    return t;
}

function toColor(hex, fallback) {
    try {
        if (hex == null || hex === '') return new THREE.Color(fallback);
        return new THREE.Color(hex);
    } catch (e) { return new THREE.Color(fallback); }
}

function applyWaterUVs(geo) {
    const pos = geo.attributes.position;
    const uvArr = new Float32Array(pos.count * 2);
    for (let i = 0; i < pos.count; i++) {
        uvArr[i * 2]     = pos.getX(i) / WATER_UV_TILE;
        uvArr[i * 2 + 1] = pos.getY(i) / WATER_UV_TILE;
    }
    geo.setAttribute('uv', new THREE.Float32BufferAttribute(uvArr, 2));
}

// === Water material (shader wrapper) =======================================
function makeWater(geometry, fillColor, sun) {
    applyWaterUVs(geometry);
    const col = toColor(fillColor, WATER_FALLBACK_COLOR);
    const water = new WaterMesh(geometry, {
        color: col,
        scale: WATER_NORMAL_SCALE,
        scale2: WATER_NORMAL_SCALE2,
        reflectDistort: WATER_REFLECT_DISTORT,
        flowSpeed: 0.12,
        reflectivity: 0.08,
        normalMap0: waterNormalsTexture(0),
        normalMap1: waterNormalsTexture(1),
    });
    water.material.side = THREE.DoubleSide;
    water.material.transparent = false;
    water.material.depthWrite = true;
    // Disable any built-in shadow reception — the WaterMesh's reflection node
    // already captures the scene's shadows (reflected on the ground / cubes),
    // and three's auto path can also apply a surface-projected shadow. We
    // render ONLY the depth-distorted custom shadow below.
    water.receiveShadow = false;
    water.castShadow = false;
    water.material.lights = false;
    const surface = water.material.colorNode;
    const deep = col.clone().multiplyScalar(WATER_DEEP_MULT);
    const shallowRGB = vec3(col.r, col.g, col.b);
    const deepRGB = vec3(deep.r, deep.g, deep.b);
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
    const SUN_DIR = (function () {
        const v = { x: -0.55, y: 1.0, z: -0.84 };
        const l = Math.hypot(v.x, v.y, v.z);
        return { x: v.x / l, y: v.y / l, z: v.z / l };
    })();
    const sunDirVec = vec3(SUN_DIR.x, SUN_DIR.y, SUN_DIR.z);
    const foamNoiseTex = texture(waterNormalsTexture(0));
    const causticTex = texture(causticTexture());

    // WebGPU shadow depth lives in sun.shadow.map.depthTexture (real
    // DepthTexture); .texture is the color attachment (often empty).
    const shadowDepth =
        (sun && sun.shadow && sun.shadow.map && sun.shadow.map.depthTexture)
            ? sun.shadow.map.depthTexture : null;
    const shadowMapNode = shadowDepth ? texture(shadowDepth) : null;
    // TSL auto-wires this matrix to sun.shadow.matrix every render — no
    // manual uniform copy needed (the manual version was misaligned).
    const sunShadowMat = sun && sun.castShadow ? lightShadowMatrix(sun) : null;

    water.material.colorNode = Fn(() => {
        const dRaw = attribute('waterDepth').toVar();
        const d = dRaw.mul(BASIN_DEPTH).clamp(0, 1).toVar();

        const eye = normalize(cameraPosition.sub(positionWorld));
        const parUV = screenUV.sub(eye.xz.mul(d).mul(0.0));
        const bed = viewportSharedTexture(viewportSafeUV(parUV)).rgb;
        const shallow = mix(bed, shallowRGB, WATER_DEEP_TINT);
        const depthColor = mix(shallow, deepRGB, d);

        let col = mix(depthColor, surface.rgb, WATER_SHINE);

        // Scatter halo (added later, on top of shadow — so the sun glare
        // still renders across shadowed water like a real specular highlight
        // that the shadow doesn't intercept).
        const fwd = eye.dot(sunDirVec).max(0);
        const scatter = fwd.pow(WATER_SCATTER_POWER).mul(d).mul(WATER_SCATTER);

        // Caustics — sampled jpg texture, animated, masked by depth/sun/shadow.
        // Parallax: the bed isn't at the water surface, so its sampled position
        // shifts opposite to the view direction with depth — caustics bend as
        // the camera moves. Scale grows with depth too (the deeper the bed,
        // the smaller / denser the caustic cells look from above).
        const cParallaxShift = eye.xz.mul(d).mul(WATER_CAUSTIC_PARALLAX);
        const cScale = d.mul(WATER_CAUSTIC_DEPTH_SCALE)
            .add(WATER_CAUSTIC_MIN_SCALE).mul(WATER_CAUSTIC_SCALE);
        const cP = positionWorld.xz.sub(cParallaxShift).mul(cScale).toVar();
        const ct = time.mul(WATER_CAUSTIC_SPEED);
        const cUV0 = cP.add(vec2(ct, ct.mul(0.7)));
        const cUV1 = cP.mul(1.31).add(vec2(ct.mul(-0.83), ct.mul(0.91)));
        const c0 = causticTex.sample(cUV0).r;
        const c1 = causticTex.sample(cUV1).r;
        const caustic = c0.mul(c1).pow(WATER_CAUSTIC_SHARP).mul(6);

        // Wide caustic visibility window: fades to 0 right at the shore (no
        // water column above the bed) and fades to a low residual at full
        // depth (most light absorbed, but not pitch dark — keeps the flat
        // lake floor from going caustic-less).
        const cMask = d.smoothstep(0, 0.03).mul(mix(0.25, 1.0, d.oneMinus()));
        const sunOverhead = sunDirVec.y.max(0);
        let shadowMask;
        let sUV, sDepth;
        if (shadowMapNode && sunShadowMat) {
            // Bed point straight down from the surface (camera-independent,
            // so the shadow stays anchored to the caster). Project bedPos
            // into shadow space → take its UV. But COMPARE with the surface's
            // own depth (not bedPos's) — if we used bedPos.z, the bed-y drop
            // would push depth past the actual ground plane (y=-0.05) and
            // EVERYTHING in deep water would falsely read as ground-shadowed.
            // Using surface depth: comparison answers "is the cube between
            // the sun and the bed-projection's uv". Bend comes from bedPos.uv
            // shifting with dRaw (world -Y projects to angled-shadow uv).
            const bedPos = vec3(
                positionWorld.x,
                positionWorld.y.sub(dRaw.mul(WATER_BED_DEPTH)),
                positionWorld.z);
            const sBed = sunShadowMat.mul(vec4(bedPos, 1.0));
            const sSurface = sunShadowMat.mul(vec4(positionWorld, 1.0));
            // WebGPU texture coords are y-down; flip y for the shadow uv.
            sUV = vec2(sBed.x.div(sBed.w), sBed.y.div(sBed.w).oneMinus());
            sDepth = sSurface.z.div(sSurface.w);
            const refD = sDepth.sub(WATER_SHADOW_BIAS);
            // 9-tap PCF (centre + 4 cardinals + 4 diagonals, diagonals at 0.7r
            // so all 8 outer taps sit on a circle of roughly radius r). Kernel
            // grows with depth so shadow blurs more in deeper water (light
            // scatters through more water column before being blocked).
            const r = d.mul(WATER_SHADOW_BLUR).add(1.0 / 2048.0);
            const rd = r.mul(0.7071);
            const nr = r.negate(); const nrd = rd.negate();
            const s0 = shadowMapNode.sample(sUV).compare(refD);
            const s1 = shadowMapNode.sample(sUV.add(vec2( r, 0))).compare(refD);
            const s2 = shadowMapNode.sample(sUV.add(vec2(nr, 0))).compare(refD);
            const s3 = shadowMapNode.sample(sUV.add(vec2(0,  r))).compare(refD);
            const s4 = shadowMapNode.sample(sUV.add(vec2(0, nr))).compare(refD);
            const s5 = shadowMapNode.sample(sUV.add(vec2( rd,  rd))).compare(refD);
            const s6 = shadowMapNode.sample(sUV.add(vec2(nrd,  rd))).compare(refD);
            const s7 = shadowMapNode.sample(sUV.add(vec2( rd, nrd))).compare(refD);
            const s8 = shadowMapNode.sample(sUV.add(vec2(nrd, nrd))).compare(refD);
            shadowMask = s0.add(s1).add(s2).add(s3).add(s4)
                .add(s5).add(s6).add(s7).add(s8).mul(1.0 / 9.0);
        } else {
            shadowMask = sunOverhead.mul(0).add(1);
            sUV = vec2(0, 0);
            sDepth = vec3(0, 0, 0).x;
        }

        if (DEBUG_CAUSTIC === 'shadow-uv') return vec4(sUV.x, sUV.y, 0, 1);
        if (DEBUG_CAUSTIC === 'shadow-d') return vec4(sDepth, sDepth, sDepth, 1);

        // ---- DEBUG: short-circuit to inspect caustic stages ------------
        if (DEBUG_CAUSTIC === 'raw') return vec4(c0, c0, c0, 1.0);
        if (DEBUG_CAUSTIC === 'pattern') return vec4(caustic, caustic, caustic, 1.0);
        if (DEBUG_CAUSTIC === 'depth') return vec4(cMask, cMask, cMask, 1.0);
        if (DEBUG_CAUSTIC === 'shadow') return vec4(shadowMask, shadowMask, shadowMask, 1.0);
        if (DEBUG_CAUSTIC === 'sun') return vec4(sunOverhead, sunOverhead, sunOverhead, 1.0);
        // ----------------------------------------------------------------
        const shadowForBlend = DEBUG_CAUSTIC === 'no-shadow' ? shadowMask.mul(0).add(1) : shadowMask;

        // Shadow darken FIRST — applied to base water, BEFORE caustic addition.
        // Shadow intensity FADES with depth: in deep water the base is already
        // very dark so the shadow blends into it (becomes invisible). Mix
        // toward the local col scaled by (1 - DARKEN), so shallow shadows
        // darken noticeably while deep shadows vanish.
        const shadowFade = d.mul(WATER_SHADOW_DEPTH_FADE).oneMinus().clamp(0, 1);
        const shadowStr = shadowFade.mul(WATER_SHADOW_DARKEN);
        const shadowedCol = col.mul(shadowStr.oneMinus());
        col = mix(shadowedCol, col, shadowForBlend);

        // Scatter halo on top of shadow — sun glare reads across shadowed
        // water rather than being eaten by the darkening.
        col = col.add(scatterRGB.mul(scatter));

        // Caustics added LAST, ON TOP, with no shadow term at all. Tuning any
        // WATER_SHADOW_* knob can never change the caustic look — caustic is
        // purely governed by WATER_CAUSTIC*. Stylised: caustics still appear
        // inside shadow areas; if you want them dimmed there too, multiply by
        // shadowForBlend here, but at the cost of recoupling the two systems.
        const causticTinted = mix(causticRGB, shallowRGB, d.mul(WATER_CAUSTIC_TINT));
        col = col.add(causticTinted.mul(caustic).mul(cMask).mul(sunOverhead).mul(WATER_CAUSTIC));

        // Shoreline foam
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

// === Water plane geometry with baked attributes ============================
// Square plane subdivided. waterDepth bakes 0 at the edges -> 1 in the middle
// (so depth tint + caustics behave like a "lake" out to the rim). flowDir is
// a constant uniform drift so ripples animate.
function buildDemoWaterGeo(size, segs) {
    const g = new THREE.PlaneGeometry(size, size, segs, segs);
    g.rotateX(-Math.PI / 2); // lay flat in XZ
    const pos = g.attributes.position;
    const n = pos.count;
    const depth = new Float32Array(n);
    const flow = new Float32Array(n * 2);
    const half = size / 2;
    // Lake-bed profile: gentle shelf near the shore, steep dropoff, flat
    // floor across the centre — closer to a real seabed than a single
    // smoothstep. SHELF / DROP are fractions of (rim → centre) distance.
    const SHELF = 0.10; // outer 10% is gentle shallow shelf
    const DROP  = 0.45; // by 45% in, full depth is reached
    function basinCurve(m) {
        if (m <= 0) return 0;
        if (m >= DROP) return 1;
        if (m < SHELF) {
            const t = m / SHELF;
            return 0.12 * t * t * (3 - 2 * t); // shelf ramps gently to 0.12
        }
        const t = (m - SHELF) / (DROP - SHELF);
        return 0.12 + 0.88 * t * t * (3 - 2 * t); // steep drop to full depth
    }
    for (let i = 0; i < n; i++) {
        const x = pos.getX(i), z = pos.getZ(i);
        const dx = 1 - Math.min(1, Math.abs(x) / half);
        const dz = 1 - Math.min(1, Math.abs(z) / half);
        const m = Math.min(dx, dz);
        depth[i] = basinCurve(m);
        flow[i * 2]     = 1.0;
        flow[i * 2 + 1] = 0.0;
    }
    g.setAttribute('waterDepth', new THREE.Float32BufferAttribute(depth, 1));
    g.setAttribute('flowDir', new THREE.Float32BufferAttribute(flow, 2));
    // Local XY for the wrapper assumes (worldX, -worldZ); but here the plane
    // is already in XZ so we set position.xy to (x, z) directly. Re-author
    // position so applyWaterUVs / the colorNode see local XY = (x, z).
    const localPos = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
        localPos[i * 3]     = pos.getX(i);
        localPos[i * 3 + 1] = pos.getZ(i);
        localPos[i * 3 + 2] = 0;
    }
    g.setAttribute('position', new THREE.Float32BufferAttribute(localPos, 3));
    g.computeVertexNormals();
    return g;
}

// === Scene setup ===========================================================
const canvas = document.getElementById('stage');
const renderer = new THREE.WebGPURenderer({ canvas, antialias: true });
renderer.setPixelRatio(window.devicePixelRatio || 1);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.0;
await renderer.init();

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x9ec7e0);

const camera = new THREE.PerspectiveCamera(55, 1, 0.1, 2000);

// Lights
scene.add(new THREE.AmbientLight(0xffffff, 1.4));
const sun = new THREE.DirectionalLight(0xffffff, 2.8);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
sun.shadow.camera.left = -80;
sun.shadow.camera.right = 80;
sun.shadow.camera.top = 80;
sun.shadow.camera.bottom = -80;
sun.shadow.camera.near = 1;
sun.shadow.camera.far = 400;
sun.shadow.bias = -0.0004;
function placeSun() {
    const dist = 150;
    const v = new THREE.Vector3(-0.55, 1.0, -0.84).normalize();
    sun.position.set(v.x * dist, v.y * dist, v.z * dist);
    sun.target.position.set(0, 0, 0);
}
placeSun();
scene.add(sun);
scene.add(sun.target);

// Grass ground (so shadow lands somewhere when not on water)
const ground = new THREE.Mesh(
    new THREE.PlaneGeometry(400, 400),
    new THREE.MeshLambertMaterial({ color: 0x5f7a44 })
);
ground.rotation.x = -Math.PI / 2;
ground.position.y = -0.05;
ground.receiveShadow = true;
scene.add(ground);

// A few "buildings" — cubes that cast shadows on the water. Plane is 80x80
// (half = 40), basinCurve has its slope between m=0.10..DROP of half-width
// (i.e. world ~4..(DROP*40) from rim). Cubes are placed so their shadows
// FALL ACROSS the slope, which is where bed depth varies → shadow edge
// actually bends. Cube near rim (35,0): shadow stretches inward across slope.
// Cube in deep centre (0,0): shadow on uniformly deep bed (control case).
const cubeMat = new THREE.MeshLambertMaterial({ color: 0xc9b89a });
for (const [x, z, w, h] of [
    [ 30,  -8,  8, 18], // near rim — shadow crosses slope, will visibly bend
    [-28,   6,  6, 12], // near rim opposite side
    [  0,   0, 10, 22], // dead centre — shadow on deep flat bed
    [-10, -28,  7, 14], // near edge again
]) {
    const c = new THREE.Mesh(new THREE.BoxGeometry(w, h, w), cubeMat);
    c.position.set(x, h / 2, z);
    c.castShadow = true;
    c.receiveShadow = true;
    scene.add(c);
}

// Prime the shadow map so makeWater can bind sun.shadow.map.texture.
renderer.render(scene, camera);

// Water plane — 80x80, subdivided 80x80 so depth gradient is smooth.
const waterGeo = buildDemoWaterGeo(80, 80);
const water = makeWater(waterGeo, 0x3f6f9a, sun);
water.rotation.x = -Math.PI / 2;
water.position.y = 0.4;
scene.add(water);

// === Orbit controls (simple inline implementation) =========================
let camYaw = -0.4, camPitch = 0.55, camDist = 90;
let camTarget = new THREE.Vector3(0, 2, 0);
function updateCamera() {
    const x = camDist * Math.cos(camPitch) * Math.sin(camYaw);
    const y = camDist * Math.sin(camPitch);
    const z = camDist * Math.cos(camPitch) * Math.cos(camYaw);
    camera.position.set(camTarget.x + x, camTarget.y + y, camTarget.z + z);
    camera.lookAt(camTarget);
}
updateCamera();

let dragging = null, lastX = 0, lastY = 0;
canvas.addEventListener('mousedown', e => {
    dragging = e.shiftKey ? 'pan' : 'orbit';
    lastX = e.clientX; lastY = e.clientY;
});
window.addEventListener('mouseup', () => { dragging = null; });
window.addEventListener('mousemove', e => {
    if (!dragging) return;
    const dx = e.clientX - lastX, dy = e.clientY - lastY;
    lastX = e.clientX; lastY = e.clientY;
    if (dragging === 'orbit') {
        camYaw -= dx * 0.005;
        camPitch = Math.max(-1.2, Math.min(1.4, camPitch + dy * 0.005));
    } else {
        const right = new THREE.Vector3().subVectors(camera.position, camTarget)
            .cross(new THREE.Vector3(0, 1, 0)).normalize();
        camTarget.addScaledVector(right, -dx * 0.1);
        camTarget.y += dy * 0.1;
    }
    updateCamera();
});
canvas.addEventListener('wheel', e => {
    e.preventDefault();
    camDist = Math.max(5, Math.min(500, camDist * (1 + e.deltaY * 0.001)));
    updateCamera();
}, { passive: false });

// === Resize + render loop ==================================================
function resize() {
    const w = window.innerWidth, h = window.innerHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / h || 1;
    camera.updateProjectionMatrix();
}
resize();
window.addEventListener('resize', resize);

renderer.setAnimationLoop(() => {
    renderer.render(scene, camera);
});
