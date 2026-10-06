import { elementShown, mapLayers } from './map-content.js';
import { mapHost } from './host.js';
import { ribbonOutlinePolygon } from './geometry.js';

// Shared billboard mesh: one InstancedMesh whose instance buffer is
// rewritten each tick from updateGrassBillboards(). The geometry is a
// GRASS_TILE_SIZE × GRASS_TILE_SIZE quad laid flat on +Y. Per-instance
// tint multiplies the texture sample so each tile keeps its shape colour.
export function forEachSplineOutline(visIds, pushOccluder) {
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const sp of n.splines || []) {
                if (!elementShown('spline', sp.id, n.id, visIds)) continue;
                const pts = sp.points || [];
                if (pts.length < 2) continue;
                const prof = mapHost.splineProfile(sp.kind, sp.preset);
                const samples = mapHost.sampleSpline(pts, !!prof.straight, !!sp.closed);
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
}

export function bboxOf(poly) {
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    for (const p of poly) {
        if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
        if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
    }
    return { minX, maxX, minY, maxY };
}
