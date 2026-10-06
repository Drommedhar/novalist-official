import * as THREE from "../three.webgpu.min.js";
import { elementShown, mapLayers, toColor } from './map-content.js';
import { mapHost } from './host.js';
import {
    bakeColorAttribute,
    buildLakeWaterGeo,
    buildRiverWaterGeo,
    mergeWaterGeometries,
    rebakeMergedWaterAttrs,
} from './water-geometry.js';
import { riverOutlinePolygon } from './geometry.js';
import { WATER_FALLBACK_COLOR, WATER_SURFACE_Y } from './water-settings.js';
import { RO_SPLINE } from './scene-settings.js';
import { makeWater } from './water-material.js';

// Flat ground ribbon along a sampled spline centreline. `samples` are the
// {x,y,w} points from sampleSpline (x/y are 2D world; y here is the south
// axis). halfWidthFn(i) → half-width at sample i. yPos lifts the ribbon
// above the terrain; po is the polygonOffset factor.
export function ribbonMesh(samples, halfWidthFn, yPos, color, renderOrder, opacity) {
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
export function buildSplines(root, visIds) {
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
                const prof = mapHost.splineProfile(sp.kind, sp.preset);
                const straight = !!prof.straight;
                const samples = mapHost.sampleSpline(pts, straight, !!sp.closed);
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
