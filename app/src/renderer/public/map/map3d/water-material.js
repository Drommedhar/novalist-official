import * as THREE from "../three.webgpu.min.js";
import { waterNormalsTex, waterState } from './water-state.js';
import {
    BASIN_DEPTH,
    WATER_BED_DEPTH,
    WATER_CAUSTIC,
    WATER_CAUSTIC_COLOR,
    WATER_CAUSTIC_DEPTH_SCALE,
    WATER_CAUSTIC_MIN_SCALE,
    WATER_CAUSTIC_PARALLAX,
    WATER_CAUSTIC_SCALE,
    WATER_CAUSTIC_SHARP,
    WATER_CAUSTIC_SPEED,
    WATER_CAUSTIC_TINT,
    WATER_DEEP_MULT,
    WATER_DEEP_TINT,
    WATER_FOAM_COLOR,
    WATER_FOAM_DRIFT,
    WATER_FOAM_SCALE,
    WATER_FOAM_SPEED,
    WATER_FOAM_WIDTH,
    WATER_NORMAL_SCALE,
    WATER_NORMAL_SCALE2,
    WATER_REFLECT_DISTORT,
    WATER_RIPPLE,
    WATER_SCATTER,
    WATER_SCATTER_COLOR,
    WATER_SCATTER_POWER,
    WATER_SHADOW_DARKEN,
    WATER_SHADOW_DEPTH_FADE,
    WATER_SHINE,
    WATER_UV_TILE,
    WATER_WAVE_DEEP,
    WATER_WAVE_SHALLOW,
    WATER_WAVE_SPEED,
} from './water-settings.js';
import { WaterMesh } from '../Water2Mesh.js';
import {
    Fn,
    attribute,
    cameraPosition,
    mix,
    normalize,
    positionLocal,
    positionWorld,
    screenUV,
    shadow,
    texture,
    time,
    uv,
    vec2,
    vec3,
    vec4,
    viewportSafeUV,
    viewportSharedTexture,
} from "../three.tsl.min.js";
import { toColor } from './map-content.js';
import { getSunDirUniform } from './sky.js';
import { sceneState } from './scene-state.js';

// Tileable ripple normal maps for the water shader, generated on a canvas
// so no binary texture asset needs bundling. Height field is tileable
// fractal value-noise (a stack of integer-frequency interpolated random
// grids) — organic, no directional streaks and no obvious repeat, unlike a
// sum of pure sines. `variant` (0 or 1) reseeds for a second distinct map.
// Normals come from central differences. Built once per variant and cached.
export function waterNormalsTexture(variant) {
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

export function causticTexture() {
    if (waterState._causticTex) return waterState._causticTex;
    const t = new THREE.TextureLoader().load('caustics.jpg');
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.colorSpace = THREE.SRGBColorSpace;
    waterState._causticTex = t;
    return t;
}

// Rewrite a water surface geometry's uv from its local XY position so the
// ripple normal map tiles every WATER_UV_TILE world units, regardless of
// how big the river / lake is. Both the ribbon BufferGeometry and the lake
// ShapeGeometry are authored in local XY (x = world x, y = -world z).
export function applyWaterUVs(geo) {
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
export function makeWater(geometry) {
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
    applyWaterWaves(water);
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
    applyWaterSurfaceColor({
        water, surface, shallowRGB, deepRGB, foamRGB, scatterRGB,
        causticRGB, sunDirVec, foamNoiseTex, causticTex,
    });
    return water;
}


function applyWaterWaves(water) {
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
}


function applyWaterSurfaceColor({ water, surface, shallowRGB, deepRGB, foamRGB, scatterRGB, causticRGB, sunDirVec, foamNoiseTex, causticTex }) {

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
        const { caustic, cMask, sunOverhead } = waterCausticSample(eye, d, sunDirVec, causticTex);

        // Shadow mask via three.js's built-in TSL shadow(). The sampling
        // position is overridden on the material below (receivedShadow-
        // PositionNode = bedPos), so this samples the shadow at the
        // virtual bed under the water — building shadows bend into the
        // deeper part as in the demo.
        const shadowMask = sceneState.sun && sceneState.sun.castShadow ? shadow(sceneState.sun) : sunOverhead.mul(0).add(1);

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
}


function waterCausticSample(eye, d, sunDirVec, causticTex) {

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
    return { caustic, cMask, sunOverhead };
}
