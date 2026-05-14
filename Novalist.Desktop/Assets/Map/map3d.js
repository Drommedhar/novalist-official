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
import { WaterMesh } from './Water2Mesh.js';
import {
    Fn, vec3, vec4, attribute, screenUV, viewportSafeUV,
    viewportSharedTexture, cameraPosition, positionWorld, normalize, mix,
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
    const WATER_PARALLAX = 0.0;    // max screen-space refraction offset (deep + grazing)
    // How strongly the interior reads as a deep basin: 0 = flat, ~0.7 default,
    // up to ~1.2 for very deep. Scales the shore->interior blend toward the
    // darkened, parallax-shifted bed.
    const BASIN_DEPTH = 1.2;
    const WATER_FALLBACK_COLOR = 0x4a7a9a; // fill colour when a spline has none
    const WATER_DEEP_MULT = 0.02;          // deep-water colour = fill colour * this
    const WATER_DEEP_TINT = 0.85;          // shore opacity: 1 = pure fill colour, 0 = scene shows through
    // Fraction of the way from shore to the deepest interior point at which the
    // water reads fully deep. Lower = far more deep area, a thin shallow rim.
    const WATER_DEEP_REACH = 0.45;
    const WATER_RIPPLE = 10;        // ripple normal strength — higher = choppier, more visible
    const WATER_SHINE = 0.5;       // how much WaterMesh reflection/ripple shows over the depth tint
    const WATER_FLOW = 10;          // river flow strength — how far the ripples scroll downstream
    const WATER_TURB = 2;          // curvature turbulence — cross-flow kick on river bends
    const WATER_FLOW_TAPER = 1.0;  // bank-to-centre speed falloff exponent — higher = sharper fast core
    const WATER_NORMAL_SCALE = 5.0;   // normal-map sample scale (layer 0) — higher = smaller ripples
    const WATER_NORMAL_SCALE2 = 5.75; // layer 1 scale, relative to layer 0 — must differ from 1
    // Ripple distortion on the reflection vs the refraction. The reflection is
    // a mirrored view, so its ripples scroll opposite — at high values they
    // fight the refraction's and read as doubled speed mid-angle. 0 = calm
    // reflection, 1 = upstream behaviour.
    const WATER_REFLECT_DISTORT = 0.8;
    const BACKDROP_Y = -2;         // neutral backdrop ground plane

    let renderer = null, scene = null, camera = null, sceneRoot = null, sun = null;
    let canvas = null;
    let active = false;
    let rendererReady = false;  // WebGPU renderer + scene initialised
    let rafId = 0, lastT = 0;
    let fallbackEl = null;      // shown when WebGPU is unavailable
    const waterNormalsTex = []; // procedural ripple normal maps, cached per variant
    const keys = {};
    let yaw = 0, pitch = -0.5;

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
        scene.background = new THREE.Color(0x9ec7e0); // sky
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
        wireInput();
        rendererReady = true;
        return true;
    }

    // Unit direction the sunlight travels FROM (upper-left, slightly downward).
    const SUN_DIR = (function () {
        const v = { x: -0.55, y: 1.0, z: -0.84 };
        const l = Math.hypot(v.x, v.y, v.z);
        return { x: v.x / l, y: v.y / l, z: v.z / l };
    })();

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
    function makeWater(geometry, fillColor) {
        applyWaterUVs(geometry);
        const col = toColor(fillColor, WATER_FALLBACK_COLOR);
        const water = new WaterMesh(geometry, {
            color: col,
            scale: WATER_NORMAL_SCALE,
            scale2: WATER_NORMAL_SCALE2,
            reflectDistort: WATER_REFLECT_DISTORT,
            flowSpeed: 0.12,    // flow direction comes from the baked `flowDir` attribute
            reflectivity: 0.08,
            normalMap0: waterNormalsTexture(0),
            normalMap1: waterNormalsTexture(1),
        });
        // Visible from above and from below (when flying underwater).
        water.material.side = THREE.DoubleSide;
        const surface = water.material.colorNode; // WaterMesh reflection + ripple
        const deep = col.clone().multiplyScalar(WATER_DEEP_MULT);
        const shallowRGB = vec3(col.r, col.g, col.b);
        const deepRGB = vec3(deep.r, deep.g, deep.b);
        water.material.colorNode = Fn(() => {
            // Baked shore distance, scaled + clamped: 0 at the shore, 1 deep.
            const d = attribute('waterDepth').mul(BASIN_DEPTH).clamp(0, 1).toVar();
            // View + depth parallax on the refracted scene below (0 = straight
            // down; grazing angles over deep water shift the scene most).
            const eye = normalize(cameraPosition.sub(positionWorld));
            const parUV = screenUV.sub(eye.xz.mul(d).mul(WATER_PARALLAX));
            const bed = viewportSharedTexture(viewportSafeUV(parUV)).rgb;
            // Shallow shore = mostly fill colour (scene faintly through);
            // deep interior = the dark deep-water colour. Deep is always
            // darker than shallow, so depth reads the right way round.
            const shallow = mix(bed, shallowRGB, WATER_DEEP_TINT);
            const depthColor = mix(shallow, deepRGB, d);
            // WaterMesh's reflection + ripple highlights composited on top.
            return vec4(mix(depthColor, surface.rgb, WATER_SHINE), 1.0);
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
        tris = subdivideTris(tris, 3);
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
        const reach = maxD * WATER_DEEP_REACH;
        const depth = dist.map(dd => smoothstep01(dd / reach));
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
        const CROSS = [-1, -0.6, -0.25, 0, 0.25, 0.6, 1];
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
                    // Flow in the geometry's local XY space (x = world x,
                    // y = -world z), negated so WaterNode's built-in
                    // `flow.x *= -1` lands it pointing downstream.
                    fx: -fwx * mag, fy: -fwy * mag,
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
                    const fillColor = (prof.bands && prof.bands[0] && prof.bands[0].color)
                        || '#cccccc';
                    const casingColor = (prof.casing && prof.casing.color) || '#555555';
                    const isRiver = sp.kind === 'river';
                    // Blend like the 2D renderer: ALL casings render before ALL
                    // fills, so a crossing spline's fill always covers another's
                    // casing — junctions read as merged, not stacked.
                    const ro = order;
                    order++;
                    if (isRiver) {
                        // Rivers / lakes: a reflective WaterMesh surface whose
                        // geometry carries a baked shore-distance — the shader
                        // fakes the recessed basin (no carved terrain). Surface
                        // geometry is local XY, rotated -90 degrees so +Z is up.
                        let geo = null;
                        if (sp.closed && samples.length >= 3) {
                            geo = buildLakeWaterGeo(samples.map(s => ({ x: s.x, y: s.y })));
                        } else {
                            geo = buildRiverWaterGeo(samples);
                        }
                        if (geo) {
                            const water = makeWater(geo, fillColor);
                            water.rotation.x = -Math.PI / 2;
                            water.position.y = WATER_SURFACE_Y;
                            water.renderOrder = RO_SPLINE + 4000 + ro;
                            root.add(water);
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

    function wireInput() {
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
            keys[e.code] = true;
            // Esc releases pointer lock (browser already does this); swallow so
            // it doesn't bubble to the 2D editor's Esc handlers.
            if (e.code === 'Escape') e.stopPropagation();
        });
        window.addEventListener('keyup', function (e) { keys[e.code] = false; });
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

    function tick(t) {
        if (!active) return;
        rafId = requestAnimationFrame(tick);
        const dt = Math.min(0.05, (t - lastT) / 1000 || 0);
        lastT = t;
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
        camera.lookAt(camera.position.clone().add(camDir()));
        updateSunShadow(); // shadow frustum tracks the camera
        // WaterMesh advances its own flow animation via the renderer's
        // updateBefore hook — no manual time stepping needed here.
        renderer.render(scene, camera);
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
            resize();
            buildScene();
            frameCamera();
            lastT = performance.now();
            rafId = requestAnimationFrame(tick);
            try { sendMessage({ type: 'map3dEntered' }); } catch (e) {}
        },
        exit: function () {
            active = false;
            if (rafId) cancelAnimationFrame(rafId);
            rafId = 0;
            if (document.pointerLockElement) document.exitPointerLock();
            if (canvas) canvas.style.display = 'none';
            if (typeof stage !== 'undefined') stage.style.display = '';
            if (typeof hud !== 'undefined' && hud) hud.style.display = '';
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
